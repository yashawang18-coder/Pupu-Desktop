# Pupu 1.11.0 V17 / Core-WPF 解耦构建报告

## 本轮结果摘要

| 项目 | 结果 | 证据或边界 |
| --- | --- | --- |
| 压缩包安全解压 | 通过 | 243 个条目；0 个路径穿越；0 个符号链接 |
| .NET 8 SDK 初始化 | SDK 文件已安装，运行时初始化受阻 | 官方 SDK 8.0.423、Host/Runtime 8.0.29、linux-x64 被 `dotnet --info` 识别；CoreCLR 返回 `0x8007000E`，进程退出码 137 |
| 依赖恢复 | 未执行到 NuGet | `dotnet restore Pupu.sln --runtime win-x64` 在 CoreCLR 初始化阶段同样退出 137 |
| C# 语法解析 | 通过 | tree-sitter C# 解析 53 个 `.cs` 文件，0 个语法错误 |
| 项目 XML | 通过 | 6 个 `.csproj` 均可解析 |
| 架构门禁 | 通过 | Core/Application 无 WPF、Win32 或 Windows TFM；测试不再链接 Desktop 实现文件；ViewModel 不直接使用 WPF图像/Dispatcher/Windows 凭据或环境实现 |
| WPF 绑定门禁 | 通过 | 显式 OneWay/TwoWay 与左键互动/右键菜单契约通过 |
| 素材门禁 | 通过 | 13 张图集、616 个图集格、78 个独立帧、V17 五态银币、20px 边距、清洁边缘与稳定锚点通过 |
| Release 编译 / 68 项测试 | 阻塞，未声称通过 | 当前容器无法启动 CoreCLR |
| win-x64 自包含发布 | 阻塞，未生成 | 未复用、复制或改名任何旧版 `Pupu.exe` |
| Windows 安装器 | 阻塞，未生成 | 未复用、复制或改名任何旧版安装器 |
| Windows 实机验证 | 待执行 | Linux 交叉编译即使成功也不能替代 Windows 10/11 x64 的启动、凭据、DPI、快捷方式和卸载验证 |

## 工程重构

- `Pupu.Behavior`：纯行为域与语义表现意图。
- `Pupu.Application`：纯 `net8.0` 应用层、持久化/协议、`PetBehaviorRuntime` 和平台/表现端口。
- `Pupu.Platform.Windows`：Windows 凭据管理器、Win32 环境检测、Clipboard/Explorer/Codex Shell 集成。
- `Pupu.Desktop`：WPF 组合根、窗口/input、`WpfPresentationHost`、素材解码与裁帧。
- `Pupu.Tests`：通过项目引用测试 Behavior/Application，不再把 Desktop 源文件链接进测试程序集。

`MainViewModel` 不再直接持有 `ImageSource`、`BitmapSource`、`CroppedBitmap`、`DispatcherTimer`、`Application.Current`、`EnvironmentContextService` 或具体的凭据/模型实现。行为仲裁器、Agent Kernel、提案队列和执行器由应用层 `PetBehaviorRuntime` 统一拥有。

## V17 银币

新正面按主人参考图重制为正视亮银边，不含旧版黑色斜侧缘。正式图集为 `Pupu.Desktop/Assets/pupu-gaze-coin-youthful-v17.png`，四个正面状态共享同一构图并确定性派生，猫爪背面与运行时状态键保持兼容。四种正面 bbox 均为约 `214×216px`，满足 256×256 单元格四边至少 20px 透明边距。

## 在正常构建主机继续

在安装了 .NET 8 SDK 的 Windows 10/11 x64 PowerShell 中，从源码根目录执行：

```powershell
.\scripts\build-windows.ps1 -Runtime win-x64
```

脚本会依次执行 `dotnet --info`、带 RID 的 solution restore、全 solution Release build、架构/绑定/素材门禁、全部测试、`win-x64` 自包含单文件发布、便携 ZIP、带 SHA-256 载荷清单的当前用户安装器。所有新产物只写入 `artifacts/`；脚本不会从旧 `dist/` 复制或改名 EXE。

交叉编译完成后仍需在 Windows 实机检查：

1. `Pupu.exe` 能启动透明 WPF 窗口，V17 银币正面无黑边且翻面正常；
2. Windows 凭据管理器写入/读取/删除模型密钥；
3. 混合 DPI、跨屏、前台全屏探测和窗口路径；
4. 安装、覆盖升级、快捷方式、已安装应用登记、卸载回滚与用户数据保留；
5. 对最终 EXE/MSI/安装器执行签名与 SmartScreen 策略检查。
