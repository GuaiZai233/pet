using System.Runtime.InteropServices;

namespace GuaiMiao;

internal static class NativeMethods
{
    public const int GwlExStyle = -20;
    public const long WsExTransparent = 0x00000020L;
    public const long WsExToolWindow = 0x00000080L;
    public const long WsExNoActivate = 0x08000000L;
    public const int WmNcHitTest = 0x0084;
    public const int WmDisplayChange = 0x007E;
    public const int WmPowerBroadcast = 0x0218;
    public const int PbtApmResumeAutomatic = 0x0012;
    public const int PbtApmResumeSuspend = 0x0007;
    public const int HtTransparent = -1;
    public const int HtClient = 1;
    public const int MoveFileDelayUntilReboot = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(nint hWnd, ref Point point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string message);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool MoveFileEx(string existingFile, string? newFile, int flags);

    public static long GetExtendedStyle(nint hWnd) => GetWindowLongPtr64(hWnd, GwlExStyle).ToInt64();

    public static void SetExtendedStyle(nint hWnd, long style) =>
        SetWindowLongPtr64(hWnd, GwlExStyle, new nint(style));
}
