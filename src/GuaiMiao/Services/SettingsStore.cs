using System.Text.Json;
using System.Text.Json.Serialization;
using GuaiMiao.Infrastructure;
using GuaiMiao.Models;

namespace GuaiMiao.Services;

internal sealed class SettingsStore
{
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
                return new AppSettings();
            var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), _options)
                ?? new AppSettings();
            var migratedAutoRun = value.SchemaVersion < 3;
            if (migratedAutoRun)
                value.AutoRunEnabled = true;
            value.Sanitize();
            if (migratedAutoRun)
            {
                LocalLog.Info("settings-migrated schema=3 autoRun=True");
                Save(value);
            }
            return value;
        }
        catch (Exception ex)
        {
            LocalLog.Warn("settings-corrupt", ex);
            try
            {
                var backup = Path.Combine(AppPaths.RootDirectory,
                    $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Move(AppPaths.SettingsFile, backup, true);
            }
            catch (Exception backupError)
            {
                LocalLog.Warn("settings-backup-failed", backupError);
            }
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Sanitize();
        Directory.CreateDirectory(AppPaths.RootDirectory);
        var temporary = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, _options));
        File.Move(temporary, AppPaths.SettingsFile, true);
    }
}
