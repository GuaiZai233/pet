using Microsoft.Win32;
using GuaiMiao.Infrastructure;

namespace GuaiMiao.Services;

internal sealed class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(AppInfo.RunValueName) is string value &&
               value.Contains(AppPaths.InstalledExecutable, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
            key.SetValue(AppInfo.RunValueName, $"\"{AppPaths.InstalledExecutable}\" {AppInfo.InstalledArgument}");
        else
            key.DeleteValue(AppInfo.RunValueName, false);
    }
}
