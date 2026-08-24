using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FormsScreen = System.Windows.Forms.Screen;
using WpfImage = System.Windows.Controls.Image;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace GuaiMiao;

internal sealed class PetWindow : Window
{
    private readonly WpfImage _image;
    private readonly DispatcherTimer _singleClickTimer;
    private nint _handle;
    private uint _taskbarCreatedMessage;
    private byte[] _alpha = new byte[192 * 208];
    private int _alphaWidth = 192;
    private int _alphaHeight = 208;
    private bool _allowClose;
    private bool _mousePassThrough;
    private bool _dragging;
    private bool _doubleClickHandled;
    private WpfPoint _mouseDownClient;
    private WpfPoint _mouseDownScreen;
    private double _startLeft;
    private double _startTop;

    public PetWindow()
    {
        Title = AppInfo.ProductName;
        Width = 192;
        Height = 208;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        _image = new WpfImage
        {
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };
        Content = _image;

        _singleClickTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(200,
                System.Windows.Forms.SystemInformation.DoubleClickTime))
        };
        _singleClickTimer.Tick += (_, _) =>
        {
            _singleClickTimer.Stop();
            SingleClicked?.Invoke();
        };

        SourceInitialized += OnSourceInitialized;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseEnter += (_, _) =>
        {
            if (!_dragging && !IsMouseCaptured)
                PointerEntered?.Invoke();
        };
        MouseLeave += (_, _) =>
        {
            if (!_dragging && !IsMouseCaptured)
                PointerExited?.Invoke();
        };
        MouseRightButtonUp += (_, e) =>
        {
            RightClicked?.Invoke();
            e.Handled = true;
        };
    }

    public event Action? SingleClicked;
    public event Action? DoubleClicked;
    public event Action? RightClicked;
    public event Action? DragStarted;
    public event Action<double>? DragMoved;
    public event Action? DragFinished;
    public event Action? PointerEntered;
    public event Action? PointerExited;
    public event Action? TaskbarCreated;
    public event Action? SystemResumed;
    public event Action? DisplayConfigurationChanged;

    public string CurrentMonitorDeviceName
    {
        get
        {
            try
            {
                var center = PointToScreen(new WpfPoint(ActualWidth / 2, ActualHeight / 2));
                return FormsScreen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y)).DeviceName;
            }
            catch
            {
                return FormsScreen.PrimaryScreen?.DeviceName ?? string.Empty;
            }
        }
    }

    public void SetFrame(BitmapSource frame)
    {
        _image.Source = frame;
        var converted = frame.Format == PixelFormats.Bgra32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        _alphaWidth = converted.PixelWidth;
        _alphaHeight = converted.PixelHeight;
        var pixels = new byte[_alphaWidth * _alphaHeight * 4];
        converted.CopyPixels(pixels, _alphaWidth * 4, 0);
        _alpha = new byte[_alphaWidth * _alphaHeight];
        for (var index = 0; index < _alpha.Length; index++)
            _alpha[index] = pixels[index * 4 + 3];
    }

    public void SetScale(double scale)
    {
        Width = 192 * scale;
        Height = 208 * scale;
        EnsureVisible();
    }

    public void SetMousePassThrough(bool enabled)
    {
        _mousePassThrough = enabled;
        ApplyExtendedStyles();
    }

    public void RestorePosition(double? left, double? top, string? monitorDeviceName)
    {
        var screen = FormsScreen.AllScreens.FirstOrDefault(item =>
                         item.DeviceName.Equals(monitorDeviceName, StringComparison.OrdinalIgnoreCase))
                     ?? FormsScreen.PrimaryScreen;
        if (left is null || top is null || screen is null)
        {
            ResetPosition(screen);
            return;
        }
        Left = left.Value;
        Top = top.Value;
        EnsureVisible();
    }

    public void ResetPosition(FormsScreen? screen = null)
    {
        screen ??= FormsScreen.PrimaryScreen;
        if (screen is null)
            return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var working = screen.WorkingArea;
        Left = working.Right / dpi.DpiScaleX - Width - 20;
        Top = working.Bottom / dpi.DpiScaleY - Height - 20;
    }

    public void EnsureVisible()
    {
        if (!IsInitialized || ActualWidth <= 0 || ActualHeight <= 0)
            return;
        var center = PointToScreen(new WpfPoint(ActualWidth / 2, ActualHeight / 2));
        var screen = FormsScreen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y));
        var dpi = VisualTreeHelper.GetDpi(this);
        var working = screen.WorkingArea;
        var left = working.Left / dpi.DpiScaleX;
        var top = working.Top / dpi.DpiScaleY;
        var right = working.Right / dpi.DpiScaleX;
        var bottom = working.Bottom / dpi.DpiScaleY;
        Left = Math.Clamp(Left, left, Math.Max(left, right - Width));
        Top = Math.Clamp(Top, top, Math.Max(top, bottom - Height));
    }

    public double MoveHorizontally(double delta)
    {
        var before = Left;
        Left += delta;
        EnsureVisible();
        return Left - before;
    }

    public double AvailableHorizontalTravel(bool moveRight)
    {
        if (!IsInitialized || ActualWidth <= 0)
            return 0;
        var center = PointToScreen(new WpfPoint(ActualWidth / 2, ActualHeight / 2));
        var screen = FormsScreen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y));
        var dpi = VisualTreeHelper.GetDpi(this);
        var working = screen.WorkingArea;
        var left = working.Left / dpi.DpiScaleX;
        var right = working.Right / dpi.DpiScaleX;
        return moveRight
            ? Math.Max(0, right - Width - Left)
            : Math.Max(0, Left - left);
    }

    public bool IsPointerWithinWindowBounds(double margin = 0)
    {
        if (!IsInitialized || ActualWidth <= 0 || ActualHeight <= 0)
            return false;
        var cursor = System.Windows.Forms.Cursor.Position;
        var topLeft = PointToScreen(new WpfPoint(0, 0));
        var bottomRight = PointToScreen(new WpfPoint(ActualWidth, ActualHeight));
        var dpi = VisualTreeHelper.GetDpi(this);
        var marginX = Math.Max(0, margin) * dpi.DpiScaleX;
        var marginY = Math.Max(0, margin) * dpi.DpiScaleY;
        return cursor.X >= topLeft.X - marginX && cursor.X < bottomRight.X + marginX &&
               cursor.Y >= topLeft.Y - marginY && cursor.Y < bottomRight.Y + marginY;
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        HwndSource.FromHwnd(_handle)?.AddHook(WindowMessageHook);
        ApplyExtendedStyles();
    }

    private void ApplyExtendedStyles()
    {
        if (_handle == 0)
            return;
        var style = NativeMethods.GetExtendedStyle(_handle) |
                    NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        style = _mousePassThrough
            ? style | NativeMethods.WsExTransparent
            : style & ~NativeMethods.WsExTransparent;
        NativeMethods.SetExtendedStyle(_handle, style);
    }

    private nint WindowMessageHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg == _taskbarCreatedMessage)
        {
            TaskbarCreated?.Invoke();
            return 0;
        }
        if (msg == NativeMethods.WmDisplayChange)
        {
            DisplayConfigurationChanged?.Invoke();
            return 0;
        }
        if (msg == NativeMethods.WmPowerBroadcast &&
            (wParam.ToInt32() == NativeMethods.PbtApmResumeAutomatic ||
             wParam.ToInt32() == NativeMethods.PbtApmResumeSuspend))
        {
            SystemResumed?.Invoke();
            return 0;
        }
        if (msg != NativeMethods.WmNcHitTest || _mousePassThrough)
            return 0;
        if (!NativeMethods.GetCursorPos(out var point) || !NativeMethods.ScreenToClient(hwnd, ref point))
            return 0;

        var clientWidth = Math.Max(1, (int)Math.Round(ActualWidth * VisualTreeHelper.GetDpi(this).DpiScaleX));
        var clientHeight = Math.Max(1, (int)Math.Round(ActualHeight * VisualTreeHelper.GetDpi(this).DpiScaleY));
        var x = Math.Clamp(point.X * _alphaWidth / clientWidth, 0, _alphaWidth - 1);
        var y = Math.Clamp(point.Y * _alphaHeight / clientHeight, 0, _alphaHeight - 1);
        if (_alpha[y * _alphaWidth + x] < 20)
        {
            handled = true;
            return NativeMethods.HtTransparent;
        }
        handled = true;
        return NativeMethods.HtClient;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_mousePassThrough)
            return;
        if (e.ClickCount >= 2)
        {
            _singleClickTimer.Stop();
            _doubleClickHandled = true;
            DoubleClicked?.Invoke();
            e.Handled = true;
            return;
        }

        _doubleClickHandled = false;
        _dragging = false;
        _mouseDownClient = e.GetPosition(this);
        _mouseDownScreen = PointToScreen(_mouseDownClient);
        _startLeft = Left;
        _startTop = Top;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
            return;
        var current = PointToScreen(e.GetPosition(this));
        if (!_dragging && Math.Abs(current.X - _mouseDownScreen.X) + Math.Abs(current.Y - _mouseDownScreen.Y) < 5)
            return;
        if (!_dragging)
        {
            _dragging = true;
            DragStarted?.Invoke();
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        var previousLeft = Left;
        Left = _startLeft + (current.X - _mouseDownScreen.X) / dpi.DpiScaleX;
        Top = _startTop + (current.Y - _mouseDownScreen.Y) / dpi.DpiScaleY;
        DragMoved?.Invoke(Left - previousLeft);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        if (_dragging)
        {
            EnsureVisible();
            DragFinished?.Invoke();
        }
        else if (!_doubleClickHandled)
        {
            _singleClickTimer.Stop();
            _singleClickTimer.Start();
        }
        _dragging = false;
        if (!IsMouseOver)
            PointerExited?.Invoke();
        e.Handled = true;
    }
}
