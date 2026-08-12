namespace GuaiMiao.Infrastructure;

internal static class AppPaths
{
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.ProductName);

    public static string InstalledExecutable { get; } = Path.Combine(RootDirectory, $"{AppInfo.ProductName}.exe");
    public static string SettingsFile { get; } = Path.Combine(RootDirectory, "settings.json");
    public static string LogsDirectory { get; } = Path.Combine(RootDirectory, "logs");
    public static string CacheDirectory { get; } = Path.Combine(RootDirectory, "cache");
}
