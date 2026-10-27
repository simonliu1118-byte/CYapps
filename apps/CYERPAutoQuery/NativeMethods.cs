using System.Runtime.InteropServices;
using System.Text;

namespace CYERPAutoQuery;

// Win32 declarations needed to drive SMART ERP. Copied from CYERPAutoInput (only the parts
// this tool uses) by user decision 2026-10-16; to be merged into a shared library later.
internal static class NativeMethods
{
    internal const int SW_MAXIMIZE = 3;
    internal const int SW_RESTORE = 9;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal static readonly nint HWND_TOP = 0;

    internal const int GWL_STYLE = -16;
    internal const long ES_READONLY = 0x0800;
    internal const uint GA_PARENT = 1;
    internal const uint GA_ROOT = 2;

    internal const uint CB_GETDROPPEDSTATE = 0x0157;
    internal const uint EM_SETSEL = 0x00B1;
    internal const uint EM_GETLIMITTEXT = 0x00D5;
    internal const uint WM_PASTE = 0x0302;
    internal const uint WM_CLOSE = 0x0010;

    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_UNICODE = 0x0004;

    internal const ushort VK_BACK = 0x08;
    internal const ushort VK_RETURN = 0x0D;
    internal const ushort VK_ESCAPE = 0x1B;
    internal const ushort VK_END = 0x23;
    internal const ushort VK_HOME = 0x24;
    internal const ushort VK_UP = 0x26;
    internal const ushort VK_DOWN = 0x28;
    internal const ushort VK_F6 = 0x75;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll")]
    internal static extern bool ClientToScreen(nint hWnd, ref POINT point);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint hWnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool EnumChildWindows(nint hWndParent, EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowEnabled(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    internal static extern nint GetParent(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    /// <summary>Milliseconds since the last keyboard or mouse input on this PC (any program).</summary>
    internal static long IdleMilliseconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? (uint)Environment.TickCount - info.dwTime : 0;
    }

    internal const uint BM_CLICK = 0x00F5;
    internal const uint CB_GETCOUNT = 0x0146;

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    internal static string WindowText(nint hwnd)
    {
        if (hwnd == 0) return string.Empty;
        var len = GetWindowTextLengthW(hwnd);
        if (len <= 0) return string.Empty;
        var sb = new StringBuilder(len + 2);
        _ = GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_GETTEXTLENGTH = 0x000E;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, StringBuilder lParam, uint flags, uint timeoutMs, out nint result);

    // GetWindowText does not return an edit control's content when the control belongs to
    // another process; WM_GETTEXT does. Returns null when ERP did not answer in time.
    internal static string? ControlText(nint hwnd, uint timeoutMs = 150)
    {
        if (hwnd == 0) return string.Empty;
        if (SendMessageTimeout(hwnd, WM_GETTEXTLENGTH, 0, 0, SMTO_ABORTIFHUNG, timeoutMs, out var length) == 0)
            return null;
        if (length <= 0) return string.Empty;
        var sb = new StringBuilder((int)length + 2);
        if (SendMessageTimeout(hwnd, WM_GETTEXT, sb.Capacity, sb, SMTO_ABORTIFHUNG, timeoutMs, out _) == 0)
            return null;
        return sb.ToString();
    }

    internal const uint CB_GETCURSEL = 0x0147;
    private const uint CB_GETLBTEXT = 0x0148;
    private const uint CB_GETLBTEXTLEN = 0x0149;

    /// <summary>The items of a standard Windows combo box (class ComboBox／TComboBox), or null when it does not answer.</summary>
    internal static IReadOnlyList<string>? ComboItems(nint hwnd, uint timeoutMs = 150)
    {
        if (QueryNumber(hwnd, CB_GETCOUNT, timeoutMs) is not { } count || count < 0) return null;
        var items = new List<string>();
        for (var i = 0; i < Math.Min(count, 50); i++)
        {
            if (SendMessageTimeout(hwnd, CB_GETLBTEXTLEN, i, 0, SMTO_ABORTIFHUNG, timeoutMs, out var length) == 0 || length < 0) return null;
            var sb = new StringBuilder((int)length + 2);
            if (SendMessageTimeout(hwnd, CB_GETLBTEXT, i, sb, SMTO_ABORTIFHUNG, timeoutMs, out _) == 0) return null;
            items.Add(sb.ToString());
        }
        return items;
    }

    /// <summary>Sends a message that returns a number, with the same hang protection.</summary>
    internal static nint? QueryNumber(nint hwnd, uint msg, uint timeoutMs = 150) =>
        SendMessageTimeout(hwnd, msg, 0, 0, SMTO_ABORTIFHUNG, timeoutMs, out var result) == 0 ? null : result;

    internal static string ClassName(nint hwnd)
    {
        if (hwnd == 0) return string.Empty;
        var sb = new StringBuilder(256);
        var n = GetClassNameW(hwnd, sb, sb.Capacity);
        return n > 0 ? sb.ToString() : string.Empty;
    }

    [DllImport("imm32.dll")]
    private static extern nint ImmGetDefaultIMEWnd(nint hWnd);

    private const uint WM_IME_CONTROL = 0x0283;
    private const nint IMC_GETOPENSTATUS = 0x0005;
    private const nint IMC_SETOPENSTATUS = 0x0006;

    /// <summary>
    /// Turns the IME of another process's window to non-conversion (英數) through its default
    /// IME window. Returns true when an open IME was closed.
    /// </summary>
    internal static bool CloseImeFor(nint hwnd)
    {
        if (hwnd == 0) return false;
        var ime = ImmGetDefaultIMEWnd(hwnd);
        if (ime == 0) return false;
        if (SendMessage(ime, WM_IME_CONTROL, IMC_GETOPENSTATUS, 0) == 0) return false;
        SendMessage(ime, WM_IME_CONTROL, IMC_SETOPENSTATUS, 0);
        return true;
    }

    internal static nint FocusedControlOfForeground()
    {
        var fg = GetForegroundWindow();
        if (fg == 0) return 0;
        var tid = GetWindowThreadProcessId(fg, out _);
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(tid, ref info) ? info.hwndFocus : 0;
    }
}

internal sealed record WindowControl(
    nint Handle,
    nint Parent,
    string ClassName,
    string Text,
    NativeMethods.RECT Rect,
    bool Visible,
    bool Enabled);
