using System.Windows;
using System.Windows.Threading;
using GuaiMiao.Animation;
using GuaiMiao.Infrastructure;
using GuaiMiao.Models;
using GuaiMiao.Services;
using Forms = System.Windows.Forms;

namespace GuaiMiao;

internal sealed class AppController : IDisposable
{
    private readonly System.Windows.Application _application;
    private readonly BootstrapResult _bootstrap;
    private readonly SingleInstanceService _singleInstance;
    private readonly SettingsStore _settingsStore = new();
    private readonly AutostartService _autostart = new();
    private readonly Random _random = new();
    private readonly PetWindow _window;
    private readonly TrayIconService _tray;
    private readonly PetAnimator _animator;
    private readonly DispatcherTimer _behaviorTimer;
    private readonly DispatcherTimer _pawTimer;
    private readonly DispatcherTimer _autoRunTimer;
    private readonly DispatcherTimer _movementTimer;
    private readonly DispatcherTimer _hoverBoundsTimer;
    private readonly HoverInteractionGate _hoverGate = new();
    private readonly DragDirectionTracker _dragDirection = new();
    private AppSettings _settings;
    private bool _autoAction;
    private int _movementTicks;
    private double _movementStep;
    private bool _movementFromAutoRun;
    private bool _dragging;
    private bool _exiting;

    public AppController(System.Windows.Application application, BootstrapResult bootstrap,
        SingleInstanceService singleInstance)
    {
        _application = application;
        _bootstrap = bootstrap;
        _singleInstance = singleInstance;
        _settings = _settingsStore.Load();
        if (_bootstrap.InstallationFeaturesAvailable)
            _settings.Autostart = _autostart.IsEnabled();

        var catalog = AnimationCatalog.Load();
        var sprites = new SpriteLibrary(catalog);
        _window = new PetWindow();
        _animator = new PetAnimator(catalog, sprites, _window.SetFrame);
        _tray = new TrayIconService();

        _behaviorTimer = OneShotTimer(OnBehaviorTimer);
        _pawTimer = OneShotTimer(OnPawTimer);
        _autoRunTimer = OneShotTimer(OnAutoRunTimer);
        _movementTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(90)
        };
        _movementTimer.Tick += OnMovementTick;
        _hoverBoundsTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _hoverBoundsTimer.Tick += (_, _) => EndHoverIfPointerLeftWindow();
        ConnectEvents();
    }

    public void Start(string? healthToken)
    {
        LocalLog.Info($"runtime-settings autoRun={_settings.AutoRunEnabled} shape={_settings.Shape} scale={_settings.Scale:0.00}");
        _window.Topmost = _settings.AlwaysOnTop;
        _window.SetScale(_settings.Scale);
        _window.RestorePosition(_settings.Left, _settings.Top, _settings.MonitorDeviceName);
        _window.SetMousePassThrough(_settings.MousePassThrough);
        ApplyShape();
        _window.Show();
        _window.EnsureVisible();
        _autoRunTimer.Stop();
        ScheduleAutoRun(runSoon: true);
        _tray.Update(_settings, _bootstrap.InstallationFeaturesAvailable);
        _singleInstance.StartServer();

        try
        {
            CodexPetSyncService.Sync();
        }
        catch (Exception ex)
        {
            LocalLog.Warn("codex-pet-sync-failed", ex);
            _tray.ShowBalloon(AppInfo.ProductName,
                $"Codex 宠物同步失败：{ex.Message}", Forms.ToolTipIcon.Warning);
        }

        SelfInstaller.MarkHealthy(healthToken);
        if (!string.IsNullOrWhiteSpace(_bootstrap.Warning))
            _tray.ShowBalloon(AppInfo.ProductName, _bootstrap.Warning, Forms.ToolTipIcon.Warning);
        if (_settings.MousePassThrough)
            _tray.ShowBalloon(AppInfo.ProductName, "鼠标穿透已开启，可从托盘菜单关闭。", Forms.ToolTipIcon.Info);
    }

    private void ConnectEvents()
    {
        _window.SingleClicked += OnPetSingleClick;
        _window.DoubleClicked += TogglePetShape;
        _window.RightClicked += _tray.ShowAtCursor;
        _window.DragStarted += OnDragStarted;
        _window.DragMoved += OnDragMoved;
        _window.DragFinished += OnDragFinished;
        _window.PointerEntered += OnPointerEntered;
        _window.PointerExited += OnPointerExited;
        _window.TaskbarCreated += _tray.Recreate;
        _window.SystemResumed += RecoverAfterSystemChange;
        _window.DisplayConfigurationChanged += RecoverAfterSystemChange;

        _tray.ShapeChanged += SetShape;
        _tray.MousePassThroughChanged += SetMousePassThrough;
        _tray.AlwaysOnTopChanged += SetAlwaysOnTop;
        _tray.AutoRunChanged += SetAutoRun;
        _tray.RunNowRequested += RunNow;
        _tray.ThinkRequested += ThinkNow;
        _tray.ScaleChanged += SetScale;
        _tray.ResetPositionRequested += ResetPosition;
        _tray.AutostartChanged += SetAutostart;
        _tray.AboutRequested += ShowAbout;
        _tray.UninstallRequested += Uninstall;
        _tray.ExitRequested += Exit;
        _tray.AttentionRequested += Attention;
        _singleInstance.CommandReceived += OnInstanceCommand;
    }

    private static DispatcherTimer OneShotTimer(EventHandler handler)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background);
        timer.Tick += (sender, args) =>
        {
            timer.Stop();
            handler(sender, args);
        };
        return timer;
    }

    private void ApplyShape()
    {
        StopAutomaticActivity();
        switch (_settings.Shape)
        {
            case ShapeMode.PawGlass:
                _animator.Play("paw-glass");
                break;
            case ShapeMode.Normal:
                _animator.Play("idle");
                break;
            default:
                _animator.Play("idle");
                ScheduleBehavior();
                SchedulePawMoment();
                break;
        }
        ScheduleAutoRun();
    }

    private void SetShape(ShapeMode shape)
    {
        _settings.Shape = shape;
        ApplyShape();
        SaveSettings();
    }

    private void TogglePetShape() =>
        SetShape(_settings.Shape == ShapeMode.PawGlass ? ShapeMode.Normal : ShapeMode.PawGlass);

    private void OnPetSingleClick()
    {
        StopAutomaticActivity();
        _autoAction = true;
        var state = _settings.Shape == ShapeMode.PawGlass ||
                    _animator.CurrentState.Equals("paw-glass", StringComparison.OrdinalIgnoreCase)
            ? "paw-glass"
            : "waving";
        _animator.Play(state, 1, FinishAutoAction);
    }

    private void ThinkNow()
    {
        StopAutomaticActivity();
        _autoAction = true;
        _animator.Play("review", 1, FinishAutoAction);
    }

    private void OnPointerEntered()
    {
        if (_settings.MousePassThrough || _autoAction || _dragging || !_hoverGate.TryEnter())
            return;
        _behaviorTimer.Stop();
        _pawTimer.Stop();
        _autoRunTimer.Stop();
        _hoverBoundsTimer.Start();
        LocalLog.Info("hover-start pounces=3");
        _animator.Play("jumping", PetInteractionPolicy.HoverPounceLoops, FinishHoverPounce);
    }

    private void OnPointerExited()
    {
        EndHoverIfPointerLeftWindow();
    }

    private void FinishHoverPounce()
    {
        if (_hoverGate.Active && !_dragging)
            _animator.Play("idle");
    }

    private void EndHoverIfPointerLeftWindow()
    {
        if (!_hoverGate.Monitoring)
            return;
        var result = _hoverGate.ObservePointer(
            _window.IsPointerWithinWindowBounds(PetInteractionPolicy.HoverExitMargin));
        if (result == HoverExitResult.None)
            return;
        LocalLog.Info(result == HoverExitResult.ActiveHoverEnded
            ? "hover-ended"
            : "hover-suppression-cleared");
        ApplyShape();
    }

    private void ScheduleBehavior()
    {
        if (_settings.Shape != ShapeMode.Auto || _autoAction || _dragging || _hoverGate.Monitoring)
            return;
        _behaviorTimer.Interval = TimeSpan.FromSeconds(_random.Next(
            PetInteractionPolicy.AutomaticDelayMinSeconds,
            PetInteractionPolicy.AutomaticDelayMaxExclusiveSeconds));
        _behaviorTimer.Start();
    }

    private void SchedulePawMoment()
    {
        if (_settings.Shape != ShapeMode.Auto || _pawTimer.IsEnabled || _dragging || _hoverGate.Monitoring)
            return;
        _pawTimer.Interval = TimeSpan.FromMinutes(4 + _random.NextDouble() * 3);
        _pawTimer.Start();
    }

    private void OnBehaviorTimer(object? sender, EventArgs e)
    {
        if (_settings.Shape != ShapeMode.Auto || _autoAction || _dragging || _hoverGate.Monitoring)
            return;
        _autoAction = true;
        switch (_random.Next(5))
        {
            case 0:
                PlayAutoOnce("waving");
                break;
            case 1:
                PlayAutoOnce("jumping");
                break;
            case 2:
                PlayAutoOnce("review");
                break;
            case 3:
                PlayAutoOnce("waiting");
                break;
            default:
                if (_settings.AutoRunEnabled)
                    StartRun(false, true);
                else
                    PlayAutoOnce("running");
                break;
        }
    }

    private void OnPawTimer(object? sender, EventArgs e)
    {
        if (_settings.Shape != ShapeMode.Auto)
            return;
        if (_autoAction || _dragging || _hoverGate.Monitoring)
        {
            SchedulePawMoment();
            return;
        }
        _autoAction = true;
        _animator.Play("paw-glass", 2, FinishAutoAction);
    }

    private void PlayAutoOnce(string state) =>
        _animator.Play(state, PetInteractionPolicy.AutomaticActionLoops, FinishAutoAction);

    private void ScheduleAutoRun(bool runSoon = false)
    {
        if (!_settings.AutoRunEnabled || _autoRunTimer.IsEnabled || _autoAction || _dragging ||
            _hoverGate.Monitoring)
            return;
        _autoRunTimer.Interval = runSoon
            ? TimeSpan.FromSeconds(PetInteractionPolicy.InitialAutoRunDelaySeconds)
            : TimeSpan.FromSeconds(_random.Next(
                PetInteractionPolicy.AutomaticDelayMinSeconds,
                PetInteractionPolicy.AutomaticDelayMaxExclusiveSeconds));
        _autoRunTimer.Start();
        LocalLog.Info($"auto-run-scheduled delayMs={_autoRunTimer.Interval.TotalMilliseconds:0}");
    }

    private void OnAutoRunTimer(object? sender, EventArgs e)
    {
        if (!_settings.AutoRunEnabled)
            return;
        if (_autoAction || _dragging || _hoverGate.Monitoring)
        {
            _autoRunTimer.Interval = TimeSpan.FromSeconds(PetInteractionPolicy.AutomaticDelayMinSeconds);
            _autoRunTimer.Start();
            return;
        }
        _autoAction = true;
        StartRun(true, true);
    }

    private void RunNow()
    {
        StopAutomaticActivity();
        _autoAction = true;
        StartRun(true, false);
    }

    private void OnDragStarted()
    {
        StopAutomaticActivity();
        _dragging = true;
        _hoverGate.SuppressUntilExit();
        _hoverBoundsTimer.Start();
        _animator.Play("idle");
        LocalLog.Info("drag-start");
    }

    private void OnDragMoved(double deltaX)
    {
        if (!_dragging)
            return;
        var direction = _dragDirection.Observe(deltaX);
        if (direction == 0)
            return;
        var state = direction < 0 ? "running-left" : "running-right";
        if (!_animator.CurrentState.Equals(state, StringComparison.OrdinalIgnoreCase))
        {
            LocalLog.Info($"drag-direction={(direction < 0 ? "left" : "right")}");
            _animator.Play(state);
        }
    }

    private void OnDragFinished()
    {
        if (!_dragging)
            return;
        _dragging = false;
        SavePosition();
        LocalLog.Info("drag-finished");
        ApplyShape();
        _hoverGate.SuppressUntilExit();
        _behaviorTimer.Stop();
        _pawTimer.Stop();
        _autoRunTimer.Stop();
        _hoverBoundsTimer.Start();
    }

    private void StartRun(bool extended, bool fromAutoRun)
    {
        var moveRight = _random.Next(2) == 0;
        var available = _window.AvailableHorizontalTravel(moveRight);
        var opposite = _window.AvailableHorizontalTravel(!moveRight);
        if (available < 80 * _settings.Scale && opposite > available)
        {
            moveRight = !moveRight;
            available = opposite;
        }
        _movementFromAutoRun = fromAutoRun;
        var stepMagnitude = (extended ? 4.0 : 1.75) * _settings.Scale;
        var desiredDistance = extended
            ? Math.Min(available, _random.Next(260, 601) * _settings.Scale)
            : _random.Next(12, 21) * 3.5 * _settings.Scale;
        if (desiredDistance < stepMagnitude)
        {
            LocalLog.Info($"run-skipped source={(fromAutoRun ? "auto" : "manual")} available={available:0.0}");
            FinishAutoAction();
            return;
        }
        _movementTicks = Math.Max(1, (int)Math.Ceiling(desiredDistance / stepMagnitude));
        _movementStep = (moveRight ? 1 : -1) * stepMagnitude;
        LocalLog.Info($"run-start source={(fromAutoRun ? "auto" : "manual")} direction={(moveRight ? "right" : "left")} distance={desiredDistance:0.0} ticks={_movementTicks}");
        _animator.Play(_movementStep < 0 ? "running-left" : "running-right");
        _movementTimer.Start();
    }

    private void OnMovementTick(object? sender, EventArgs e)
    {
        var moved = _window.MoveHorizontally(_movementStep);
        _movementTicks--;
        if (_movementTicks > 0 && Math.Abs(moved - _movementStep) < 0.5)
            return;
        _movementTimer.Stop();
        SavePosition();
        LocalLog.Info("run-finished");
        FinishAutoAction();
    }

    private void FinishAutoAction()
    {
        _movementTimer.Stop();
        _hoverBoundsTimer.Stop();
        _movementFromAutoRun = false;
        _autoAction = false;
        ApplyShape();
    }

    private void StopAutomaticActivity()
    {
        _behaviorTimer.Stop();
        _pawTimer.Stop();
        _autoRunTimer.Stop();
        _movementTimer.Stop();
        _hoverBoundsTimer.Stop();
        _movementFromAutoRun = false;
        _dragDirection.Reset();
        _dragging = false;
        _hoverGate.Reset();
        _autoAction = false;
    }

    private void SetAutoRun(bool enabled)
    {
        _settings.AutoRunEnabled = enabled;
        LocalLog.Info($"auto-run-enabled={enabled}");
        if (!enabled)
        {
            _autoRunTimer.Stop();
            if (_movementFromAutoRun)
                ApplyShape();
        }
        else
        {
            ScheduleAutoRun(runSoon: true);
        }
        SaveSettings();
        _tray.ShowBalloon(AppInfo.ProductName,
            enabled ? "自动跑动已开启，乖喵会在 6 秒内先跑一次。" : "自动跑动已关闭。",
            Forms.ToolTipIcon.Info);
    }

    private void SetMousePassThrough(bool enabled)
    {
        _settings.MousePassThrough = enabled;
        _window.SetMousePassThrough(enabled);
        _tray.Update(_settings, _bootstrap.InstallationFeaturesAvailable);
        SaveSettings();
        if (enabled)
            _tray.ShowBalloon(AppInfo.ProductName, "已开启鼠标穿透；可从托盘菜单关闭。", Forms.ToolTipIcon.Info);
    }

    private void SetAlwaysOnTop(bool enabled)
    {
        _settings.AlwaysOnTop = enabled;
        _window.Topmost = enabled;
        _tray.Update(_settings, _bootstrap.InstallationFeaturesAvailable);
        SaveSettings();
    }

    private void SetScale(double scale)
    {
        _settings.Scale = scale;
        _window.SetScale(scale);
        _tray.Update(_settings, _bootstrap.InstallationFeaturesAvailable);
        SavePosition();
    }

    private void ResetPosition()
    {
        _window.ResetPosition();
        SavePosition();
    }

    private void SavePosition()
    {
        _settings.Left = _window.Left;
        _settings.Top = _window.Top;
        _settings.MonitorDeviceName = _window.CurrentMonitorDeviceName;
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
            _tray.Update(_settings, _bootstrap.InstallationFeaturesAvailable);
        }
        catch (Exception ex)
        {
            LocalLog.Warn("settings-save-failed", ex);
            _tray.ShowBalloon(AppInfo.ProductName, $"保存设置失败：{ex.Message}", Forms.ToolTipIcon.Warning);
        }
    }

    private void SetAutostart(bool enabled)
    {
        if (!_bootstrap.InstallationFeaturesAvailable)
            return;
        try
        {
            _autostart.SetEnabled(enabled);
            _settings.Autostart = _autostart.IsEnabled();
            if (_settings.Autostart != enabled)
                throw new InvalidOperationException("系统未保存开机启动设置。");
            SaveSettings();
            LocalLog.Info($"autostart-enabled={enabled} command={AutostartService.ExpectedCommand}");
            _tray.ShowBalloon(AppInfo.ProductName,
                enabled ? "开机启动已开启。" : "开机启动已关闭。",
                Forms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            LocalLog.Warn("autostart-failed", ex);
            _settings.Autostart = _autostart.IsEnabled();
            _tray.Update(_settings, _bootstrap.InstallationFeaturesAvailable);
            _tray.ShowBalloon(AppInfo.ProductName, $"修改开机启动失败：{ex.Message}", Forms.ToolTipIcon.Warning);
        }
    }

    private static void ShowAbout() => new AboutWindow().ShowDialog();

    private void Uninstall()
    {
        if (!_bootstrap.InstallationFeaturesAvailable)
            return;
        var dialog = new UninstallWindow();
        if (dialog.ShowDialog() != true)
            return;
        if (System.Windows.MessageBox.Show("确定要卸载乖喵吗？此操作会关闭当前宠物。", $"卸载 {AppInfo.ProductName}",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        try
        {
            SelfInstaller.BeginUninstall(dialog.DeleteSettings);
            Exit();
        }
        catch (Exception ex)
        {
            LocalLog.Error("uninstall-launch-failed", ex);
            System.Windows.MessageBox.Show($"无法启动卸载程序：{ex.Message}", AppInfo.ProductName,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RecoverAfterSystemChange()
    {
        _window.Dispatcher.BeginInvoke(() =>
        {
            _window.EnsureVisible();
            ApplyShape();
        });
    }

    private void OnInstanceCommand(string command)
    {
        _window.Dispatcher.BeginInvoke(() =>
        {
            if (command.Equals("shutdown-update", StringComparison.OrdinalIgnoreCase))
                Exit();
            else
                Attention();
        });
    }

    private void Attention()
    {
        if (!_window.IsVisible)
            _window.Show();
        _window.EnsureVisible();
        OnPetSingleClick();
    }

    public void Exit()
    {
        if (_exiting)
            return;
        _exiting = true;
        SavePosition();
        StopAutomaticActivity();
        _window.CloseForExit();
        _application.Shutdown();
    }

    public void Dispose()
    {
        StopAutomaticActivity();
        _animator.Dispose();
        _tray.Dispose();
    }
}
