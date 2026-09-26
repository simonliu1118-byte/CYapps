using System.Runtime.InteropServices;
using System.Text;

namespace CYERPAutoInput;

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

    internal const uint WM_KEYDOWN = 0x0100;
    internal const uint WM_KEYUP = 0x0101;
    internal const uint WM_CHAR = 0x0102;
    internal const uint WM_SETTEXT = 0x000C;
    internal const uint WM_COPY = 0x0301;
    internal const uint EM_SETSEL = 0x00B1;

    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_UNICODE = 0x0004;

    internal const ushort VK_BACK = 0x08;
    internal const ushort VK_TAB = 0x09;
    internal const ushort VK_RETURN = 0x0D;
    internal const ushort VK_ESCAPE = 0x1B;
    internal const ushort VK_HOME = 0x24;
    internal const ushort VK_END = 0x23;
    internal const ushort VK_DOWN = 0x28;
    internal const ushort VK_F2 = 0x71;

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
    internal static extern nint SendMessage(nint hWnd, uint Msg, nint wParam, string? lParam);

    [DllImport("user32.dll")]
    internal static extern nint SendMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

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

    internal static string ClassName(nint hwnd)
    {
        if (hwnd == 0) return string.Empty;
        var sb = new StringBuilder(256);
        var n = GetClassNameW(hwnd, sb, sb.Capacity);
        return n > 0 ? sb.ToString() : string.Empty;
    }

    internal static nint FocusedControlOfForeground(nint expectedRoot = 0)
    {
        var fg = GetForegroundWindow();
        if (fg == 0) return 0;
        if (expectedRoot != 0 && fg != expectedRoot) return 0;
        var tid = GetWindowThreadProcessId(fg, out _);
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(tid, ref info)) return 0;

        var focus = info.hwndFocus;
        if (focus == 0 || expectedRoot == 0) return focus;

        // In the COPI08 F2 lookup, clicking a data row normally moves focus into
        // TcxCustomInnerTextEdit, which is the active editor owned by TcxGridSite.
        // For callers that are validating the F2 selection, normalize that editor to
        // its owning grid. This keeps the safety check strict without treating a valid
        // DevExpress editing state as a focus failure.
        if (WindowText(expectedRoot).Contains("F2開窗查詢", StringComparison.Ordinal) &&
            ClassName(focus).Equals("TcxCustomInnerTextEdit", StringComparison.OrdinalIgnoreCase))
        {
            for (var parent = GetParent(focus); parent != 0 && parent != expectedRoot; parent = GetParent(parent))
            {
                if (ClassName(parent).Equals("TcxGridSite", StringComparison.OrdinalIgnoreCase))
                    return parent;
            }
        }

        return focus;
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
