# Pupu 1.11.0 V17 / Core-WPF 解耦构建报告

## 本轮结果摘要

| 项目 | 结果 | 证据或边界 |
| --- | --- | --- |
| 压缩包安全解压 | 通过 | 243 个条目；0 个路径穿越；0 个符号链接 |
| .NET 8 SDK 初始化 | 通过 | GitHub `windows-latest` x64 主机成功执行 `dotnet --info` |
| 依赖恢复 | 通过 | `dotnet restore Pupu.sln --runtime win-x64` 成功 |
| C# 语法解析 | 通过 | tree-sitter C# 解析 53 个 `.cs` 文件，0 个语法错误 |
| 项目 XML | 通过 | 6 个 `.csproj` 均可解析 |
| 架构门禁 | 通过 | Core/Application 无 WPF、Win32 或 Windows TFM；测试不再链接 Desktop 实现文件；整个 ViewModels 目录禁止直接使用 WPF 图像、计时器、对话框、`App`、Windows 凭据或环境实现 |
| WPF 绑定门禁 | 通过 | 显式 OneWay/TwoWay 与左键互动/右键菜单契约通过 |
| 素材门禁 | 通过 | 13 张图集、616 个图集格、78 个独立帧、V17 五态银币、20px 边距、清洁边缘与稳定锚点通过 |
| Release 编译 / 68 项测试 | 通过 | Windows CI 全 solution Release build 成功，`68/68 tests passed` |
| win-x64 自包含发布 | 通过 | 生成全新的 `Pupu.exe` 与便携 ZIP；PE32+ GUI x86-64；未复用或改名旧版 EXE |
| Windows 安装器 | 通过 | 生成全新的 `Pupu-Setup-x64-1.11.0.exe`；PE32+ GUI x86-64；载荷 ZIP 与 28 项安装清单哈希全部通过 |
| Windows 交互式实机验证 | 待执行 | GitHub Windows 主机完成编译、测试和打包，但未进行透明窗口、DPI、凭据、快捷方式、覆盖安装和卸载的人工交互验证 |

首个全流程通过的 Windows CI：`Windows x64 build` run `30727534475`，源码提交 `9c1e89cee7e34af2d851ad2a2895c5005e4494ab`。CI Artifact SHA-256 为 `d9293980c349c3beb262d9250ed6d54a10a063cbdd2aac55e223f8fe72fb4941`；安装器 SHA-256 为 `ea364226aee2df573517f0d4ce7af8cc4adbda0bea0d31b7afd6139a6e9a0c9a`；便携包 SHA-256 为 `7f9add6268fbef2bda4788c2842c03330babcfff0180df675fc2cb242f82911a`。

## 工程重构

- `Pupu.Behavior`：纯行为域与语义表现意图。
- `Pupu.Application`：纯 `net8.0` 应用层、持久化/协议、`PetBehaviorRuntime` 和平台/表现端口。
- `Pupu.Platform.Windows`：Windows 凭据管理器、Win32 环境检测、Clipboard/Explorer/Codex Shell 集成。
- `Pupu.Desktop`：WPF 组合根、窗口/input、`WpfPresentationHost`、素材解码与裁帧。
- `Pupu.Tests`：通过项目引用测试 Behavior/Application，不再把 Desktop 源文件链接进测试程序集。

`MainViewModel` 不再直接持有 `ImageSource`、`BitmapSource`、`CroppedBitmap`、`DispatcherTimer`、`Application.Current`、`EnvironmentContextService` 或具体的凭据/模型实现。行为仲裁器、Agent Kernel、提案队列和执行器由应用层 `PetBehaviorRuntime` 统一拥有。

## V17 银币

新正面按主人参考图重制为正视亮银边，不含旧版黑色斜侧缘。正式图集为 `Pupu.Desktop/Assets/pupu-gaze-coin-youthful-v17.png`，四个正面状态共享同一构图并确定性派生，猫爪背面与运行时状态键保持兼容。四种正面 bbox 均为约 `214×216px`，满足 256×256 单元格四边至少 20px 透明边距。

## 复现 Windows 构建

在安装了 .NET 8 SDK 的 Windows 10/11 x64 PowerShell 中，从源码根目录执行：

```powershell
.\scripts\build-windows.ps1 -Runtime win-x64
```

脚本会依次执行 `dotnet --info`、带 RID 的 solution restore、全 solution Release build、架构/绑定/素材门禁、全部测试、`win-x64` 自包含单文件发布、便携 ZIP、带 SHA-256 载荷清单的当前用户安装器。所有新产物只写入 `artifacts/`；脚本不会从旧 `dist/` 复制或改名 EXE。

CI 构建完成后仍需在 Windows 实机检查：

1. `Pupu.exe` 能启动透明 WPF 窗口，V17 银币正面无黑边且翻面正常；
2. Windows 凭据管理器写入/读取/删除模型密钥；
3. 混合 DPI、跨屏、前台全屏探测和窗口路径；
4. 安装、覆盖升级、快捷方式、已安装应用登记、卸载回滚与用户数据保留；
5. 对最终 EXE/MSI/安装器执行签名与 SmartScreen 策略检查。
