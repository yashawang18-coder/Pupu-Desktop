# Pupu Desktop 1.6.0 代码实现设计说明

## 1. 版本范围

1.6.0 在 WPF 透明桌面窗口、PersonalityBehaviorV2、外部素材清单、本地 Markdown 记忆、模型协议和相册索引上增量实现：

- V12 十张正式图集与旧素材归档；
- 重制动作的序列、行为、素材库和记忆映射；
- 无原地踏步的连续随机路线和八方向高速扫帚；
- 贴地两相摆尾视线、高信任走近缩放、双面猫爪银币；
- 主人魔法必定执行与普通互动概率参与的边界；
- 桌面／面板共用的双入口对话；
- DeepSeek 预设与独立连接测试修复；
- Markdown 宠物系统提示词；
- 自动发现真实子目录、逐图描述和描述召回；
- 产品设计与代码实现卡片页。

## 2. 素材清单与运行时加载

### 2.1 正式清单

`Pupu.Desktop/Assets/pupu-assets.json` 的版本为：

```text
1.6.0-v12-continuous-motion-album
```

正式图集为：

```text
core          -> pupu-core-youthful-v12.png           -> 6 × 8
life          -> pupu-life-youthful-v12.png           -> 8 × 8
directions    -> pupu-directions-youthful-v12.png     -> 4 × 8
touch         -> pupu-touch-youthful-v12.png          -> 6 × 8
routines      -> pupu-routines-youthful-v12.png       -> 8 × 8
walkModes     -> pupu-walk-modes-youthful-v12.png     -> 8 × 8
activity      -> pupu-activity-youthful-v8.png         -> 8 × 8
lifeEquipment -> pupu-life-equipment-youthful-v12.png -> 3 × 8
motion        -> pupu-motion-youthful-v12.png          -> 11 × 8
gazeCoin      -> pupu-gaze-coin-youthful-v12.png      -> 3 × 8
litter        -> pupu-litter-youthful-v12.png          -> 4 × 8
specials      -> pupu-specials-youthful-v11.png        -> 5 × 8
seasonal      -> pupu-seasonal-youthful-v10.png        -> 4 × 8
```

`Pupu.Desktop.csproj` 以外部 `Content` 发布这 13 张图集。`AssetPackService.ValidateAndLoad()` 要求全部 ID、8 列和最低行数均存在；`SupersededBundledVersions` 增加 `1.5.0-short-leg-motion-memory`，避免旧导出包覆盖 V12。

### 2.2 `SpriteAtlas`

`MainViewModel.SpriteAtlas` 增加：

```text
GazeCoin
Litter
```

`SheetFor()` 继续把枚举映射到清单 ID。动作代码只引用图集、行、帧和时长，不重新硬编码 PNG 文件名。

### 2.3 旧素材归档

被替换的旧正式文件从 `Pupu.Desktop/Assets` 移到：

```text
AssetSources/legacy-formal-v12/
```

归档包括 V9 Core/Directions/Touch/WalkModes、V6 Life/Routines、V11 LifeEquipment/Motion 和未使用的 V10 Specials。V12 重建脚本从归档读取仍需保留的基线行，运行包不发布归档目录。

## 3. V12 素材重建

`scripts/rebuild-v12-assets.sh` 读取 `AssetSources/v12` 的六张生成源：

```text
pupu-daily-actions-v12-chroma.png
pupu-care-actions-v12-chroma.png
pupu-social-actions-v12-chroma.png
pupu-free-motion-v12-chroma.png
pupu-gaze-coin-v12-chroma.png
pupu-litter-v12-chroma.png
```

重建流程：

1. 移除纯绿色色键并清理绿边；
2. 按源图真实行列提取主体；
3. 同一动作行共享缩放率、底部落点和透明安全区；
4. 对自由移动相关行联合归一化，降低跨方向体型漂移；
5. 把重制行替换进归档基线，其余已验收行保持不变；
6. 生成独立 `gazeCoin` 与 `litter` 图集；
7. 完整解码并验证目标尺寸后写入正式目录。

## 4. 动画序列映射

### 4.1 日常与触摸

| 行为 | 图集行 |
| --- | --- |
| `idle.prone_observe` | `routines:1` |
| `self.paw_nibble` | `routines:2` |
| `self.groom` | `lifeEquipment:0` |
| `play.roll` | `core:1` |
| `play.tail_chase` | `core:2` |
| `care.feed_freeze_dried` | `routines:4` |
| `care.feed_canned` | `routines:5` |
| `care.clean_litter` | `life:1` |
| `care.groom` | `life:3` |
| `touch.warning` / 过度 rua | `touch:3` |
| 高信任 `touch.enjoy` | `touch:5` |
| `mischief.bat_object` | `life:5` |

冻干和罐头各自拆为：

```text
一次性 intro/pounce：0 → 1 → 2 → 3 → 4 → 5 → 6 → 7
持续 eating loop：4 → 5 → 6 → 7 → 6 → 5
```

`FeedAsync()` 只播放一次起步，长进食阶段使用后半段循环，避免每个进度周期重新扑食。

### 4.2 独立如厕

```text
ToiletEnterSequence   -> litter:0 frames 0..3
ToiletRelieveSequence -> litter:1 frames 2,3,4,5,4,3
ToiletLookUpSequence  -> litter:2 frames 0..7
ToiletBurySequence    -> litter:3 frames 0..6
ToiletExitSequence    -> litter:3 frames 5,6,7
```

`RunAutonomousToiletAsync()` 保留 `enter -> relieve -> optional look-up -> mandatory bury -> exit` 顺序；`finally` 继续负责排泄已提交后的短版埋砂收尾。

### 4.3 走近缩放

高信任 `touch.enjoy` 选择 `TrustTouchSequence` 后启动 `AnimateTrustApproachScaleAsync()`：

- 18 步、每步约 85ms；
- `InteractionScale` 从 `1.0` 增长到 `1.06`；
- 停留约 1.5 秒；
- 取消、结束或异常时在 `finally` 恢复 `1.0`。

`MainWindow.xaml` 在 `PetImage.RenderTransform` 使用独立缩放变换，因此该效果不修改保存的 `PetScale`，也不移动窗口。

## 5. 贴地视线状态机

`MainWindow.RefreshCursorGaze()` 每次采样全局鼠标坐标，把相对位置映射为中立和七个方向帧。`MainViewModel.UpdateCursorGaze()` 增加：

```text
_cursorGazeTailPhase
_nextCursorGazeTailPhaseAt
```

首次进入时保存原序列、行为、标签、上下文和动画来源。鼠标方向变化时立即切帧；方向不变但达到节拍时在 `gazeCoin:0` 与 `gazeCoin:1` 之间切换，默认间隔约 420ms。退出后恢复进入前序列，而不是强制切到统一待机。

`CanUseCursorGaze()` 会阻止视线覆盖忙碌动作、触摸逃离、石化、已调度行为和桌面移动。

## 6. 连续路线规划

### 6.1 `DesktopRoutePlanner`

新增纯逻辑服务：

```text
Pupu.Desktop/Services/DesktopRoutePlanner.cs
```

核心类型：

```text
DesktopRouteProfile = FullWalk | AutonomousRoam | BroomFlight
RouteDirection = Left | Right | Up | Down |
                 UpLeft | UpRight | DownLeft | DownRight
RouteBounds
RoutePoint
DesktopRouteSegment
```

`DesktopRouteSegment.Sample()` 使用 smoothstep、法线弧线、lift 和 flutter 计算坐标，并在每个采样点约束到 `RouteBounds`。

### 6.2 普通行走

`TryCreateWalkSegment()`：

- 正式遛猫优先更大范围目标，自主溜达混合附近和中等目标；
- 对不同 profile 设置最小／最大路段距离；
- 最多尝试 36 次，失败时使用远端角落或边缘回退点；
- 距离小于 1 DIP 时拒绝创建，禁止原地段；
- 根据位移向量选择八方向素材；
- FullWalk 速度约 285–520 DIP/s，AutonomousRoam 约 210–400 DIP/s；
- 弧线和小跳会经过边界探测，若越界则递减装饰幅度。

`MainWindow.ViewModel_DesktopMoveRequested()` 为每次动作创建新随机 planner，并在动作时限内连续生成路段。`AnimateRouteSegmentAsync()` 每约 16ms 更新 `Left` 和 `Top`；一段完成后立即创建下一段。

### 6.3 扫帚飞行

`TryCreateBroomSegment()` 使用八方向桶：

1. 每轮 Fisher-Yates 打乱八个方向；
2. 回溯排序避免相邻方向造成无法延续的边缘死路；
3. 每个方向尝试生成足够远的目标；
4. 边缘无法满足方向时保留未消费桶，并先执行远距离 reposition；
5. 一轮完成后重新打乱。

飞行速度约 1450–2300 DIP/s，路段时长限制为 16–760ms，弧线可达 250 DIP。`MoveBroomFlightAsync()` 不插入普通步行停顿，直到一分钟结束或收到取消。

## 7. 魔法参与和双面银币

### 7.1 主人魔法必定执行

`AccioBroomCommand`、`ApparateCommand`、`PetrificusTotalusCommand`、`ScourgifyCommand` 直接调用各自动作方法，不再调用 `TryParticipateAsync(OwnerInteractionKind.Magic, ...)`。命令的忙碌／石化可执行条件仍作为技术互斥。

`RunAutonomousMagicAsync()` 保持不变：四种魔法仍是统一自主候选，选择前要求 `daily_magic_available`，动作开始前消费当日额度。

### 7.2 银币正反面

`gazeCoin:2` 定义：

```text
SilverCoinSequence            -> 正面 frames 0,1
SilverCoinBackSequence        -> 背面 frames 4,5
SilverCoinFlipToBackSequence  -> frames 0,1,2,3,4
SilverCoinFlipToFrontSequence -> frames 4,5,6,7,0
```

`FlipPetrifiedCoinAsync()`：

- 只在 `_isPetrified` 且没有正在翻转时执行；
- 播放专用过渡序列；
- 将 `CoinFlipScaleX` 从 1 压缩至 0.16，再恢复至 1；
- 在结束点切换 `_isCoinBackVisible` 和循环序列；
- 取消或异常时把横向缩放恢复为 1。

`PetImage_MouseLeftButtonUp()` 在石化状态优先调用翻转并返回，不向 `GestureInterpreter` 发送 PointerUp，因此硬币点击不累计触摸压力。

## 8. 双入口对话

`MainWindow.xaml` 在 `PetImage` 下方增加：

```text
TextBox.Text -> ChatInput      Mode=TwoWay
Enter        -> SendChatCommand
发送 Button  -> SendChatCommand
```

`ControlWindow` 保留原聊天页，两者共享同一个 `MainViewModel`，因此不建立第二套会话、状态或网络服务。`BubbleText` 继续绑定在宠物上方，模型错误只写 `ModelApiStatus`。

## 9. DeepSeek 连接测试

### 9.1 预设与地址规范化

`ModelProtocolAdapter.GetPreset(ModelProvider.DeepSeek)` 返回：

```text
DefaultEndpoint = https://api.deepseek.com/chat/completions
DefaultModel = deepseek-v4-flash
DefaultApiFormat = OpenAiChat
SupportsResponses = false
SupportsVision = false
```

`NormalizeEndpoint()` 会为 DeepSeek 根地址补 `/chat/completions`，也为以 `/v1` 结尾的地址补同一路径；已经以 `/chat/completions` 结尾的地址保持不变。

### 9.2 独立测试路径

`ModelApiService.TestAsync()` 调用 `SendCoreAsync()` 时使用：

```text
requireEnabled = false
enforcePetBoundary = false
```

因此测试连接不依赖 `Enabled`，也不会因为测试回复不是宠物口吻而误报失败。设置、HTTPS／localhost、模型名和凭据仍会验证。服务端非成功响应由 `ExtractSafeServerMessage()` 提取短摘要，并清理疑似密钥文本。

## 10. Markdown 系统提示词

`PetProfile` 增加 `SystemPrompt`，在 `Normalize()` 中规范化为最多 6000 字符。纯逻辑编解码器 `PetSystemPromptMarkdown`：

- 从 `## 宠物系统提示词` 段提取项目；
- 导出时写回同名段；
- 忽略引用说明和空行；
- 支持 Markdown 保存—重载—导出往返。

`MemoryEngine.ApplyEditableNotebook()` 读取该段；`BuildEditableNotebook()` 写回；`BuildChatContextAsync()` 在档案、性格、状态和关系之后加入主人保存的提示词。最终请求仍由 `PetSpeechComposer.BuildSystemPrompt()` 包住固定角色与安全边界。

## 11. 相册自动发现与逐图描述

### 11.1 数据模型

`PhotoAlbumCatalog.SchemaVersion` 升为 2，并增加：

```text
PhotoDescriptions[]
  RelativePath
  Description
  UpdatedAt
```

描述使用相对根目录路径，不保存重复的绝对路径。

### 11.2 自动发现

`PhotoAlbumService.GetEffectiveAlbums()` 合并：

- `albums.json` 中主人手动建立的子相册；
- 根目录下自动发现的一级真实子文件夹。

自动发现目录：

- 跳过 reparse point、系统目录和无权限目录；
- 用规范化相对路径生成稳定 SHA-256 派生 ID；
- 使用 `ParseDirectoryMetadata()` 从中文或分隔符日期格式解析年月日；
- 移除日期后把剩余名称作为主题；
- 不把自动发现结果强制写回本地文件夹。

### 11.3 逐图描述

`SavePhotoDescriptionAsync()`：

- 验证照片仍位于已链接根目录；
- 只接受支持的图片扩展名；
- 规范化并限制描述长度；
- 描述为空时删除索引，非空时新增或更新；
- 通过临时文件原子保存 `albums.json`；
- 不修改原图片。

`Search()` 同时匹配描述、文件名、相对路径、相册名、主题和成长阶段。`BuildAlbumConversationMemoryAsync()` 把相关描述作为“主人描述”加入有限文字上下文，并参与 `AlbumRelevanceScore()` 排序。

## 12. 面板卡片和页签

`ControlWindow.xaml` 的前部产品页包含概览、档案、相册和动作素材库。动作库以 `RegularActionGalleryGroups` 为普通类别页签，`MagicActionGallery` 和 `SeasonalActionGallery` 单独展示。

`BuildInformationCards()` 生成：

```text
ProductDesignCards
CodeImplementationCards
```

两组都使用圆角卡片和可换行矩阵布局；信息来自运行代码中的静态说明，不执行文件或脚本。

## 13. 测试与验证

`Pupu.Tests` 增加纯逻辑覆盖：

- 普通路线持续产生非零距离、大小混合路段并保持在边界内；
- 扫帚方向桶覆盖八个方向、高速、大幅且不原地停顿；
- DeepSeek 根地址／`/v1` 地址规范化和默认模型；
- Markdown 宠物系统提示词往返；
- 自动发现带日期／主题的真实子目录并保存可检索描述。

本轮结果：

- Pupu.Tests Release `43/43` 通过；
- WPF Desktop Release 编译 `0 warning / 0 error`；
- C# 语法解析通过；
- XAML/XML 解析通过；
- 显式 OneWay/TwoWay 绑定检查通过；
- 13 张图集、624 格素材审计通过；
- 最低清晰度 `111.3`；
- 最大移动体型漂移 `1.238×`；
- 最大相邻中心位移 `8.54px`。

发布阶段还需校验自包含 Windows x64 ZIP、PE GUI 架构、13 图集白名单、V12 哈希一致性，并在 Windows 10/11 实机检查混合 DPI、跨屏、凭据管理器、文件夹选择器和一分钟扫帚飞行。
