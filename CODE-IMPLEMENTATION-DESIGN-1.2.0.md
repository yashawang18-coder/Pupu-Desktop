# Pupu Desktop 1.2.0 代码实现设计说明

## 1. 架构范围

解决方案分为三个项目：

| 项目 | 职责 |
|---|---|
| `Pupu.Behavior` | 平台无关的性格、状态、行为选择、互动会话、感知、记忆和宠物台词约束 |
| `Pupu.Desktop` | WPF 界面、动画、Win32 窗口/显示器感知、模型 API、凭据存储和持久化接线 |
| `Pupu.Tests` | 无第三方测试框架的确定性验收与轨迹回放 |

行为层不依赖 WPF，桌面项目把操作系统信号转换为行为上下文。UI 不直接决定自主行为。

## 2. 宠物发言边界

### 2.1 `PetSpeechComposer`

文件：`Pupu.Behavior/PetSpeech.cs`

`PetSpeechIntent` 描述发言意图，如启动、轻触享受、好奇、边界、回避、进食、玩耍、休息、记住、主动关注、对话和可恢复问题。

`Compose` 根据：

- `TemperamentBaseline`；
- `RuntimeState.Stress`；
- 发言意图；
- 可选外部草稿；

选择角色台词。外部草稿只有通过 `TryNormalizePetReply` 才能进入气泡，否则使用本地台词。

`TryNormalizePetReply` 完成：

- 空白归一；
- 技术词正则拦截；
- “作为 AI”等身份泄漏清除；
- 120 字符上限；
- 空结果拒绝。

`BuildSystemPrompt` 把身份、五维性格、当前状态、关系、自主性、关心边界和技术禁区组合为模型系统提示。该方法只用于模型请求，不向宠物气泡显示。

### 2.2 UI 分层

`MainViewModel.ShowBubbleAsync` 是宠物气泡的单一入口，再次调用 `PetSpeechComposer`。启动失败、模型失败、素材失败和规则保存失败把详细文本写入 `MemoryStatus`、`ModelApiStatus` 或 `AssetPackStatus`。

`App.xaml.cs` 的不可恢复异常对话框标题和正文明确标记为“系统诊断（不是宠物发言）”。

## 3. 模型 API

### 3.1 数据模型

`ModelApiSettings` 保存：

- `Enabled`；
- `Endpoint`；
- `Model`；
- `Temperature`；
- `MaximumReplyTokens`。

`StoragePaths.ModelApiSettingsFile` 指向 `%LOCALAPPDATA%\PupuDesktop\model-api.json`。该文件不包含 API 密钥。

### 3.2 `ModelApiService`

文件：`Pupu.Desktop/Services/ModelApiService.cs`

主要流程：

```mermaid
flowchart TD
    A["主人输入"] --> B["构建角色系统提示"]
    B --> C["读取 Windows 凭据"]
    C --> D["发送 HTTPS 请求"]
    D --> E["解析 Chat/Responses 文本"]
    E --> F["角色边界校验"]
    F -->|通过| G["宠物气泡"]
    F -->|拒绝| H["系统状态 + 本地台词回退"]
```

Endpoint 包含 `/responses` 时发送 `instructions` 与 `input`；否则发送 Chat Completions 的 `messages`。响应可解析：

- `choices[0].message.content`；
- `output_text`；
- `output[].content[].text`。

远程 endpoint 必须为 HTTPS；环回地址允许 HTTP。HttpClient 超时为 45 秒。非成功状态码、无文字、越界回复都抛出只供系统状态区使用的异常。

### 3.3 凭据

`WindowsCredentialVault` 使用 `CredWrite`、`CredRead`、`CredDelete`：

- 类型：Generic Credential；
- target：`PupuDesktop/ModelApi`；
- 持久化：当前 Windows 设备；
- 读取后的原生指针由 `CredFree` 释放；
- 写入临时字节与非托管内存在结束时覆盖并释放。

旧 `ChatGptWebBridgeService` 已删除，菜单、命令和 XAML 不再提供打开 ChatGPT 或复制粘贴网页回复。

## 4. 双击与互动会话

`MainWindow.xaml.cs` 不再根据 `ClickCount >= 2` 打开控制面板。每次鼠标释放都进入同一触摸解释路径。

`GestureInterpreter` 用时间、位移、持续时间和区域解释：

- touch；
- stroke；
- hold；
- drag；
- rapid_tap；
- release。

`InteractionSessionManager` 在会话窗口内合并连续触摸。双击生成两次触摸事件，但只形成一个连续会话与一个学习机会。达到频率阈值后才是 `rapid_tap`；是否享受、警告或回避仍由性格、关系和状态决定。

控制面板只由右键命令触发，消除“连续互动还是打开面板”的输入竞争。

## 5. 窗口和显示器感知

### 5.1 `EnvironmentContextService`

文件：`Pupu.Desktop/Services/EnvironmentContextService.cs`

Win32 输入：

- `EnumWindows`；
- `IsWindowVisible`；
- `IsIconic`；
- `GetWindowRect`；
- `GetForegroundWindow`；
- `GetWindowThreadProcessId`；
- `MonitorFromWindow`；
- `GetMonitorInfo`；
- `GetDpiForWindow` / `GetDpiForMonitor`；
- `EnumDisplayMonitors`。

过滤规则：

- 排除宠物自身窗口；
- 排除不可见、最小化、工具窗口；
- 排除没有普通标题或尺寸过小的窗口；
- 排除过度贴近显示器顶边、无法安全站立的表面；
- 不读取窗口内容。

输出类型：

- `DesktopRect`；
- `WindowSurfaceSnapshot`；
- `DesktopEnvironmentSnapshot`。

候选评分优先：

1. 与宠物同一显示器；
2. 常见浏览器进程；
3. 当前前台窗口；
4. 足够宽的安全上沿；
5. 合理的垂直位置。

这只是感知候选优先级。行为能否执行仍由 EligibilityFilter 的 `window_edge_available` 硬条件决定。

### 5.2 坐标与 DPI

Win32 返回物理像素，WPF 使用 DIP。服务按窗口或监视器 DPI 把边界转换为 DIP。所有自动移动通过 `CurrentWorkArea()` 获取宠物当前监视器工作区，而不是全局 `SystemParameters.WorkArea` 主屏值；后者只保留为窗口句柄尚未创建时的启动回退。

### 5.3 窗口跟随

`MainWindow` 每两秒捕获环境：

- 把快照传给 `MainViewModel`；
- 若正在栖息，按窗口句柄刷新表面；
- 窗口移动或缩放后更新宠物 X/Y；
- 目标消失、最小化或变得不安全时解除跟踪；
- 主人开始拖动宠物时立即解除窗口吸附。

`WindowPerch` 使用平滑三次曲线移动到窗口上沿；`WindowEdgeWalk` 在有效左右范围内选择目标并循环移动。每段移动约 60 FPS 插值，不直接跳坐标。

显示器、系统时间、休眠和唤醒事件会刷新上下文与状态恢复。

## 6. 行为管线接线

`BehaviorCatalog` 新增：

- `idle.sploot`；
- `environment.window_edge_rest`；
- `environment.window_edge_walk`。

窗口行为要求 `window_edge_available >= 0.2`。浏览器信号只提高效用，不替代硬前置。

运行流程：

```mermaid
flowchart LR
    A["环境/状态/偏好"] --> B["EligibilityFilter"]
    B --> C["UtilityScoring"]
    C --> D["SelectionPolicy"]
    D --> E["ActionScheduler"]
    E --> F["WPF 动画与移动"]
```

`MainViewModel.UpdateDesktopEnvironment` 把快照转换为有 TTL 的 `PerceptionEvent`，设置 `window_edge_available` 和 `browser_edge` 信号。`RunWindowSurfaceBehaviorAsync` 发起桌面移动请求；成功后记录动作生命周期和状态效果。

板鸭趴是被动低干扰自主候选，受疲劳、压力、独立度、活泼度、冷却和重复抑制影响。

## 7. 动画与素材

### 7.1 V8 图集

清单：`Pupu.Desktop/Assets/pupu-assets.json`

Activity 正式文件：

```text
pupu-activity-youthful-v8.png
2048 × 2048
8 列 × 8 行
256 × 256 RGBA/格
```

`AssetPackService` 要求 Activity 至少 8 行。项目文件只发布 V8，不发布旧 V7。

### 7.2 生成流程

`scripts/rebuild-v8-activity.sh`：

1. 对 V8 色键源图去背景；
2. 按 7 个生成关键帧归组每一行；
3. 使用行级固定比例与底部锚点归一化；
4. 复制保持帧补齐第 8 帧；
5. 将独立生成的明确板鸭趴覆盖到第 6 行；
6. 生成轻微呼吸差异；
7. 输出 8×8 透明图集；
8. 原子替换正式文件。

身份参考位于 `AssetSources/reference/pupu-face-2026-07-23/`。正脸吐舌与轻侧脸高清图为主要身份来源；仰视张嘴低清图只提供动态姿态。

### 7.3 质量审计

`scripts/verify-assets.sh` 检查：

- 清单 JSON；
- 七张图集尺寸；
- 384 格非空；
- alpha 主体最大 216px；
- 四边最少 20px；
- PNG 重复完整解码。

`scripts/audit-asset-quality.py` 额外使用 Pillow/Numpy 检查：

- 有效主体短边 ≥64px；
- 有效主体长边 ≥96px；
- 主体区域 Laplacian 方差 ≥70；
- 所有清单图集逐格覆盖。

Windows 构建同时运行 PowerShell 版尺寸/边距验证。

## 8. 面板

`ControlWindow.xaml` 把对话页改为“和朴朴聊天”：

- 启用复选框；
- endpoint；
- 模型名；
- PasswordBox；
- 温度；
- 保存、测试、删除密钥；
- 独立系统状态；
- 对话列表和输入框。

技术信息使用小号系统说明样式；宠物对话内容与系统状态不共用绑定。面板保持低饱和青灰与暖橙卡片体系。

## 9. 持久化

| 数据 | 位置 | 敏感性 |
|---|---|---|
| 模型非敏感设置 | `%LOCALAPPDATA%\PupuDesktop\model-api.json` | 不含密钥 |
| 模型 API 密钥 | Windows 凭据管理器 | 敏感 |
| 行为/关系/状态 | `personality-behavior-v2.json` | 本地 |
| 可编辑记忆 | `pupu-memory.md` | 本地 |
| 事件时间线 | `events.md` | 本地 |
| 错误日志 | `pupu-error.log` | 不写 API 密钥 |

模型 API 失败不会修改记忆或天生性格。

## 10. 测试

`Pupu.Tests/Program.cs` 共 28 项。1.2.0 新增：

- 双击仍是连续触摸且不映射面板；
- 宠物台词屏蔽技术语言，并随性格变化；
- 窗口上沿行为在缺少操作系统表面信号时被 EligibilityFilter 拒绝。

原 25 项继续覆盖：

- 五维性格分布；
- 状态压制；
- 固定种子选择；
- 24 小时切换稳定性；
- 感知习惯化；
- 休眠/唤醒/系统时间/显示器轨迹；
- 同一连续触摸会话；
- 无回应不降信任；
- 记忆删除级联；
- 确认事实优先；
- 动态互动区域；
- 长动作中断；
- 迁移幂等；
- 离线无欠账。

发布验证还包含 WPF 编译、XAML 绑定静态检查、384 格素材审计、ZIP 完整解压、PE 类型和发布/源码素材哈希一致性。

## 11. 运行风险与后续建议

- Win32 窗口枚举、混合 DPI 与虚拟桌面坐标必须在 Windows 10/11 多显示器设备实测；
- 可增加按窗口类别学习“喜欢趴在哪里”，但不得读取内容；
- 模型 API 可后续增加更多兼容响应适配器，仍需经过同一角色边界；
- 未来重绘旧 V6 图集时应继续使用 V8 的圆脸身份参考和清晰度审计，不允许为了统一而放大低清旧素材。
