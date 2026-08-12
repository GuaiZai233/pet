using System.Drawing;
using System.Reflection;
using GuaiMiao.Models;
using Forms = System.Windows.Forms;

namespace GuaiMiao.Services;

internal sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _shapeAuto;
    private readonly Forms.ToolStripMenuItem _shapeNormal;
    private readonly Forms.ToolStripMenuItem _shapePaw;
    private readonly Forms.ToolStripMenuItem _passThrough;
    private readonly Forms.ToolStripMenuItem _topmost;
    private readonly Forms.ToolStripMenuItem _autoRun;
    private readonly Dictionary<double, Forms.ToolStripMenuItem> _sizes = [];
    private readonly Forms.ToolStripMenuItem _autostart;
    private readonly Forms.ToolStripMenuItem _uninstall;
    private readonly Icon _icon;

    public TrayIconService()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("GuaiMiao.Assets.GuaiMiao.ico")
            ?? throw new InvalidOperationException("Missing tray icon.");
        using var loadedIcon = new Icon(stream);
        _icon = (Icon)loadedIcon.Clone();

        _menu = new Forms.ContextMenuStrip { ShowImageMargin = false };
        var shape = new Forms.ToolStripMenuItem("形态");
        _shapeAuto = NewItem("自动漫游", () => ShapeChanged?.Invoke(ShapeMode.Auto));
        _shapeNormal = NewItem("日常", () => ShapeChanged?.Invoke(ShapeMode.Normal));
        _shapePaw = NewItem("肉垫贴屏", () => ShapeChanged?.Invoke(ShapeMode.PawGlass));
        shape.DropDownItems.AddRange([_shapeAuto, _shapeNormal, _shapePaw]);

        _passThrough = NewItem("鼠标穿透", () => MousePassThroughChanged?.Invoke(!_passThrough!.Checked));
        _topmost = NewItem("始终置顶", () => AlwaysOnTopChanged?.Invoke(!_topmost!.Checked));
        _autoRun = NewItem("自动跑动（已开启）", () => AutoRunChanged?.Invoke(!_autoRun!.Checked));
        var runNow = NewItem("立即跑动（测试）", () => RunNowRequested?.Invoke());
        var think = NewItem("立即思考", () => ThinkRequested?.Invoke());

        var size = new Forms.ToolStripMenuItem("大小");
        foreach (var scale in new[] { 0.75, 1.0, 1.25, 1.50 })
        {
            var item = NewItem($"{scale:P0}", () => ScaleChanged?.Invoke(scale));
            _sizes[scale] = item;
            size.DropDownItems.Add(item);
        }

        var reset = NewItem("重置位置", () => ResetPositionRequested?.Invoke());
        _autostart = NewItem("开机启动", () => AutostartChanged?.Invoke(!_autostart!.Checked));
        var about = NewItem("关于", () => AboutRequested?.Invoke());
        _uninstall = NewItem("卸载", () => UninstallRequested?.Invoke());
        var exit = NewItem("退出", () => ExitRequested?.Invoke());

        _menu.Items.AddRange([
            shape,
            think,
            _autoRun,
            runNow,
            _passThrough,
            _topmost,
            size,
            reset,
            new Forms.ToolStripSeparator(),
            _autostart,
            about,
            _uninstall,
            new Forms.ToolStripSeparator(),
            exit
        ]);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = AppInfo.ProductName,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => AttentionRequested?.Invoke();
    }

    public event Action<ShapeMode>? ShapeChanged;
    public event Action<bool>? MousePassThroughChanged;
    public event Action<bool>? AlwaysOnTopChanged;
    public event Action<bool>? AutoRunChanged;
    public event Action? RunNowRequested;
    public event Action? ThinkRequested;
    public event Action<double>? ScaleChanged;
    public event Action? ResetPositionRequested;
    public event Action<bool>? AutostartChanged;
    public event Action? AboutRequested;
    public event Action? UninstallRequested;
    public event Action? ExitRequested;
    public event Action? AttentionRequested;

    public void Update(AppSettings settings, bool installationFeaturesAvailable)
    {
        _shapeAuto.Checked = settings.Shape == ShapeMode.Auto;
        _shapeNormal.Checked = settings.Shape == ShapeMode.Normal;
        _shapePaw.Checked = settings.Shape == ShapeMode.PawGlass;
        _passThrough.Checked = settings.MousePassThrough;
        _topmost.Checked = settings.AlwaysOnTop;
        _autoRun.Checked = settings.AutoRunEnabled;
        _autoRun.Text = settings.AutoRunEnabled ? "自动跑动（已开启）" : "自动跑动（已关闭）";
        foreach (var pair in _sizes)
            pair.Value.Checked = Math.Abs(pair.Key - settings.Scale) < 0.01;
        _autostart.Checked = settings.Autostart;
        _autostart.Enabled = installationFeaturesAvailable;
        _autostart.ToolTipText = installationFeaturesAvailable ? string.Empty : "便携模式不可设置开机启动";
        _uninstall.Enabled = installationFeaturesAvailable;
    }

    public void ShowAtCursor() => _menu.Show(Forms.Cursor.Position);

    public void ShowBalloon(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(3500);
    }

    public void Recreate()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Visible = true;
    }

    private static Forms.ToolStripMenuItem NewItem(string text, Action action)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
