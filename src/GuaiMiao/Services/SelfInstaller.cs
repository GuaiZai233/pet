using System.Diagnostics;
using GuaiMiao.Infrastructure;

namespace GuaiMiao.Services;

internal sealed record BootstrapResult(bool ShouldExit, bool InstallationFeaturesAvailable, string? Warning);

internal static class SelfInstaller
{
    public static BootstrapResult Bootstrap(string[] args)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current))
            return new BootstrapResult(false, false, "无法确定当前程序路径，已进入便携模式。");

        if (args.Contains(AppInfo.PortableArgument, StringComparer.OrdinalIgnoreCase))
            return new BootstrapResult(false, false, null);

        if (Path.GetFullPath(current).Equals(Path.GetFullPath(AppPaths.InstalledExecutable),
                StringComparison.OrdinalIgnoreCase))
            return new BootstrapResult(false, true, null);

        try
        {
            Directory.CreateDirectory(AppPaths.RootDirectory);
            Directory.CreateDirectory(AppPaths.CacheDirectory);
            if (!File.Exists(AppPaths.InstalledExecutable))
                return InstallFirstRun(current);

            var incoming = ReadVersion(current);
            var installed = ReadVersion(AppPaths.InstalledExecutable);
            var sameBinary = incoming == installed && FilesHaveSameSha256(current, AppPaths.InstalledExecutable);
            if (!ShouldUpgrade(incoming, installed, sameBinary))
            {
                WakeOrStartInstalled();
                return new BootstrapResult(true, true, null);
            }
            return UpgradeInstalled(current);
        }
        catch (Exception ex)
        {
            LocalLog.Error("bootstrap-install-failed", ex);
            return new BootstrapResult(false, false, $"安装失败，当前会话以便携模式运行：{ex.Message}");
        }
    }

    public static bool IsInstalledProcess => Environment.ProcessPath is string current &&
        Path.GetFullPath(current).Equals(Path.GetFullPath(AppPaths.InstalledExecutable),
            StringComparison.OrdinalIgnoreCase);

    public static string CreateHealthToken()
    {
        Directory.CreateDirectory(AppPaths.CacheDirectory);
        return Path.Combine(AppPaths.CacheDirectory, $"health-{Guid.NewGuid():N}.ok");
    }

    public static void MarkHealthy(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;
        var full = Path.GetFullPath(token);
        var cache = Path.GetFullPath(AppPaths.CacheDirectory) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(cache, StringComparison.OrdinalIgnoreCase))
            return;
        File.WriteAllText(full, "ok");
    }

    public static void BeginUninstall(bool deleteSettings)
    {
        if (!IsInstalledProcess)
            throw new InvalidOperationException("便携模式无法执行自卸载。");

        new AutostartService().SetEnabled(false);
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。");
        var helper = Path.Combine(Path.GetTempPath(), $"GuaiMiao-uninstall-{Guid.NewGuid():N}.exe");
        File.Copy(current, helper, true);
        var start = new ProcessStartInfo(helper) { UseShellExecute = true };
        start.ArgumentList.Add(AppInfo.UninstallHelperArgument);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add(AppPaths.RootDirectory);
        start.ArgumentList.Add(deleteSettings ? "1" : "0");
        if (Process.Start(start) is null)
            throw new InvalidOperationException("无法启动卸载程序。");
    }

    public static int RunUninstallHelper(string[] args)
    {
        try
        {
            if (args.Length < 4 || !int.TryParse(args[1], out var processId))
                return 2;
            var root = Path.GetFullPath(args[2]);
            if (!root.Equals(Path.GetFullPath(AppPaths.RootDirectory), StringComparison.OrdinalIgnoreCase))
                return 3;
            var deleteSettings = args[3] == "1";

            try
            {
                using var process = Process.GetProcessById(processId);
                process.WaitForExit(15000);
            }
            catch
            {
                // The app has already exited.
            }

            new AutostartService().SetEnabled(false);
            DeleteFile(AppPaths.InstalledExecutable);
            DeleteFile(AppPaths.InstalledExecutable + ".bak");
            DeleteFile(AppPaths.InstalledExecutable + ".new");
            DeleteDirectory(AppPaths.CacheDirectory);
            DeleteDirectory(AppPaths.LogsDirectory);
            if (deleteSettings)
            {
                DeleteFile(AppPaths.SettingsFile);
                foreach (var backup in Directory.Exists(root)
                             ? Directory.EnumerateFiles(root, "settings.corrupt-*.json")
                             : [])
                    DeleteFile(backup);
            }
            try { Directory.Delete(root, false); } catch { }

            var helper = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(helper))
                NativeMethods.MoveFileEx(helper, null, NativeMethods.MoveFileDelayUntilReboot);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static BootstrapResult InstallFirstRun(string current)
    {
        var staged = AppPaths.InstalledExecutable + ".new";
        File.Copy(current, staged, true);
        File.Move(staged, AppPaths.InstalledExecutable, true);
        var token = CreateHealthToken();
        var process = StartInstalled(token);
        if (!WaitForHealth(token, process, TimeSpan.FromSeconds(12)))
        {
            TryStop(process);
            DeleteFile(AppPaths.InstalledExecutable);
            return new BootstrapResult(false, false, "安装副本未能启动，当前会话以便携模式运行。");
        }
        DeleteFile(token);
        return new BootstrapResult(true, true, null);
    }

    private static BootstrapResult UpgradeInstalled(string current)
    {
        SingleInstanceService.SendAsync("shutdown-update", 1500).GetAwaiter().GetResult();
        WaitForPrimaryToExit(TimeSpan.FromSeconds(10));

        var backup = AppPaths.InstalledExecutable + ".bak";
        var staged = AppPaths.InstalledExecutable + ".new";
        DeleteFile(backup);
        DeleteFile(staged);
        File.Move(AppPaths.InstalledExecutable, backup, true);
        try
        {
            File.Copy(current, staged, true);
            File.Move(staged, AppPaths.InstalledExecutable, true);
            var token = CreateHealthToken();
            var process = StartInstalled(token);
            if (!WaitForHealth(token, process, TimeSpan.FromSeconds(12)))
                throw new InvalidOperationException("新版未能通过启动健康检查。");
            DeleteFile(token);
            DeleteFile(backup);
            return new BootstrapResult(true, true, null);
        }
        catch (Exception upgradeError)
        {
            LocalLog.Error("upgrade-failed", upgradeError);
            DeleteFile(AppPaths.InstalledExecutable);
            if (File.Exists(backup))
                File.Move(backup, AppPaths.InstalledExecutable, true);
            try
            {
                StartInstalled(null);
                return new BootstrapResult(true, true, "升级失败，已回滚到上一版本。");
            }
            catch (Exception rollbackError)
            {
                LocalLog.Error("rollback-failed", rollbackError);
                return new BootstrapResult(false, false,
                    $"升级和回滚均失败，当前会话以便携模式运行：{rollbackError.Message}");
            }
        }
    }

    private static void WakeOrStartInstalled()
    {
        if (!SingleInstanceService.SendAsync("attention", 900).GetAwaiter().GetResult())
            StartInstalled(null);
    }

    private static Process StartInstalled(string? healthToken)
    {
        var start = new ProcessStartInfo(AppPaths.InstalledExecutable) { UseShellExecute = true };
        start.ArgumentList.Add(AppInfo.InstalledArgument);
        if (!string.IsNullOrWhiteSpace(healthToken))
        {
            start.ArgumentList.Add("--health-token");
            start.ArgumentList.Add(healthToken);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("无法启动已安装副本。");
    }

    private static bool WaitForHealth(string token, Process process, TimeSpan timeout)
    {
        var stopAt = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < stopAt)
        {
            if (File.Exists(token))
                return true;
            if (process.HasExited)
                return false;
            Thread.Sleep(100);
        }
        return false;
    }

    private static void WaitForPrimaryToExit(TimeSpan timeout)
    {
        var stopAt = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < stopAt)
        {
            try
            {
                using var mutex = Mutex.OpenExisting(AppInfo.MutexName);
                if (mutex.WaitOne(0))
                {
                    mutex.ReleaseMutex();
                    return;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return;
            }
            Thread.Sleep(100);
        }
    }

    private static Version ReadVersion(string path)
    {
        var raw = FileVersionInfo.GetVersionInfo(path).FileVersion;
        return Version.TryParse(raw, out var version) ? version : new Version(0, 0, 0, 0);
    }

    internal static bool ShouldUpgrade(Version incoming, Version installed, bool sameBinary) =>
        incoming > installed || incoming == installed && !sameBinary;

    private static bool FilesHaveSameSha256(string leftPath, string rightPath)
    {
        using var left = File.OpenRead(leftPath);
        using var right = File.OpenRead(rightPath);
        var leftHash = System.Security.Cryptography.SHA256.HashData(left);
        var rightHash = System.Security.Cryptography.SHA256.HashData(right);
        return leftHash.AsSpan().SequenceEqual(rightHash);
    }

    private static void TryStop(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch { }
    }

    private static void DeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void DeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}
