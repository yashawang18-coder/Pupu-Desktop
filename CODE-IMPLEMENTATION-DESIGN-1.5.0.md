# Pupu Desktop 1.5.0 代码实现设计说明

## 1. 版本范围

1.5.0 在现有 WPF、PersonalityBehaviorV2、动作调度、窗口感知、外部图集和本地 Markdown 记忆架构上增量实现：

- V11 拿破仑矮脚身份约束与三张运行图集；
- 八方向地面移动和扫帚飞行；
- 每日自主如厕、低频舔毛、蓝色小窝；
- 魔法视觉、真实坐标移动与动作收尾；
- 本地相册、档案画像、性格摘要与关系阶段；
- 多服务商模型协议、短期会话和相册视觉输入。

不修改旧版本文档，也不把魔法服装或装备作为独立图层叠到现有猫身上。

## 2. 素材清单与运行时映射

### 2.1 清单

`Pupu.Desktop/Assets/pupu-assets.json` 继续是唯一图集文件名和网格登记。1.5.0 新增或替换：

```text
lifeEquipment -> pupu-life-equipment-youthful-v11.png -> 4 rows × 8 columns
motion        -> pupu-motion-youthful-v11.png         -> 11 rows × 8 columns
specials      -> pupu-specials-youthful-v11.png       -> 5 rows × 8 columns
```

`AssetPackService` 把三项加入必需图集校验；无效自定义素材包回退内置 1.5.0 清单。`Pupu.Desktop.csproj` 以外部 `Content` 方式发布 V11 PNG，不把图集嵌入程序集。

### 2.2 `SpriteAtlas`

`SpriteAtlas` 增加 `LifeEquipment` 和 `Motion`，`SheetFor()` 分别映射到清单 ID。核心序列映射为：

| 行为 | 图集行 |
| --- | --- |
| `routine.toilet` 进入、如厕、概率抬头、埋砂、离开 | `lifeEquipment:0` |
| `self.groom` 舔胸毛、侧腹和回身梳理 | `lifeEquipment:1` |
| `rest.bed` 入窝与慢呼吸睡眠 | `lifeEquipment:2` |
| 孔雀蓝牵引绳互动 | `lifeEquipment:3` |
| 背带左前、右前、左后、右后 | `motion:0–3` |
| 无背带左前、右前、左后、右后 | `motion:4–7` |
| 扫帚八方向 | `motion:8` |
| 矮脚侧躺微动 | `motion:9` |
| 窗口上沿矮脚趴卧 | `motion:10` |
| 八方向视线与四种魔法 | `specials:0–4` |

同一动作行使用往返或闭合相位序列。移动方向变化通过归一化帧位置保留步态相位，避免每次转向从第 0 帧重新起步。

## 3. V11 素材重建

`scripts/rebuild-v11-assets.sh` 读取 `AssetSources/v11` 的纯绿色色键源并执行：

1. 色键移除、去绿边、轻量边缘收缩；
2. 按真实源图行列提取主体；
3. 同一动作行计算统一缩放率；
4. 以 256×256 单格、20px 底部安全区统一落点；
5. 拼接为 8 列正式 RGBA 图集；
6. 完整解码、尺寸和透明通道预检；
7. 先写入 staged 文件，再原子替换正式 PNG。

V10 已验收的低趴视线行原样进入 V11 Specials 第 0 行。魔法四行重新构建；石化行明确选取五个渐进猫身阶段和三个不同银币高光阶段，避免一格出现两个主体。

## 4. 每日自主如厕

### 4.1 数据模型

`DailyToiletPlan` 持久化：

```text
LocalDate
TargetCount
Slots[]
  Id
  ScheduledAt
  Status = Pending | Reserved | Completed | Skipped
  UpdatedAt
```

`PetState.DailyToiletPlan` 向后兼容；旧状态没有计划时按本地日期自动创建。

### 4.2 `DailyToiletPlanner`

- `EnsurePlan()` 每天随机生成 2–3 个时隙；
- 计划从本地上午 8 点后开始；当天较晚启动时从当前时间后继续分桶；
- `DueWindow` 默认 45 分钟；
- `TryReserveDueSlot()` 在动作第一帧前预留，防止崩溃重复；
- `TryCompleteSlot()` 只接受已预留时隙；
- `ExpireMissed()` 和 `SkipPastPending()` 把错过的时隙标为 `Skipped`，不离线补做；
- 同一日期的有效计划具有幂等性。

`BehaviorCatalog` 注册 `routine.toilet`，并要求 `toilet_due` 硬信号。它仍经过 EligibilityFilter、UtilityScoring、SelectionPolicy 和 ActionScheduler，不从评分管线旁路触发。

### 4.3 动作链

`RunAutonomousToiletAsync()` 在开始前保存 Reserved 状态，然后依次播放：

```text
enter -> relieve -> optional look-up -> mandatory bury -> exit
```

抬头使用低概率角色变体。排泄已经提交但动作中断时，`finally` 中播放短版埋砂收尾；埋砂完成后可把时隙标为 Completed。手动清理命令保留兼容边界但不在用户界面暴露。

## 5. 低频梳理与蓝色小窝

`BehaviorCatalog` 新增或强化：

```text
self.groom
  passive = true
  minimum dwell >= 75s
  cooldown >= 10min
  high stress penalty

rest.bed
  passive = true
  movement = false
  minimum dwell >= 7min
  cooldown >= 15min
  fatigue/safety positive, arousal/stress negative
```

两项继续读取具体 LearnedPreference、近期历史、冷却和重复抑制。`rest.bed` 使用包含垫子和猫身的完整预设帧，不建立独立装备叠加层。

## 6. 八方向真实移动

### 6.1 方向枚举

`PetDirection` 扩展为：

```text
Left, Right, Up, Down,
UpLeft, UpRight, DownLeft, DownRight
```

`DirectionForVector(deltaX, deltaY)` 使用主轴阈值把位移向量映射到八个扇区。主轴明显占优时使用四向旧图；其余方向使用 V11 斜向步态。

### 6.2 地面移动

`MainWindow.ViewModel_DesktopMoveRequested()` 每段实时生成随机目标点，计算距离、弯曲法线、小跳和帧数，并在每个 16ms 帧真实更新 WPF `Left` / `Top`。路线不持久化，也不允许只换图不改坐标。

背带和无背带根据模式分别选择 `motion:0–3` 和 `motion:4–7`；水平/垂直主方向继续使用已验收的 WalkModes 四向行。`PlayMovementSequence()` 保留归一化步态相位。

## 7. Windows 窗口环境

`EnvironmentContextService` 使用 Win32：

- `EnumWindows`、`GetWindowRect`；
- `GetForegroundWindow`；
- `MonitorFromWindow`、`GetMonitorInfo`；
- `GetDpiForWindow` / `GetDpiForMonitor`；
- 窗口可见、最小化、工具窗口和进程过滤。

`WindowSurfaceSnapshot` 保存 HWND、窗口矩形、监视器工作区、进程类别、浏览器标记、前台状态和采集时间。几何从物理像素转换为 WPF DIP。

候选优先同屏、浏览器和前台窗口。窗口上沿动作必须具有新鲜 `window_edge_available` 信号。目标失效时：

1. 清空被跟踪 HWND；
2. 通知 ViewModel 结束窗口表面行为；
3. 恢复普通待机。

非窗口移动和魔法开始前同样清空旧句柄，避免过期窗口几何覆盖新位置。

## 8. 魔法实现

### 8.1 `Accio Broom`

`DesktopMoveMode.BroomFlight` 固定在当前监视器工作区，并持续到请求的一分钟时限：

- 每段随机选择与当前方向不同、距离足够大的目标；
- 根据八方向向量选择 `motion:8` 的对应飞行姿势；
- `AnimateBroomCurveAsync()` 使用 smoothstep、法线弧线和轻微 flutter；
- 时长按约 `distance × 0.82ms` 计算并限制在 260–920ms；
- 段间只保留极短随机间隔，显著快于普通步行。

### 8.2 `Apparate`

`MoveApparateAsync()` 在当前工作区内选择与原位置有最小距离的随机点：

1. 施法序列播放扩大弧光；
2. 透明度平滑从 1 降到 0；
3. 隐藏约 2.1 秒；
4. 更新真实 `Left` / `Top`；
5. 平滑恢复透明；
6. 维持魔法重现帧约 1.5 秒；
7. 再恢复普通低趴。

### 8.3 `Petrificus Totalus`

石化序列完成后设置 `_isPetrified` 并循环播放银币高光。普通行为被阻止，直到 `ReleasePetrificationAsync()`：

1. 清除石化状态；
2. 播放 Core 第 5 行完整伸展；
3. 完成原互动生命周期；
4. 恢复普通低趴。

一级“停下”复用同一解除路径。

### 8.4 `Scourgify`

`DesktopMoveMode.EdgePolish` 优先刷新真实目标 HWND 的几何，并依次沿上、右、下、左边缘移动；无目标时沿当前显示器工作区边缘。V11 Specials 第 4 行提供放大光环和擦拭流光。实现不调用 Shell API 改动桌面图标。

该动作由 `ScourgifyAsync()` 真实发出 `DesktopMoveRequested`，窗口层负责完成坐标移动并回传结果，修复只播放施法动画却没有桌面反馈的旧路径。无论完成、中断或异常，窗口透明度和移动占用都会在收尾阶段恢复。

## 9. 相册与档案展示

### 9.1 数据模型

`albums.json` 使用 `PhotoAlbumCatalog`：

```text
RootDirectory
Albums[]
  Id, Name, RelativeDirectory
  Theme, StartDate, EndDate, GrowthStage
ProfilePresentation
  RelationshipStageOverride
```

`PhotoAlbumService` 只保存索引：

- 根目录必须真实存在；
- 子相册路径必须是相对路径，规范化后不得逃出根目录；
- 删除子相册只删索引；
- 扫描忽略重解析点、系统文件和无权限目录；
- 目录失联时返回不可用快照，但保留元数据；
- 写入使用临时文件替换。

检索匹配文件名、相对路径、相册名、主题和成长阶段，并按本地文件日期过滤，最多返回 1000 条；面板最多展示前 300 条。

### 9.2 档案页

档案顶部画像从当前 Core 图集裁取，不创建第二套身份图。`AutomaticPersonalitySummary` 读取五维天生性格和已形成的跨天偏好，且只生成说明文字，不回写天生性格。

自动好感阶段根据长期关系计算。主人保存的阶段覆盖只影响 `RelationshipStageDisplay`，不修改底层 Trust、InitiativeAcceptance 或 TouchAcceptance。

### 9.3 高级调试和节日限制

`ControlWindow.xaml` 的调试项使用 `DebugCard` 圆角卡片和 `WrapPanel`。节日项使用独立纯文字数据模板，不创建 `Thumbnail` 或 `PreviewCommand`；日期播放仍只从 `DailySpecialRules` 进入。

## 10. 多服务商模型协议

### 10.1 设置

`ModelApiSettings` 新增：

```text
Provider = OpenAI | Qwen | DeepSeek | Custom
ApiFormat = OpenAiChat | OpenAiResponses
VisionEnabled
VisionModel
SendAlbumImages
ConversationTurns = 8..12
OmitTemperature
```

旧设置没有显式 Provider / ApiFormat 时，根据 Endpoint 迁移。每个服务商预设包含默认端点、默认格式及 Responses / Vision 能力。UI 在切换预设时更新能力说明。

### 10.2 协议适配

`ModelProtocolAdapter` 是无网络、无凭据、无文件系统依赖的纯翻译层：

- Chat Completions：`system + history + user`；
- Responses：`instructions + input`；
- 将本地 `owner/pupu` 角色映射为 `user/assistant`；
- 支持 Chat 的 `image_url` 和 Responses 的 `input_image`；
- 同时解析 `choices[].message.content`、`output_text` 和 `output[].content`。

服务商能力限制在发送前校验。DeepSeek 当前预设不声明 Responses 或视觉能力；需要其他兼容能力时使用明确支持的预设或 Custom。

### 10.3 凭据与网络

密钥目标由“Provider + Endpoint”散列隔离。旧单目标凭据在迁移期只作为兼容读取来源。远程端点必须 HTTPS，本机回环地址可使用 HTTP。

`ModelApiService` 对 429 和 5xx 最多重试 2 次：

- 优先遵守 `Retry-After`；
- 没有响应头时使用有界指数等待；
- 服务端要求等待超过 30 秒时终止本轮，不长期阻塞 UI。

模型文字仍经 `PetSpeechComposer.TryNormalizePetReply()` 检查，越界回复不进入宠物气泡。

## 11. 长短期记忆与相册视觉

### 11.1 短期会话

`ConversationSessionStore` 使用 `%LOCALAPPDATA%\PupuDesktop\memory\conversation.json`：

- 保留最近 8–12 个主人轮次，默认 10；
- 一轮通常是一条 owner 加一条 pupu；
- 单条文字规范化并限制长度；
- 临时文件写入后原子替换；
- 损坏 JSON 备份后以空会话恢复；
- 启动时恢复，成功回复和本地回退都追加。

### 11.2 长期上下文

`MemoryEngine.BuildChatContextAsync()` 继续提供档案、性格、状态、关系、确认事实、情景摘要、习惯偏好和主人可编辑 Markdown。短期会话不改变天生性格，也不取代长期记忆。

### 11.3 相册检索

`BuildAlbumConversationMemoryAsync()` 只在主人话题出现照片、相册、回忆、日期、成长阶段等线索时运行：

1. 解析关键词、主题和日期范围；
2. 从本地索引选择相关照片；
3. 将相册名、日期、主题和成长阶段写入有限文字上下文；
4. 明确要求模型不要猜测未提供的画面；
5. 若视觉授权开启，最多选择 2 张图片构造 `data:image/...;base64`。

图片发送还要求：

- `VisionEnabled == true`；
- `SendAlbumImages == true`；
- 扩展名为 JPEG、PNG 或 WebP；
- 单图大于 0 且不超过 6 MB；
- 完整路径仍位于已链接的根相册内。

本地绝对路径不会进入模型上下文。

## 12. 测试与发布验收

`Pupu.Tests` 共 38 项且全部通过，1.5.0 重点覆盖：

- 每日如厕计划 2–3 次、同日幂等和离线跳过；
- `routine.toilet` 的 `toilet_due` 硬门槛；
- `rest.bed` 的疲劳、安全、驻留和冷却；
- `self.groom` 的原地、低频、压力抑制；
- 既有状态参与、每日魔法、节日日期和档案语言回归。

本轮机器验收结果为 `38/38`；576 格最小有效主体 `69×100px`、最低清晰度 `111.3`、最大移动体型漂移 `1.236×`、最大相邻中心位移 `9.01px`。

发布链还应执行：

- WPF Release 编译；
- XAML 绑定检查；
- 清单与 576 格图集检查；
- Windows x64 自包含发布；
- ZIP 完整解压；
- PE GUI 架构检查；
- V11 在工作区、应用包和源码包中的哈希一致性检查。
