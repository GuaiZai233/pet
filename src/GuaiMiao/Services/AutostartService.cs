using Microsoft.Win32;
using GuaiMiao.Infrastructure;

namespace GuaiMiao.Services;

internal sealed class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string ExpectedCommand => $"\"{AppPaths.InstalledExecutable}\" {AppInfo.InstalledArgument}";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(AppInfo.RunValueName) is string value &&
               value.Equals(ExpectedCommand, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey, true) ??
                         throw new InvalidOperationException("无法打开当前用户的启动项注册表。"))
        {
            if (enabled)
                key.SetValue(AppInfo.RunValueName, ExpectedCommand, RegistryValueKind.String);
            else
                key.DeleteValue(AppInfo.RunValueName, false);
            key.Flush();
        }
        if (IsEnabled() != enabled)
            throw new InvalidOperationException("开机启动注册表写入后校验失败。");
    }
}
