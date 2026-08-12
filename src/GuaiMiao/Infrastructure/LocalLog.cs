using System.Text;

namespace GuaiMiao.Infrastructure;

internal static class LocalLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize()
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        _path = Path.Combine(AppPaths.LogsDirectory, "guai-miao.log");
        RotateIfNeeded();
        Info("start");
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        lock (Gate)
        {
            try
            {
                if (_path is null)
                    return;
                RotateIfNeeded();
                var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
                if (exception is not null)
                    line += $" | {exception.GetType().Name}: {exception.Message}";
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must never stop the pet.
            }
        }
    }

    private static void RotateIfNeeded()
    {
        if (_path is null || !File.Exists(_path) || new FileInfo(_path).Length < MaxBytes)
            return;

        var second = _path + ".2";
        var first = _path + ".1";
        if (File.Exists(second)) File.Delete(second);
        if (File.Exists(first)) File.Move(first, second);
        File.Move(_path, first);
    }
}
