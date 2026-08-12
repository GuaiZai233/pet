namespace GuaiMiao.Models;

internal enum ShapeMode
{
    Auto,
    Normal,
    PawGlass
}

internal sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 3;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public string? MonitorDeviceName { get; set; }
    public double Scale { get; set; } = 1.0;
    public ShapeMode Shape { get; set; } = ShapeMode.Auto;
    public bool AlwaysOnTop { get; set; } = true;
    public bool MousePassThrough { get; set; }
    public bool AutoRunEnabled { get; set; } = true;
    public bool Autostart { get; set; }

    public void Sanitize()
    {
        SchemaVersion = 3;
        if (!double.IsFinite(Scale) || Scale is < 0.75 or > 1.50)
            Scale = 1.0;
        if (Left is not null && !double.IsFinite(Left.Value))
            Left = null;
        if (Top is not null && !double.IsFinite(Top.Value))
            Top = null;
        if (!Enum.IsDefined(Shape))
            Shape = ShapeMode.Auto;
        MonitorDeviceName = string.IsNullOrWhiteSpace(MonitorDeviceName) ? null : MonitorDeviceName;
    }
}
