using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Pupu.Application;

namespace Pupu.Desktop.Services;

public sealed class CodexIterationService : ICodexIterationService
{
    public async Task<string> LoadProjectPathAsync()
    {
        if (!File.Exists(StoragePaths.CodexProjectPathFile)) return string.Empty;
        return (await File.ReadAllTextAsync(StoragePaths.CodexProjectPathFile, Encoding.UTF8)).Trim();
    }

    public async Task<string> CreateIterationRequestAsync(
        string ownerRequest,
        string localPetContext,
        string projectPath)
    {
        ownerRequest = ownerRequest.Trim();
        projectPath = projectPath.Trim().Trim('"');
        await File.WriteAllTextAsync(StoragePaths.CodexProjectPathFile, projectPath, new UTF8Encoding(false));

        var builder = new StringBuilder();
        builder.AppendLine("# pupu · Codex 迭代任务");
        builder.AppendLine();
        builder.AppendLine($"> 生成时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine();
        builder.AppendLine("## 主人的新需求");
        builder.AppendLine();
        builder.AppendLine(ownerRequest);
        builder.AppendLine();
        builder.AppendLine("## 当前 pupu 上下文");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(localPetContext.Trim());
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## 给 Codex 的执行约定");
        builder.AppendLine();
        builder.AppendLine("- 在 pupu 源码根目录工作，先读取 `AGENTS.md` 与 `PUPU-CODEX.md`。");
        builder.AppendLine("- 保留现有本地长期记忆、性格演进、主人纠正和 Markdown 兼容性。");
        builder.AppendLine("- 新增动作时同步更新动作素材库、帧率、触发规则、自然语言映射与文档。");
        builder.AppendLine("- 所有宠物帧必须是 256×256 高清安全格，四边至少 20 px 透明边距；不得串格、裁尾、出现黑色边线或改变 pupu 身份。");
        builder.AppendLine("- 触摸反应只能出现猫，不得出现手、手臂、手指或人体局部；整行动作共用缩放率与落地点。");
        builder.AppendLine("- 完成后运行 Release 编译、只读绑定检查、动作格边界检查并生成新的 Windows x64 包。");
        builder.AppendLine();
        builder.AppendLine("## 源码目录");
        builder.AppendLine();
        builder.AppendLine(string.IsNullOrWhiteSpace(projectPath) ? "（尚未设置；请在应用中填写解压后的源码目录）" : $"`{projectPath}`");

        var text = builder.ToString();
        await File.WriteAllTextAsync(StoragePaths.CodexRequestFile, text, new UTF8Encoding(false));
        Clipboard.SetText(text);

        if (Directory.Exists(projectPath))
            Process.Start(new ProcessStartInfo("explorer.exe", projectPath) { UseShellExecute = true });
        Process.Start(new ProcessStartInfo("https://chatgpt.com/codex") { UseShellExecute = true });

        return Directory.Exists(projectPath)
            ? $"迭代任务已写入 {StoragePaths.CodexRequestFile}，并复制到剪贴板；源码目录与 Codex 已打开。"
            : $"迭代任务已写入 {StoragePaths.CodexRequestFile} 并复制到剪贴板。请先填写源码目录，再让 Codex 在该目录执行。";
    }
}
