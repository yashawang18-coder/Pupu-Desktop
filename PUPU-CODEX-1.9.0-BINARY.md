# pupu 与 Codex 的迭代工作流

桌面宠物不会读取 ChatGPT Cookie、账号密码或私有会话。应用中的“Codex 迭代”页会把主人的新需求和当前本地上下文写到：

```text
%LOCALAPPDATA%\PupuDesktop\memory\codex-iteration-request.md
```

同时复制任务、打开源码目录和 Codex 页面。让 Codex 在本仓库根目录执行该任务；`AGENTS.md` 会自动提供长期工程约束。

## 架构入口

- `MainWindow.xaml(.cs)`：透明桌面窗口、左键互动/拖动区分、右键分级照顾菜单、实时随机桌面路径。
- `Pupu.Behavior/`：PersonalityBehaviorV2 核心；四类数据、统一行为评分、手势解释、跨天偏好、互动生命周期和幂等迁移。
- `MainViewModel.cs`：把核心评分结果映射到动画、窗口移动、长动作进度、素材库和 Codex/ChatGPT 桥。
- `AssetPackService.cs` / `Assets/pupu-assets.json`：外部素材包解析、尺寸校验、自定义包回退及可编辑素材导出。
- `MemoryEngine.cs`：V2 持久化、关系日变化上限、习惯巩固、主人纠正和 ChatGPT/Codex 上下文。
- `NaturalLanguageRuleService.cs`：普通中文到可执行角色参数的映射。
- `pupu-memory.md`：运行时由主人维护的 Markdown 主文件；不在仓库里，而在本地应用数据目录。

## 性格—状态—关系—偏好—动作关系

- `TemperamentBaseline` 是主人设定的天生底色，运行时不可自动修改。
- `RuntimeState` 决定当下是否愿意；`RelationshipState` 缓慢变化且有单日上限。
- `LearnedPreference` 精确关联 `behavior_id + interaction_type + context`；普通习惯至少跨 3 个日期。
- 所有自主候选统一使用 `BehaviorScorer`；调试日志必须包含每个评分分量和选择原因。
- 触摸由 `GestureInterpreter` 先解释、先改状态，再通过同一评分系统选择反应。
- 长动作必须记录 Started/Progressed/Completed/Interrupted/Failed，效果只在实际进度节点应用。

## 推荐的 Codex 请求

```text
新增“鼠标在附近晃动时追踪光标”的动作。高活泼时更容易触发，主人标记“不像朴朴”后降低权重；更新素材库、Markdown 匹配说明、自然语言设定、测试和 0.x 版本发布包。
```

Codex 完成后应给出修改摘要、验证结果、新动作素材路径和更新后的安装包；不应自动上传账号数据或绕过主人确认。
