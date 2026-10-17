using System.Runtime.InteropServices;

namespace CYERPAutoQuery;

// Window lookup and native input, adapted from CYERPAutoInput (user decision 2026-10-16:
// copy the needed parts now, merge both tools into a shared library later).
internal static class Win32Automation
{
    public static IReadOnlyList<nint> FindInvi02Windows() => FindFormWindows(ErpForm.Invi02);

    /// <summary>
    /// Every visible window of <paramref name="form"/> (INVI02, COPI01 …), whatever else ERP has
    /// open. ERP message boxes can reuse the form caption, so when the real form class is present
    /// only that class counts. A hidden one is a form the user closed and Delphi kept in memory;
    /// driving it went wrong (V0.4.0 LOG, 2026-10-16: visible=False), so it is opened again
    /// through 作業速查 instead.
    /// </summary>
    public static IReadOnlyList<nint> FindFormWindows(ErpForm form)
    {
        var found = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) && form.IsTitle(NativeMethods.WindowText(hwnd)) &&
                !Invi02Layout.IsMessageDialogClass(NativeMethods.ClassName(hwnd)))
                found.Add(hwnd);
            return true;
        }, 0);
        var forms = found.Where(h => NativeMethods.ClassName(h).Equals(form.FormClass, StringComparison.OrdinalIgnoreCase)).ToList();
        return forms.Count > 0 ? forms : found;
    }

    /// <summary>
    /// Visible SMART ERP main windows (鼎新電腦Conductor), top of the z-order first. Delphi's
    /// hidden TApplication window carries the same caption but has no size and can never come
    /// to the front (V0.4.0 LOG, 2026-10-16: 「無法把 ERP 主視窗帶到前景」), so it is skipped,
    /// as is anything too small to be the main form (a minimized form still counts).
    /// </summary>
    public static IReadOnlyList<nint> FindErpMainWindows()
    {
        var found = new List<nint>();
        var applications = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!Invi02Layout.IsErpMainTitle(NativeMethods.WindowText(hwnd))) return true;
            if (NativeMethods.ClassName(hwnd).Equals("TApplication", StringComparison.OrdinalIgnoreCase)) applications.Add(hwnd);
            else if (NativeMethods.IsWindowVisible(hwnd) && Invi02Layout.IsErpMainClass(NativeMethods.ClassName(hwnd)) && IsMainSized(hwnd))
                found.Add(hwnd);
            return true;
        }, 0);
        if (found.Count > 0) return found;

        // The main form's own caption need not say Conductor: on the user's PC only the hidden
        // TApplication window did (V0.4.0 Build 2 LOG). Then the main form is the largest visible
        // window of that ERP process that is not a program form (INVxx) or a message box.
        foreach (var app in applications)
        {
            var pid = ProcessId(app);
            var candidates = new List<(nint Handle, long Area)>();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (hwnd != app && ProcessId(hwnd) == pid && NativeMethods.IsWindowVisible(hwnd) &&
                    Invi02Layout.IsErpMainClass(NativeMethods.ClassName(hwnd)) && IsMainSized(hwnd) &&
                    !Invi02Layout.IsInvi02Title(NativeMethods.WindowText(hwnd)) &&
                    !ErpForm.Copi01.IsTitle(NativeMethods.WindowText(hwnd)) &&
                    !ErpForm.Puri01.IsTitle(NativeMethods.WindowText(hwnd)) &&
                    !Invq01Layout.IsInvq01Title(NativeMethods.WindowText(hwnd)) &&
                    NativeMethods.GetWindowRect(hwnd, out var r))
                    candidates.Add((hwnd, NativeMethods.IsIconic(hwnd) ? 0 : (long)r.Width * r.Height));
                return true;
            }, 0);
            found.AddRange(candidates.OrderByDescending(c => c.Area).Select(c => c.Handle));
        }
        return found;
    }

    /// <summary>
    /// When ERP is minimized Delphi hides its main form (V0.4.0 Build 3 LOG, 2026-10-16: only the
    /// iconic TApplication window was left). Returns that TApplication window and the hidden
    /// main form (class <see cref="Invi02Layout.ErpMainFormClass"/>), or nothing.
    /// </summary>
    public static (nint Application, nint Main)? FindMinimizedErp()
    {
        var applications = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (Invi02Layout.IsErpMainTitle(NativeMethods.WindowText(hwnd)) &&
                NativeMethods.ClassName(hwnd).Equals("TApplication", StringComparison.OrdinalIgnoreCase))
                applications.Add(hwnd);
            return true;
        }, 0);
        foreach (var app in applications)
        {
            var pid = ProcessId(app);
            nint main = 0;
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (ProcessId(hwnd) == pid && NativeMethods.ClassName(hwnd).Equals(Invi02Layout.ErpMainFormClass, StringComparison.OrdinalIgnoreCase))
                {
                    main = hwnd;
                    return false;
                }
                return true;
            }, 0);
            if (main != 0) return (app, main);
        }
        return null;
    }

    private static bool IsMainSized(nint hwnd) =>
        NativeMethods.IsIconic(hwnd) || NativeMethods.GetWindowRect(hwnd, out var r) && r.Width >= 300 && r.Height >= 200;

    /// <summary>
    /// Visible top-level windows of <paramref name="root"/>'s process, other than the listed
    /// ones, that overlap <paramref name="area"/> (a drop-down list is such a popup).
    /// </summary>
    public static int CountProcessPopupsOver(nint root, Rectangle area, params nint[] except)
    {
        var pid = ProcessId(root);
        var count = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (except.Contains(hwnd) || hwnd == root || !NativeMethods.IsWindowVisible(hwnd) || ProcessId(hwnd) != pid) return true;
            if (!NativeMethods.GetWindowRect(hwnd, out var r)) return true;
            var bounds = r.ToRectangle();
            if (bounds.Width is >= 10 and <= 1200 && bounds.Height is >= 10 and <= 800 && bounds.IntersectsWith(area)) count++;
            return true;
        }, 0);
        return count;
    }

    /// <summary>Every visible top-level window of <paramref name="root"/>'s process except root itself.</summary>
    public static IReadOnlySet<nint> VisibleProcessWindows(nint root)
    {
        var pid = ProcessId(root);
        var set = new HashSet<nint>();
        if (pid == 0) return set;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd != root && NativeMethods.IsWindowVisible(hwnd) && ProcessId(hwnd) == pid) set.Add(hwnd);
            return true;
        }, 0);
        return set;
    }

    /// <summary>
    /// Visible top-level windows of the ERP process (not <paramref name="root"/>) that overlap
    /// <paramref name="area"/>, of any size: a Ribbon drop-down is such a small popup
    /// (CYERPAutoInput).
    /// </summary>
    public static IReadOnlyList<Rectangle> ProcessPopupsNear(nint root, Rectangle area)
    {
        var popups = new List<Rectangle>();
        foreach (var hwnd in VisibleProcessWindows(root))
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var rect)) continue;
            var bounds = rect.ToRectangle();
            if (bounds.Width is < 20 or > 1200 || bounds.Height is < 16 or > 800 || !bounds.IntersectsWith(area)) continue;
            popups.Add(bounds);
        }
        return popups;
    }

    public static uint ProcessId(nint hwnd)
    {
        if (hwnd == 0) return 0;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    public static bool PrepareForeground(nint hwnd, AppLogger? log = null)
    {
        if (hwnd == 0) return false;
        // A window parented inside another (an ERP dialog shown within INVI02) is brought
        // forward through its top-level window.
        hwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) is var top && top != 0 ? top : hwnd;
        if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
        NativeMethods.BringWindowToTop(hwnd);
        NativeMethods.SetForegroundWindow(hwnd);
        for (var i = 0; i < 10; i++)
        {
            if (NativeMethods.GetForegroundWindow() == hwnd) return true;
            Thread.Sleep(50);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        log?.Warn("focus", $"foreground confirmation failed hwnd=0x{hwnd:X}");
        return NativeMethods.GetForegroundWindow() == hwnd;
    }

    public static IReadOnlyList<WindowControl> EnumerateChildren(nint root)
    {
        var list = new List<WindowControl>();
        NativeMethods.EnumChildWindows(root, (hwnd, _) =>
        {
            NativeMethods.GetWindowRect(hwnd, out var rect);
            list.Add(new WindowControl(
                hwnd,
                NativeMethods.GetParent(hwnd),
                NativeMethods.ClassName(hwnd),
                NativeMethods.WindowText(hwnd),
                rect,
                NativeMethods.IsWindowVisible(hwnd),
                NativeMethods.IsWindowEnabled(hwnd)));
            return true;
        }, 0);
        return list;
    }

    /// <summary>The TcxTabSheet (or other window) inside <paramref name="root"/> with this caption.</summary>
    public static WindowControl? FindTabSheet(nint root, string caption)
    {
        var target = Invi02Layout.NormalizeCaption(caption);
        return EnumerateChildren(root)
            .FirstOrDefault(c => c.ClassName.Contains("TabSheet", StringComparison.OrdinalIgnoreCase) &&
                                 Invi02Layout.NormalizeCaption(c.Text) == target);
    }

    /// <summary>
    /// Input-like controls under <paramref name="container"/>. DevExpress editors host an inner
    /// edit window; only the outermost input control is kept so each field is listed once.
    /// </summary>
    public static List<WindowControl> InputControls(nint container, bool visibleOnly)
    {
        var all = EnumerateChildren(container);
        var inputs = all.Where(c => Invi02Layout.IsInputLike(c.ClassName)).ToList();
        var handles = inputs.Select(c => c.Handle).ToHashSet();
        return inputs
            .Where(c => !visibleOnly || c.Visible)
            .Where(c => !HasAncestorIn(c.Parent, handles, container))
            .ToList();
    }

    private static bool HasAncestorIn(nint parent, HashSet<nint> handles, nint stop)
    {
        for (var p = parent; p != 0 && p != stop; p = NativeMethods.GetParent(p))
            if (handles.Contains(p)) return true;
        return false;
    }

    /// <summary>
    /// True when <paramref name="child"/> is <paramref name="ancestor"/> or lies inside it. Walks
    /// real parents only (GA_PARENT): GetParent would also climb to the owner of a popup, so
    /// an ERP popup over INVI02 would count as part of INVI02.
    /// </summary>
    public static bool IsInside(nint child, nint ancestor)
    {
        if (child == 0 || ancestor == 0) return false;
        var p = child;
        for (var depth = 0; p != 0 && depth < 64; depth++)
        {
            if (p == ancestor) return true;
            p = NativeMethods.GetAncestor(p, NativeMethods.GA_PARENT);
        }
        return false;
    }

    /// <summary>True when the screen at <paramref name="point"/> belongs to <paramref name="window"/> (or a window inside it), i.e. nothing covers it.</summary>
    public static bool IsPointShownBy(nint window, Point point)
    {
        var hit = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = point.X, Y = point.Y });
        return hit != 0 && IsInside(hit, window);
    }

    public static bool IsReadOnly(nint hwnd) =>
        (NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64() & NativeMethods.ES_READONLY) != 0;

    public static Point Center(NativeMethods.RECT r) => new((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
}

internal static class InputSender
{
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;

    public static void Click(Point screenPoint)
    {
        NativeMethods.SetCursorPos(screenPoint.X, screenPoint.Y);
        Thread.Sleep(30);
        var inputs = new[]
        {
            new NativeMethods.INPUT { type = 0, U = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { dwFlags = MouseLeftDown } } },
            new NativeMethods.INPUT { type = 0, U = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { dwFlags = MouseLeftUp } } }
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public static void Press(ushort key)
    {
        var inputs = new[] { Key(key, 0), Key(key, NativeMethods.KEYEVENTF_KEYUP) };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public static void UnicodeText(string text, int delayMs = 24)
    {
        // A Chinese IME left open in ERP holds typed characters in its composition instead of
        // the field (CYERPAutoInput, 2026-10-11): switch the receiving field's IME off first.
        if (NativeMethods.CloseImeFor(NativeMethods.FocusedControlOfForeground()))
            Thread.Sleep(60);
        foreach (var ch in text)
        {
            var inputs = new[] { Unicode(ch, 0), Unicode(ch, NativeMethods.KEYEVENTF_KEYUP) };
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
            if (delayMs > 0) Thread.Sleep(delayMs);
        }
    }

    /// <summary>Clears a field without Ctrl+A (forbidden in SMART ERP): End, then Backspace.</summary>
    public static void EndBackspace(int count)
    {
        Press(NativeMethods.VK_END);
        for (var i = 0; i < count; i++) Press(NativeMethods.VK_BACK);
    }

    private static NativeMethods.INPUT Key(ushort key, uint flags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT { wVk = key, dwFlags = flags } }
    };

    private static NativeMethods.INPUT Unicode(char ch, uint flags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = NativeMethods.KEYEVENTF_UNICODE | flags }
        }
    };
}
