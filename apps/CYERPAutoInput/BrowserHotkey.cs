using System.Runtime.InteropServices;

namespace CYERPAutoInput;

/// <summary>
/// F8 in the browser opens Shopee 光學辨識 and starts the page scan (user, 2026-10-12: a key
/// while CY runs in the background). F8 is not a Chrome or Edge shortcut (F1 help, F3 find,
/// F5 reload, F6 address bar, F7 caret browsing, F9 Edge's reader view, F10 menu, F11 full
/// screen, F12 developer tools). Only a physical F8, without Ctrl, Alt or
/// Shift, pressed while a browser window is in front is taken; everywhere else (ERP, CY
/// itself) F8 passes through untouched. A low-level hook on its own thread, as the mouse lock.
/// </summary>
internal sealed class BrowserHotkey : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmQuit = 0x0012;
    private const uint LlkhfInjected = 0x10;
    public const int Key = 0x77; // VK_F8
    public const string KeyName = "F8";

    private readonly AppLogger _log;
    private readonly Action _pressed;
    private readonly Thread _thread;
    private readonly HookProc _proc;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private bool _swallowKeyUp;

    private BrowserHotkey(AppLogger log, Action pressed)
    {
        _log = log;
        _pressed = pressed;
        _proc = Hook;
        _thread = new Thread(Run) { IsBackground = true, Name = "CY browser hotkey" };
    }

    /// <param name="pressed">Called on the hook thread; marshal to the UI thread.</param>
    public static BrowserHotkey Start(AppLogger log, Action pressed)
    {
        var hotkey = new BrowserHotkey(log, pressed);
        hotkey._thread.Start();
        hotkey._ready.Wait(TimeSpan.FromSeconds(2));
        return hotkey;
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        var hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (hook == 0) _log.Warn("hotkey", $"browser {KeyName} hook failed error={Marshal.GetLastWin32Error()}");
        else _log.Info("hotkey", $"browser {KeyName} ready");
        _ready.Set();
        try
        {
            while (GetMessage(out var msg, 0, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            if (hook != 0) UnhookWindowsHookEx(hook);
        }
    }

    private nint Hook(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if (info.vkCode == Key && (info.flags & LlkhfInjected) == 0)
            {
                if (wParam == WmKeyUp && _swallowKeyUp) { _swallowKeyUp = false; return 1; }
                if (wParam == WmKeyDown && !Modified() && BrowserInFront())
                {
                    _swallowKeyUp = true;
                    try { _pressed(); }
                    catch (Exception ex) { _log.Error("hotkey", ex); }
                    return 1;
                }
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private static bool Modified() => Down(0x11) || Down(0x12) || Down(0x10); // Ctrl, Alt, Shift

    private static bool Down(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

    private static bool BrowserInFront()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0) return false;
        NativeMethods.GetWindowThreadProcessId(foreground, out var pid);
        if (pid == (uint)Environment.ProcessId) return false;
        var cls = NativeMethods.ClassName(foreground);
        return cls is "Chrome_WidgetWin_1" or "MozillaWindowClass";
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode, scanCode, flags, time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public Point pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
