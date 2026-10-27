using System.Runtime.InteropServices;

namespace CYERPAutoInput;

/// <summary>
/// While CY types into ERP the physical mouse is ignored, so a nudge cannot move the focus to
/// another field (user, 2026-10-11). A low-level mouse hook on its own thread drops every mouse
/// event that is not injected; CY's own clicks (SendInput) and SetCursorPos still work. A
/// physical Esc releases the lock from the hook thread itself, so the mouse comes back even if
/// the UI thread were stuck; the normal Esc emergency stop then cancels the run.
/// </summary>
internal sealed class PhysicalMouseLock : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmQuit = 0x0012;
    private const uint LlmhfInjected = 0x01;
    private const uint LlkhfInjected = 0x10;

    private readonly AppLogger _log;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly HookProc _mouseProc;
    private readonly HookProc _keyboardProc;
    private uint _threadId;
    private volatile bool _released;
    private int _escapeRaised;

    /// <summary>
    /// A physical Esc, raised once on a pool thread the moment the hook sees it (user,
    /// 2026-10-12: the run should stop at once, not when the current step happens to end).
    /// </summary>
    public event Action? EscapePressed;
    private int _blocked;

    /// <summary>
    /// The one place the physical mouse may move and click while locked (the 紙本發票待列印 box,
    /// user 2026-10-27); null for none. Shared by every lock.
    /// </summary>
    public static Rectangle? Allowed
    {
        get => _allowed as Rectangle?;
        set => _allowed = value;
    }
    private static volatile object? _allowed;

    private PhysicalMouseLock(AppLogger log)
    {
        _log = log;
        _mouseProc = MouseHook;
        _keyboardProc = KeyboardHook;
        _thread = new Thread(Run) { IsBackground = true, Name = "CY mouse lock" };
    }

    /// <summary>Locks the physical mouse until disposed or a physical Esc.</summary>
    public static PhysicalMouseLock Engage(AppLogger log)
    {
        var mouseLock = new PhysicalMouseLock(log);
        mouseLock._thread.Start();
        mouseLock._ready.Wait(TimeSpan.FromSeconds(2));
        return mouseLock;
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        var module = GetModuleHandle(null);
        var mouse = SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);
        var keyboard = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, module, 0);
        if (mouse == 0) _log.Warn("mouse-lock", $"mouse hook failed error={Marshal.GetLastWin32Error()}");
        else _log.Info("mouse-lock", "physical mouse locked");
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
            if (mouse != 0) UnhookWindowsHookEx(mouse);
            if (keyboard != 0) UnhookWindowsHookEx(keyboard);
            _log.Info("mouse-lock", $"physical mouse released blocked_events={_blocked}");
        }
    }

    private nint MouseHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && !_released)
        {
            var info = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            if ((info.flags & LlmhfInjected) == 0 && !(_allowed is Rectangle area && area.Contains(info.pt)))
            {
                Interlocked.Increment(ref _blocked);
                return 1;
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private nint KeyboardHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (wParam == WmKeyDown || wParam == WmSysKeyDown))
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            // Only a physical Esc; the Esc CY sends to abandon an ERP document is injected.
            if (info.vkCode == (uint)NativeMethods.VK_ESCAPE && (info.flags & LlkhfInjected) == 0)
            {
                _released = true;
                if (Interlocked.Exchange(ref _escapeRaised, 1) == 0 && EscapePressed is { } escape)
                    ThreadPool.QueueUserWorkItem(_ => escape());
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    /// <summary>
    /// The mouse free again while the keyboard hook stays (user, 2026-10-26: inside 蝦皮全自動 a
    /// question for the person must not end the watch for Esc, or Esc stops nothing).
    /// </summary>
    public void Release()
    {
        _released = true;
        _log.Info("mouse-lock", "physical mouse released; Esc still watched");
    }

    public void Relock()
    {
        _released = false;
        _log.Info("mouse-lock", "physical mouse locked again");
    }

    public void Dispose()
    {
        _released = true;
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public Point pt;
        public uint mouseData, flags, time;
        public nint dwExtraInfo;
    }

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
