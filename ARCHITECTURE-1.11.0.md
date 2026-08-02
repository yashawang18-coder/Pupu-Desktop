# Pupu 1.11.0 Agent 架构

## 目标

1. Agent 的行为决策、人格、关系和记忆不依赖 WPF、PNG 图集或建模技术。
2. 所有行为冲突只由一个仲裁器处理，避免资格过滤、忙碌、保护期、冷却和鼠标注意力分别作决定。
3. 保持 1.10.1 的记忆、素材清单和用户设置兼容。

## 运行数据流

```text
感知事件 / 主人口令 / 记忆召回
        ↓
PetAgentKernel
  - IAgentDecisionStatePort：隔离评分快照
  - IAgentMemoryPort：情节/关系事实/习惯摘要
        ↓
BehaviorArbitrator
  - 硬资格与强制状态
  - 性格、状态、关系、记忆和环境评分
  - 迟滞选择
  - 当前行为租约、优先级、保护期、可打断性和冷却
        ↓
BehaviorPresentationIntent
        ↓
IBehaviorPresentationResolver<T>
        ↓
当前：sprite-atlas-v17
未来：skeletal-2d / skeletal-3d / procedural
```

## 工程边界

| 项目 | TFM | 职责 | 禁止依赖 |
| --- | --- | --- | --- |
| `Pupu.Behavior` | `net8.0` | 行为域、人格、记忆端口、仲裁、提案与语义意图 | WPF、Win32、平台服务、具体素材 |
| `Pupu.Application` | `net8.0` | 应用模型、存储/协议、行为运行时组合、平台与表现端口 | WPF、Win32、Windows TFM |
| `Pupu.Platform.Windows` | `net8.0-windows` | Windows 凭据管理器、Win32 环境探测、Shell/剪贴板 | 行为选择与素材映射 |
| `Pupu.Desktop` | `net8.0-windows` | WPF 窗口、输入、图像裁帧、计时器、sprite 表现与组合根 | 第二套仲裁、直接修改行为域状态 |
| `Pupu.Tests` | `net8.0` | Core/Application 行为与架构回归 | 链接编译 Desktop 实现文件 |

`scripts/verify-architecture.ps1` / `.sh` 会检查依赖方向、TFM、Core/Application 的平台无关性，以及 `MainViewModel` 不得重新引入 WPF 图像、Dispatcher、Windows 凭据或环境探测实现。

## 代码边界

### Agent 内核

- `Pupu.Behavior/AgentKernel.cs`
- `Pupu.Behavior/BehaviorArbitration.cs`
- `Pupu.Behavior/BehaviorScoring.cs`
- `Pupu.Behavior/MemoryLayers.cs`
- `Pupu.Behavior/PetAgent.cs`

该层不引用 WPF、Windows API、Bitmap、图集行或骨骼节点。

### 记忆端口

Agent 不再通过一个端口同时获得可变人格和长期记忆：

- `IAgentDecisionStatePort.ReadDecisionState()` 返回本次决策专用副本，仅包含天生性格、运行状态、关系和已索引偏好；不携带原始事件或可编辑文件。
- `IAgentMemoryPort.ReadAgentMemory()` 只返回最近情节摘要、主人确认的关系事实和跨天习惯摘要。

`MemoryEngine` 继续负责 Markdown、JSON、JSONL、迁移、删除和持久化。Agent 不能直接改写长期记忆。

### 表现端口

Agent 输出 `BehaviorPresentationIntent`：

- `BehaviorId`
- `Enter / Loop / Exit / Settle`
- `Stationary / Locomotion / Teleport / Flight`
- 方向、归一化速度、循环和语义参数

当前 WPF 壳使用 `DictionaryBehaviorPresentationResolver` 把行为映射到 `AnimationSequence`。图像、裁帧、计时器、动作预览和应用生命周期都位于 `IDesktopPresentationHost` 后面；`MainViewModel` 只保存平台无关的 `object` 表现句柄。替换建模方案时实现新的 resolver/host，不修改人格、记忆、行为评分和仲裁代码。

Windows 凭据、模型网络服务、Codex Shell 集成与前台全屏检测分别通过 `IModelApiService`、`ICodexIterationService` 与 `IDesktopEnvironmentProbe` 注入。macOS 适配器可以实现相同端口，不需要引用 `Pupu.Platform.Windows`。

### 单一行为仲裁

`BehaviorArbitrator` 是唯一决策权威：

- `EvaluateEligibility`：移动能力、安静环境、安全阈值、必要信号。
- `BehaviorScorer`：天生性格、运行状态、关系、学习偏好、上下文、重复和迟滞。
- `SelectionPolicy`：从同一候选集合选择。
- `Evaluate`：来源优先级、当前行为租约、保护期、可打断性、强制状态和冷却。
- `BehaviorDecision.Admission`：在同一环境快照内完成选择和提交，执行层不能再用第二组规则重新判断。
- `RollbackAdmission`：表现适配器无法执行、抛错或取消时，仅回滚该次租约与冷却，不覆盖更新的决定。

`EligibilityFilter` 与 `BehaviorSelector` 只作为窄接口保留，构造时必须注入同一个 `BehaviorArbitrator`，不再允许内部创建另一份规则、冷却或历史。

`busy` 不再作为第二个决策系统：UI 只用它控制按钮是否可点；行为能否打断、保护期是否结束及状态是否禁用，都以仲裁器的租约和状态快照为准。鼠标注意力使用 observation-only 或最低优先级请求，不直接抢占长动作。

## 新表现方案接入

1. 保持现有 `BehaviorId` 和 `BehaviorPresentationIntent`。
2. 实现 `IBehaviorPresentationResolver<TPresentation>`。
3. 为必须支持的行为声明 Enter、Loop、Exit、方向和移动能力。
4. 在平台壳注入新 resolver。
5. 运行同一套 Agent 和仲裁测试，再增加该表现方案的脚掌、根运动或骨骼约束测试。

建模方案不得修改性格权重、记忆内容、行为优先级或冷却；如果表现能力缺失，应回退到安全待机，而不是让表现层重新选择行为。
