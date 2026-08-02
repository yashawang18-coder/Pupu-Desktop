# Pupu 1.11.0 内核解耦与统一仲裁验收

## 本轮范围

- 拆分 Agent 的决策状态端口与记忆端口。
- 让行为评分只读取隔离快照，避免修改持久化人格。
- 让自主行为与触摸行为在单一 `BehaviorArbitrator` 中一次完成资格、评分、选择和提交。
- 为行为提案增加失败、取消和无平台映射时的租约/冷却回滚。
- 保留 `BehaviorPresentationIntent` 作为 2D 切帧、二维骨骼、三维骨骼或程序化表现的共同语义边界。

## 验收结果

- `Pupu.Behavior` Release 编译：通过。
- `Pupu.Tests` Release 编译：通过。
- Windows WPF Desktop Release 编译：通过。
- `Pupu.Installer` Release 编译：通过。
- 自动化测试：68/68 通过。
- WPF 显式绑定与桌面输入契约：通过。
- 素材验收：13 张图集、616 个图集格、78 个独立帧、V16 五态银币均通过。
- 应用与安装器均为 Windows x64 GUI、自包含 Bundle，不依赖系统安装的 .NET Desktop Runtime。
- 安装载荷包含 26 个正式文件及逐文件 SHA-256 清单；压缩包可完整解压，主程序和素材清单齐全。
- 安装器支持当前用户安装、桌面与开始菜单快捷方式、系统卸载入口、覆盖升级、失败回滚和安装后自动启动。
- 普通卸载保留 `%LOCALAPPDATA%\PupuDesktop` 中的主人设置、共同记忆和相册索引。

## 兼容性

- 不修改现有 Markdown、JSON、JSONL 记忆格式。
- 不修改 `pupu-assets.json` 行为/素材清单格式。
- 不修改现有 V15/V16 PNG 素材。
- 当前 2D 图集继续由 `sprite-atlas-v16` 适配器解析。

## Windows 安装交付

- `Pupu-Setup-x64-1.11.0.exe`：双击后安装到
  `%LOCALAPPDATA%\Programs\Pupu` 并自动启动，无需管理员权限。
- `Pupu-Windows-x64-1.11.0-portable.zip`：完整解压后直接运行
  `Pupu.exe`，`Assets` 文件夹必须与程序保持在一起。
- 当前构建环境不能显示 Windows WPF 窗口，已完成编译、测试、PE
  架构、GUI 子系统、官方 Bundle 识别、内嵌入口、载荷哈希和素材一致性
  验证；首次发布仍需在真实 Windows 10/11 x64 设备上完成一次安装与启动验收。

## 后续建模接入

新增二维或三维骨骼方案时，实现新的
`IBehaviorPresentationResolver<TPresentation>`，继续消费相同
`BehaviorPresentationIntent`。建模适配器不得修改行为权重、记忆、优先级或冷却；能力缺失时应返回安全待机表现。
