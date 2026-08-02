# Pupu Desktop 1.1.0 代码实现设计说明

## 1. 工程结构

| 模块 | 职责 |
|---|---|
| `Pupu.Behavior` | 无 WPF 依赖的状态、决策、感知、互动、记忆与迁移核心 |
| `Pupu.Desktop` | WPF 窗口、输入、动画、桌面移动、面板与本地持久化 |
| `Pupu.Tests` | 固定时钟/随机源的分布、生命周期和传感器轨迹回放 |
| `Assets` | 外部 PNG 图集和 `pupu-assets.json` |

## 2. 行为选择管线

```text
PerceptionEvent + RuntimeState + Relationship + DerivedPreference
    -> EligibilityFilter
    -> UtilityScoring
    -> SelectionPolicy
    -> ActionScheduler
    -> Animation / Movement / InteractionRecord
```

### EligibilityFilter

执行硬条件：移动权限、当前动作是否可中断、最短驻留、主人诉求冷却、未回应抑制、深夜、勿扰、会议、全屏、高压力、高疲劳、低安全感和 RequiredSignals。未通过的候选不进入评分。

### UtilityScoring

对合格候选计算：

`BaseWeight + TemperamentAffinity + RuntimeStateFit + RelationshipFit + LearnedPreference + ContextFit - CooldownPenalty - RepetitionPenalty - InterruptionCost + SeededJitter`

派生偏好由索引字典按 `behavior_id|interaction_type|context` 读取，不遍历原始记录；显式偏好与派生偏好的合计影响受上限约束。

### SelectionPolicy

将最高分附近的候选组成 top band，再用温度权重和可注入 `IRandomSource` 选择。相同状态、时钟和随机种子产生相同结果。

### ActionScheduler

状态为 Entering、Looping、Exiting、Completed、Interrupted。一级“停下”向当前动作发送取消并安全收尾。睡眠姿势切换先播放过渡序列；循环序列使用往返帧，避免末帧跳回。

## 3. RuntimeState

维度：arousal、stress、social_desire、play_desire、curiosity、fatigue、safety。

- `AdvanceActive` 每次最多推进 5 分钟。
- `MaximumEventDelta=0.18`；五分钟单位变化上限 `0.12`。
- 敏感度和安全感影响压力恢复；活泼、淘气影响玩耍/好奇目标；状态之间只做有限耦合。
- `MarkSuspended` 记录暂停点。
- `RestoreAfterResume` 最多按两小时恢复窗口计算，并对压力、疲劳、安全感设置更小的单次上限。
- 离线不逐分钟模拟，不生成饥饿、猫砂、关注欠账或关系惩罚。

## 4. InteractionSession 与生命周期

`InteractionSessionManager` 使用 2.4 秒连续触摸间隙，将多个 touch/stroke/hold/release 合并为同一会话。宠物主动靠近、用户回应和自然结束也保留一个会话 ID。

长动作记录 Started、Progressed、Completed、Interrupted、Failed。效果在动画节点逐步写入；中断只保留已发生效果，记录 completion_ratio、interrupt_reason 和 applied_effects。

## 5. InteractionRegionMap

区域使用 0–1 归一化坐标，按 sequence/pose 选择区域集；左向时镜像 X，按帧呼吸/步态修正 Y，再由显示尺寸映射指针坐标。区域类型包括 Head、Body、Paw、Tail、MoveHandle，并标记是否支持 Lift。

输入区分：

- 短点：TouchPet。
- 连续路径：StrokePet。
- 长按：HoldPet。
- 支持抬起的身体区域长按：LiftPet。
- 600ms 内超过拖动距离：MoveWindow。
- 松开：Release。

## 6. PerceptionEvent

字段：Id、Timestamp、Source、Kind、Confidence、Ttl、DeduplicationKey、Priority、Intensity、Metadata。

`PerceptionEventProcessor`：

- 拒绝未来异常时间和已过 TTL 的事件。
- 180ms 内相同去重键合并。
- 12 秒窗口记录重复刺激。
- 非 Important/Safety 事件按重复次数习惯化。
- Snapshot 按优先级和有效强度排序。

允许的当前来源是 pointer 与 operating_system。普通键鼠不能产生 meeting、emotion、screen_content；会议/勿扰只能来自主人规则或明确系统上下文。

## 7. 四层记忆

| 层 | 用途 |
|---|---|
| `ConfirmedProfileFact` | 主人确认事实，解析时最高优先级 |
| `RawInteractionEvent` | 可删除的原始证据，带 session/opportunity/outcome |
| `EpisodicMemory` | 同会话事件整理后的情景摘要 |
| `DerivedHabitPreference` | 行为决策直接读取的索引偏好 |

习惯门槛：至少 6 个会话样本、3 个不同日期和 6 次机会。权重还考虑 outcome、context consistency 和 contradictory samples；反向证据使用更慢的 alpha。单条派生偏好影响上限 0.42。

`Maintain` 负责重建、容量限制与清理；情景记忆可固定。`DeleteEvidence` 写入删除墓碑、删除相关情景并重算 key。墓碑阻止下次整理恢复已删证据。ConfirmedProfileFact 的解析优先于推断事实。

## 8. 动作与素材

新增 `pupu-activity-youthful-v7.png`：2048×1536，8×6，单格 256：

1. laser-wiggle-chase
2. laser-paw
3. sleep-curled
4. sleep-belly-up
5. sleep-side
6. sleep-transition

素材通过 `pupu-assets.json` 注册，`AssetPackService` 校验文件、网格和尺寸；ViewModel 只按 atlas ID、row 和 frame 读取。激光两行动画及睡眠循环使用往返序列。生成源使用内置图像生成、三张实拍面部参考、纯绿色背景和本地色键透明化；`rebuild-v7-activity.sh` 以整行动作统一比例、底部锚点和 20px 安全区归一化为 RGBA。

## 9. 桌面接线

- `MainWindow.xaml.cs` 提供指针、显示器、系统时间、休眠/唤醒事件。
- `MainViewModel` 将感知信号放入 BehaviorContext，调用统一选择器并映射动画。
- `MemoryEngine` 负责状态恢复、四层记忆持久化和事件记录。
- `EnvironmentContextService` 只提供可验证的全屏信号，不读取屏幕内容。
- 右键命令设置 `RequestSource=Owner`；自主管线不能直接启动 care/walk/toy 服务。

## 10. 测试

25 项自动测试覆盖原 13 项验收和新增：

- 安全行为在评分前硬过滤。
- top band 固定种子可复现。
- 状态恢复和 resume 有界。
- 鼠标反复经过后习惯化。
- 休眠、唤醒、系统时间和显示器事件回放。
- 连续触摸只形成一个会话。
- 未回应主动诉求不损失信任。
- 删除证据级联删除派生偏好。
- 主人确认事实优先。
- 区域随姿势、帧、方向和缩放。
- 调度器进入/循环/退出/中断阶段。
- 白天多姿势睡眠与夜间安静活动分布。

构建命令：

```powershell
dotnet build .\Pupu.sln -c Release
dotnet run --project .\Pupu.Tests\Pupu.Tests.csproj -c Release
.\scripts\verify-bindings.ps1
.\scripts\verify-assets.ps1
.\scripts\build-windows.ps1
```

## 11. 已知边界

- 三张实拍原图作为开发参考随源码包保存，但不随运行应用包发布，避免运行时携带无关隐私素材；正式应用只包含派生透明动作图集。
- 互动区域是动作包元数据模型，但当前内置区域仍为规则化矩形；高精度多边形/逐帧人工标注可后续加入清单。
- 会议模式来自主人明确设置，不从键鼠推断。
- Linux 构建环境不能执行真实 WPF UI 自动化，仍需 Windows 10/11 做休眠、多显示器和高 DPI 冒烟测试。
