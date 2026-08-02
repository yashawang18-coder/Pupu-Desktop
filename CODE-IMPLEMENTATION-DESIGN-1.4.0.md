# Pupu Desktop 1.4.0 代码实现设计说明

## 1. 版本范围

1.4.0 在现有 WPF、PersonalityBehaviorV2、动作调度、窗口感知和九宫格动画架构上增量实现档案、状态参与、全局视线、魔法和日历特辑。没有替换 V9/V8 运行图，也没有改动既有 256×256 单格协议。

## 2. 档案数据模型

`PetProfile` 新增并持久化：

```text
ChineseName
EnglishName
Breed
Sex
Birthday
OwnerNickname
RelationshipToOwner
OwnerBirthday
```

`Name` 继续保留为旧 `profile.json` 的兼容字段，并在规范化时与 `EnglishName` 同步。

`PetProfile.Normalize()` 负责：

- 空值回退；
- 去除多余空白；
- 文本长度限制；
- 日期收敛到 1900–2100；
- 对主人昵称为空时保持空值，由 `OwnerAddress` 在运行时返回“主人”。

`SelfIdentity` 汇总完整档案，供本地记忆和模型系统提示使用。`Clone()` 防止 WPF 编辑状态直接修改已保存档案。

## 3. 档案保存路径

`MainViewModel` 持有 `_editableProfile`，各字段使用显式 `TwoWay` 绑定。`SavePetProfileAsync()` 调用 `MemoryEngine.SaveProfileAsync()`：

1. 规范化编辑副本；
2. 复制到当前 `Profile`；
3. 为姓名、品种、性别、生日、昵称、关系和主人生日写入 `ConfirmedProfileFact`；
4. 持久化 profile、PersonalityBehaviorV2 与 Markdown；
5. 刷新面板绑定；
6. 重新检查当日生日。

`MemoryEngine.BuildChatContextAsync()` 写入 `Profile.SelfIdentity` 和主人生日。`MainViewModel` 给 `ModelApiService` 的 `petIdentity` 参数也改为 `SelfIdentity`，因此模型和本地气泡使用同一份身份事实。

`PetSpeechComposer.Compose()` 接收 `petName` 与 `ownerAddress`。内置台词和安全通过的作者台词都会把旧的“朴朴 / pupu / 主人”替换成档案称呼；技术语言拦截规则保持不变。

## 4. 状态参与评估

`OwnerInteractionParticipationEvaluator` 位于行为核心，不依赖 WPF。输入包括：

- `PersonalityBehaviorState`；
- `OwnerInteractionKind`；
- `OwnerInteractionContext`；
- 可注入的 0–1 随机值。

不同互动使用不同连续公式。例如投喂提高饥饿权重，散步和玩具提高精力、玩耍意愿与好奇权重，梳毛读取信任和触摸接受，魔法读取淘气、活泼、好奇与安全感。最终概率限制在 0.08–0.96，避免绝对服从或永久拒绝。

`MainViewModel.TryParticipateAsync()` 在动作创建、调度和状态效果之前调用评估器。拒绝时：

- 不调用 `BeginAction()`；
- 设置单独的 `interaction.refused.*` 行为标识；
- 生成角色理由；
- 写入中性 `interaction_refused` 事件；
- 关系变化为 0。

## 5. 日历规则

`DailySpecialRules` 提供纯函数：

- `HolidayFor()`：圣诞、万圣节和春节；
- `IsOwnerBirthday()`；
- `OwnerAgeOnBirthday()`；
- `CanTriggerAutonomousMagic()`；
- `WasTriggeredToday()`。

春节使用 .NET `ChineseLunisolarCalendar` 判断农历一月初一。所有“同日”判断使用应用时钟的本地 `Date`，不使用 UTC 日期。

`PetState` 新增：

```text
LastAutonomousMagicAt
LastSeasonalOutfitAt
LastBirthdayGreetingAt
```

状态文件向后兼容，旧版本缺少字段时按空值处理。

## 6. 鼠标视线管线

`MainWindow` 创建 90ms `DispatcherTimer`，用 Win32 `GetCursorPos` 获得全局鼠标位置，再用 `PetImage.PointToScreen()` 得到宠物画面物理坐标。距离和方向计算考虑当前 WPF DPI。

方向映射：

```text
中心 → 0
左 → 1
左上 → 2
上 → 3
右上 → 4
右 → 5
右下 → 6
左下 → 7
```

`MainViewModel.UpdateCursorGaze()` 只在可用状态保存当前行为快照、切到 Specials 第 0 行指定帧；鼠标离开或其他动作开始时恢复行为、标签、互动上下文、动画来源与先前序列。`BeginAction()` 会先结束视线覆盖，避免长动作结束后恢复到错误帧。

视线事件同时进入现有 `PerceptionEventProcessor` 的 `mouse_nearby` 去重通道，但不直接触发位置移动。

## 7. 魔法动作调度

### 7.1 素材序列

`SpriteAtlas` 新增 `Specials` 与 `Seasonal`。`SheetFor()` 映射到素材清单 ID。魔法序列：

- `AccioBroomIntroSequence` + `BroomFlightSequence`；
- `ApparateSequence`；
- `PetrifySequence` + `SilverCoinSequence`；
- `ScourgifySequence`。

### 7.2 桌面移动模式

`DesktopMoveMode` 新增：

- `BroomFlight`；
- `Apparate`；
- `EdgePolish`。

`MainWindow.ViewModel_DesktopMoveRequested()` 在普通随机步行和窗口上沿分支之前处理三种魔法：

- BroomFlight：固定当前显示器工作区，持续生成随机目标并高速平滑插值，直到 1 分钟或取消；
- Apparate：延迟后把窗口透明度设为 0，选择与原位置有最小距离的随机点，移动后恢复透明度；
- EdgePolish：优先刷新目标窗口几何并沿其上缘往返；没有目标窗口时沿当前显示器上缘运行。

所有分支在 `finally` 中恢复透明度、移动状态、显示器边界和完成信号。

### 7.3 石化生命周期

Petrificus Totalus 在石化转场完成后保持 `_busyAction=true`，保存 `_petrificationSession`，播放银币循环。`ReleasePetrificationAsync()` 完成互动生命周期并回到低趴；“停下”检测石化状态后复用同一解除入口。

## 8. 自发魔法

四种魔法以独立 `BehaviorDefinition` 注册到 `BehaviorCatalog.Autonomous`，统一经过 EligibilityFilter、UtilityScoring、SelectionPolicy、具体魔法 LearnedPreference、冷却与重复抑制：

1. 检查同日本地额度；
2. `daily_magic_available` 作为必需信号；无额度时四项在评分前全部硬过滤；
3. 勿扰、会议、压力、疲劳、安全感、好奇、活泼和淘气进入既有过滤与评分；
4. 选择策略从满足条件的魔法和其他自主行为中可复现地选择；
5. `RunScoredAutonomousMagicAsync()` 在动作开始前保存 `LastAutonomousMagicAt`；
6. 执行被评分管线选中的具体魔法。

开始前持久化保证中断或崩溃不会让同日额度重复。手动魔法不写这个字段。

## 9. 节日调度

`TryRunCalendarSpecialAsync()` 在启动、每分钟状态更新和档案保存后调用。生日优先于节日，防止同一天生日被节日占用。单次动画约 12 秒，完成后返回低趴。

Seasonal 运行序列没有普通右键命令。节日项使用独立的纯文字模型，不创建 `Thumbnail` 或 `PreviewCommand`；对应日期的播放只从日历调度入口发生。

## 10. 界面接线

`ControlWindow.xaml`：

- 标题绑定 `PetProfileTitle`；
- 新增“宠物档案”页；
- 动作卡抽成 `ActionGalleryCard` 模板；
- “动作素材库”内部增加“全部动作 / 魔法特辑”页签；
- 动作卡显示 `AvailabilityLabel`。

`MainWindow.xaml`：

- 宠物图片命名为 `PetImage`，用于全局坐标；
- 右键一级新增“宠物魔法”；
- 四个二级名称固定英文；
- 增加“解除石化”。

## 11. 素材构建

`scripts/rebuild-v10-specials.sh` 以色键源构建：

- `pupu-specials-youthful-v10.png`，8×5；
- `pupu-seasonal-youthful-v10.png`，8×4。

脚本逐行统一缩放与落点，清除绿色色键，保留 20px 透明安全区。视线源、魔法源和节日源保存在 `AssetSources/v10`，V9/V8 正式图集不被写入。

`pupu-assets.json` 是唯一运行清单。`AssetPackService` 把 `specials=8×5`、`seasonal=8×4` 加入必需图集，并把 1.3.0 自动导出的旧素材副本视为需要回退到新版内置包。

## 12. 测试

`Pupu.Tests` 共 34 项。1.4.0 新增：

- 同一个确定随机值在活跃和疲惫状态下得到不同的参与结论；
- 同一本地日期不能触发第二次自发魔法；
- 圣诞、万圣节、2026 春节日期及相邻非节日日期门槛；
- 主人生日年龄计算；
- 档案中文名与主人称呼进入本地宠物台词。

发布验收还包括：

- WPF Release 编译；
- XAML 绑定静态检查；
- 九图集 456 格素材检查；
- ZIP 解压；
- PE x64 GUI 类型；
- 发布包、源码包和工作区 V10 哈希一致性。
