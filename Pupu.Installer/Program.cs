using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

[assembly: SupportedOSPlatform("windows")]

namespace Pupu.Installer;

internal static class Program
{
    private const string AppName = "Pupu";
    private const string DisplayName = "朴朴桌面宠物";
    private const string Version = "1.11.1";
    private const string PayloadResource = "Pupu.Installer.Payload.zip";
    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Pupu";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Any(IsUninstallStage2))
                return RunUninstallStage2(args);
            if (args.Any(IsUninstall))
                return BeginUninstall(args.Any(IsQuiet));

            InstallAndLaunch();
            return 0;
        }
        catch (Exception exception)
        {
            ShowError($"安装未完成。\n\n{FriendlyMessage(exception)}");
            return 1;
        }
    }

    private static void InstallAndLaunch()
    {
        if (!Environment.Is64BitOperatingSystem)
            throw new PlatformNotSupportedException("朴朴 1.11.1 需要 64 位 Windows 10 或 Windows 11。");

        var installRoot = GetInstallRoot();
        var installParent = Directory.GetParent(installRoot)?.FullName
            ?? throw new InvalidOperationException("无法确定安装目录。");
        Directory.CreateDirectory(installParent);

        var token = Guid.NewGuid().ToString("N");
        var stagingRoot = Path.Combine(installParent, $"{AppName}.installing-{token}");
        var backupRoot = Path.Combine(installParent, $"{AppName}.backup-{token}");
        var movedExisting = false;
        var movedNewInstall = false;

        try
        {
            Directory.CreateDirectory(stagingRoot);
            ExtractPayload(stagingRoot);
            VerifyPayload(stagingRoot);
            StopRunningPupu();

            if (Directory.Exists(installRoot))
            {
                Directory.Move(installRoot, backupRoot);
                movedExisting = true;
            }

            Directory.Move(stagingRoot, installRoot);
            movedNewInstall = true;
            InstallUninstaller(installRoot);
            CreateShortcuts(installRoot);
            RegisterUninstaller(installRoot);
            TryDeleteDirectory(backupRoot);

            Process.Start(new ProcessStartInfo(Path.Combine(installRoot, "Pupu.exe"))
            {
                UseShellExecute = true,
                WorkingDirectory = installRoot
            });
        }
        catch
        {
            TryDeleteDirectory(stagingRoot);
            if (movedNewInstall)
                TryDeleteDirectory(installRoot);
            if (movedExisting && Directory.Exists(backupRoot))
            {
                if (Directory.Exists(installRoot))
                    throw new IOException("新版本安装失败，且旧版本暂时无法自动恢复。旧版本仍保存在备份目录。");
                Directory.Move(backupRoot, installRoot);
            }
            throw;
        }
    }

    private static void ExtractPayload(string destination)
    {
        using var payload = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(PayloadResource)
            ?? throw new InvalidDataException("安装程序不包含应用载荷，请重新下载安装程序。");
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false);
        var destinationPrefix = Path.GetFullPath(destination)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装载荷包含不安全路径。");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void VerifyPayload(string root)
    {
        var manifestPath = Path.Combine(root, "install-manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("安装载荷缺少完整性清单。");
        var manifest = JsonSerializer.Deserialize<InstallManifest>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("安装载荷完整性清单无效。");
        if (!string.Equals(manifest.Version, Version, StringComparison.Ordinal))
            throw new InvalidDataException("安装载荷版本与安装程序不一致。");

        var rootPrefix = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var item in manifest.Files)
        {
            var relative = item.Path.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(root, relative));
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullPath))
            {
                throw new InvalidDataException($"安装载荷缺少文件：{item.Path}");
            }

            using var stream = File.OpenRead(fullPath);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actual, item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"安装载荷校验失败：{item.Path}");
        }

        if (!File.Exists(Path.Combine(root, "Pupu.exe")) ||
            !File.Exists(Path.Combine(root, "Assets", "pupu-assets.json")))
        {
            throw new InvalidDataException("安装载荷缺少主程序或动作素材。");
        }
    }

    private static void InstallUninstaller(string installRoot)
    {
        var source = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法定位安装程序。");
        var target = Path.Combine(installRoot, "Pupu-Setup.exe");
        if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            File.Copy(source, target, overwrite: true);
    }

    private static void CreateShortcuts(string installRoot)
    {
        var target = Path.Combine(installRoot, "Pupu.exe");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var startMenuFolder = Path.Combine(programs, DisplayName);
        Directory.CreateDirectory(startMenuFolder);
        CreateShortcut(Path.Combine(desktop, $"{DisplayName}.lnk"), target, installRoot);
        CreateShortcut(Path.Combine(startMenuFolder, $"{DisplayName}.lnk"), target, installRoot);
        CreateShortcut(
            Path.Combine(startMenuFolder, "卸载朴朴.lnk"),
            Path.Combine(installRoot, "Pupu-Setup.exe"),
            installRoot,
            "/uninstall");
    }

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string arguments = "")
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(targetPath);
            link.SetWorkingDirectory(workingDirectory);
            link.SetDescription(DisplayName);
            link.SetIconLocation(Path.Combine(workingDirectory, "Pupu.exe"), 0);
            if (!string.IsNullOrWhiteSpace(arguments))
                link.SetArguments(arguments);
            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static void RegisterUninstaller(string installRoot)
    {
        var setup = Path.Combine(installRoot, "Pupu-Setup.exe");
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey)
            ?? throw new InvalidOperationException("无法登记卸载入口。");
        key.SetValue("DisplayName", DisplayName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", "Pupu & 主人");
        key.SetValue("DisplayIcon", $"{Path.Combine(installRoot, "Pupu.exe")},0");
        key.SetValue("InstallLocation", installRoot);
        key.SetValue("UninstallString", $"\"{setup}\" /uninstall");
        key.SetValue("QuietUninstallString", $"\"{setup}\" /uninstall /quiet");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", EstimateInstalledKilobytes(installRoot), RegistryValueKind.DWord);
    }

    private static int BeginUninstall(bool quiet)
    {
        if (!quiet)
        {
            var answer = MessageBox(
                IntPtr.Zero,
                "确定卸载朴朴吗？\n\n共同记忆和设置会继续保留。",
                DisplayName,
                0x00000004u | 0x00000020u | 0x00000100u);
            if (answer != 6)
                return 0;
        }

        var current = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法定位卸载程序。");
        var temporary = Path.Combine(
            Path.GetTempPath(),
            $"Pupu-Uninstaller-{Guid.NewGuid():N}.exe");
        File.Copy(current, temporary, overwrite: true);
        Process.Start(new ProcessStartInfo(temporary)
        {
            UseShellExecute = false,
            Arguments =
                $"/uninstall-stage2 \"{GetInstallRoot()}\" {Environment.ProcessId}" +
                (quiet ? " /quiet" : string.Empty)
        });
        return 0;
    }

    private static int RunUninstallStage2(string[] args)
    {
        var marker = Array.FindIndex(args, IsUninstallStage2);
        if (marker < 0 || marker + 2 >= args.Length ||
            !int.TryParse(args[marker + 2], out var parentProcessId))
        {
            throw new InvalidDataException("卸载参数无效。");
        }

        var requestedRoot = Path.GetFullPath(args[marker + 1]);
        var expectedRoot = Path.GetFullPath(GetInstallRoot());
        if (!string.Equals(requestedRoot, expectedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("拒绝删除非朴朴安装目录。");

        try
        {
            Process.GetProcessById(parentProcessId).WaitForExit(10_000);
        }
        catch
        {
            // Parent has already exited.
        }

        StopRunningPupu();
        DeleteShortcuts();
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        TryDeleteDirectory(expectedRoot);
        ScheduleSelfDelete();

        if (!args.Any(IsQuiet))
            MessageBox(IntPtr.Zero, "朴朴已卸载，共同记忆和设置仍然保留。", DisplayName, 0x40);
        return 0;
    }

    private static void DeleteShortcuts()
    {
        var desktopShortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"{DisplayName}.lnk");
        var startMenuFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            DisplayName);
        TryDeleteFile(desktopShortcut);
        TryDeleteDirectory(startMenuFolder);
    }

    private static void StopRunningPupu()
    {
        foreach (var process in Process.GetProcessesByName(AppName))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId)
                    continue;
                try
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(1_500))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(3_000);
                    }
                }
                catch
                {
                    // The process may have exited between discovery and shutdown.
                }
            }
        }
    }

    private static void ScheduleSelfDelete()
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments =
                    $"/D /Q /C ping 127.0.0.1 -n 3 > nul & del /F /Q \"{current}\""
            });
        }
        catch
        {
            MoveFileEx(current, null, 0x4);
        }
    }

    private static int EstimateInstalledKilobytes(string root)
    {
        var bytes = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Sum(path =>
            {
                try { return new FileInfo(path).Length; }
                catch { return 0L; }
            });
        return (int)Math.Clamp((bytes + 1023) / 1024, 0, int.MaxValue);
    }

    private static string GetInstallRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        AppName);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static string FriendlyMessage(Exception exception) =>
        exception is UnauthorizedAccessException
            ? "没有写入当前用户安装目录的权限，请关闭安全软件拦截后重试。"
            : exception.Message;

    private static bool IsUninstall(string arg) =>
        arg.Equals("/uninstall", StringComparison.OrdinalIgnoreCase);

    private static bool IsUninstallStage2(string arg) =>
        arg.Equals("/uninstall-stage2", StringComparison.OrdinalIgnoreCase);

    private static bool IsQuiet(string arg) =>
        arg.Equals("/quiet", StringComparison.OrdinalIgnoreCase);

    private static void ShowError(string message) =>
        MessageBox(IntPtr.Zero, message, $"{DisplayName}安装程序", 0x10);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(
        IntPtr window,
        string text,
        string caption,
        uint type);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(
        string existingFileName,
        string? newFileName,
        uint flags);
}

internal sealed class InstallManifest
{
    public string Version { get; set; } = "";
    public List<InstallManifestFile> Files { get; set; } = new();
}

internal sealed class InstallManifestFile
{
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLink
{
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
    void GetPath(
        [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file,
        int maximumPath,
        IntPtr findData,
        uint flags);
    void GetIDList(out IntPtr itemIdList);
    void SetIDList(IntPtr itemIdList);
    void GetDescription(
        [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name,
        int maximumName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
    void GetWorkingDirectory(
        [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder directory,
        int maximumPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
    void GetArguments(
        [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder arguments,
        int maximumPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
    void GetHotkey(out short hotkey);
    void SetHotkey(short hotkey);
    void GetShowCmd(out int showCommand);
    void SetShowCmd(int showCommand);
    void GetIconLocation(
        [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder iconPath,
        int iconPathLength,
        out int iconIndex);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
    void Resolve(IntPtr window, uint flags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
}
