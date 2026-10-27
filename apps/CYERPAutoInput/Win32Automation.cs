using System.Runtime.InteropServices;

namespace CYERPAutoInput;

internal static class Win32Automation
{
    /// <summary>
    /// Every visible COPI08 window, top of the z-order first, whatever else ERP has open (as
    /// CYERPAutoQuery finds its forms; user, 2026-10-17: the two tools behave alike). A hidden
    /// one is a form the user closed and Delphi kept in memory, or one hidden with ERP
    /// minimized; the caller restores ERP or opens COPI08 again through 作業速查.
    /// </summary>
    public static IReadOnlyList<nint> FindCopi08Windows()
    {
        var found = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) && IsCopi08Title(NativeMethods.WindowText(hwnd)) &&
                !IsMessageDialogClass(NativeMethods.ClassName(hwnd)))
                found.Add(hwnd);
            return true;
        }, 0);
        // ERP message boxes (e.g. 是否放棄本次新增) reuse the COPI08 caption; when the real
        // form class is present, only that class counts as a COPI08 window.
        var forms = found.Where(h => NativeMethods.ClassName(h).Equals(Copi08FormClass, StringComparison.OrdinalIgnoreCase)).ToList();
        return forms.Count > 0 ? forms : found;
    }

    public const string Copi08Code = "COPI08";
    private const string Copi08FormClass = "TfrmCopi08";

    private static bool IsCopi08Title(string title) =>
        title.Contains("銷貨單建立作業", StringComparison.Ordinal) && title.Contains(Copi08Code, StringComparison.OrdinalIgnoreCase);

    /// <summary>An ERP program form's caption carries its code (COPI08, INVI02 …).</summary>
    private static readonly System.Text.RegularExpressions.Regex ProgramCode =
        new(@"[A-Z]{4}\d{2}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Another ERP program form (INVI02 …, not a message box over one): not a dialog of COPI08.</summary>
    private static bool IsProgramForm(nint hwnd) =>
        ProgramCode.IsMatch(NativeMethods.WindowText(hwnd)) && !IsMessageDialogClass(NativeMethods.ClassName(hwnd));

    /// <summary>The SMART ERP main window: 「鼎新電腦Conductor [user]」 (CYERPAutoQuery).</summary>
    private static bool IsErpMainTitle(string title) =>
        title.Contains("Conductor", StringComparison.OrdinalIgnoreCase) && !ProgramCode.IsMatch(title);

    /// <summary>SMART ERP's main form class; its caption is 「DSForm」, not Conductor (CYERPAutoQuery).</summary>
    private const string ErpMainFormClass = "SmartERP_iSM_MainMenu";

    private static bool IsErpMainClass(string className) =>
        !IsMessageDialogClass(className) && !className.Equals("TApplication", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Visible SMART ERP main windows, top of the z-order first (copied from CYERPAutoQuery).
    /// Delphi's hidden TApplication window carries the Conductor caption but can never come to
    /// the front, so it is skipped; when only it says Conductor, the main form is the largest
    /// visible window of that process that is not a program form or a message box.
    /// </summary>
    public static IReadOnlyList<nint> FindErpMainWindows()
    {
        var found = new List<nint>();
        var applications = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsErpMainTitle(NativeMethods.WindowText(hwnd))) return true;
            if (NativeMethods.ClassName(hwnd).Equals("TApplication", StringComparison.OrdinalIgnoreCase)) applications.Add(hwnd);
            else if (NativeMethods.IsWindowVisible(hwnd) && IsErpMainClass(NativeMethods.ClassName(hwnd)) && IsMainSized(hwnd))
                found.Add(hwnd);
            return true;
        }, 0);
        if (found.Count > 0) return found;

        foreach (var app in applications)
        {
            var pid = ProcessId(app);
            var candidates = new List<(nint Handle, long Area)>();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (hwnd != app && ProcessId(hwnd) == pid && NativeMethods.IsWindowVisible(hwnd) &&
                    IsErpMainClass(NativeMethods.ClassName(hwnd)) && IsMainSized(hwnd) &&
                    !ProgramCode.IsMatch(NativeMethods.WindowText(hwnd)) &&
                    NativeMethods.GetWindowRect(hwnd, out var r))
                    candidates.Add((hwnd, NativeMethods.IsIconic(hwnd) ? 0 : (long)r.Width * r.Height));
                return true;
            }, 0);
            found.AddRange(candidates.OrderByDescending(c => c.Area).Select(c => c.Handle));
        }
        return found;
    }

    /// <summary>
    /// Minimized ERP: Delphi hides its main form and leaves the iconic TApplication window
    /// (CYERPAutoQuery). That window and the hidden main form, or nothing.
    /// </summary>
    public static (nint Application, nint Main)? FindMinimizedErp()
    {
        var applications = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsErpMainTitle(NativeMethods.WindowText(hwnd)) &&
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
                if (ProcessId(hwnd) == pid && NativeMethods.ClassName(hwnd).Equals(ErpMainFormClass, StringComparison.OrdinalIgnoreCase))
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

    public static uint ProcessId(nint hwnd)
    {
        if (hwnd == 0) return 0;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    /// <summary>Every visible top-level window of <paramref name="root"/>'s process except root itself (for the LOG).</summary>
    public static IReadOnlyList<nint> VisibleProcessWindows(nint root)
    {
        var pid = ProcessId(root);
        var list = new List<nint>();
        if (pid == 0) return list;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd != root && NativeMethods.IsWindowVisible(hwnd) && ProcessId(hwnd) == pid) list.Add(hwnd);
            return true;
        }, 0);
        return list;
    }

    /// <summary>
    /// True when the window owning the screen at the area's center and inset corners is
    /// <paramref name="root"/> itself, i.e. nothing (such as the CY window) covers it.
    /// </summary>
    public static bool IsAreaShownBy(nint root, Rectangle area)
    {
        var inset = 6;
        var points = new[]
        {
            new Point(area.Left + area.Width / 2, area.Top + area.Height / 2),
            new Point(area.Left + inset, area.Top + inset),
            new Point(area.Right - inset, area.Top + inset),
            new Point(area.Left + inset, area.Bottom - inset),
            new Point(area.Right - inset, area.Bottom - inset)
        };
        foreach (var p in points)
        {
            var hit = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = p.X, Y = p.Y });
            if (hit == 0 || NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT) != root) return false;
        }
        return true;
    }

    private static bool IsMessageDialogClass(string className) =>
        className is "#32770" or "TMessageForm";

    public static bool PrepareForeground(nint hwnd, AppLogger? log = null)
    {
        if (hwnd == 0) return false;
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

    public static WindowControl? FindVisibleControl(nint root, Func<WindowControl, bool> predicate) =>
        EnumerateChildren(root).Where(c => c.Visible && predicate(c)).FirstOrDefault();

    public static WindowControl? FindLargestVisibleClass(nint root, string className)
    {
        return EnumerateChildren(root)
            .Where(c => c.Visible && c.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => Math.Max(0, c.Rect.Width) * Math.Max(0, c.Rect.Height))
            .FirstOrDefault();
    }

    public static nint FindTopLevelByTitleContains(string text)
    {
        nint found = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) &&
                NativeMethods.WindowText(hwnd).Contains(text, StringComparison.Ordinal))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, 0);
        return found;
    }

    public static bool IsInside(nint child, nint ancestor)
    {
        if (child == 0 || ancestor == 0) return false;
        for (var p = child; p != 0; p = NativeMethods.GetParent(p))
        {
            if (p == ancestor) return true;
            var next = NativeMethods.GetParent(p);
            if (next == p) break;
        }
        return false;
    }


    public static IReadOnlyList<nint> FindVisibleProcessPeerWindows(nint root)
    {
        if (root == 0) return Array.Empty<nint>();
        NativeMethods.GetWindowThreadProcessId(root, out var processId);
        if (processId == 0) return Array.Empty<nint>();

        var peers = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd == root || !NativeMethods.IsWindowVisible(hwnd)) return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != processId) return true;
            if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return true;
            if (rect.Width is < 140 or > 1200 || rect.Height is < 70 or > 800) return true;
            // Another program form (INVI02 left open by CYERPAutoQuery …) is not a dialog of
            // COPI08 and does not stop the run (user, 2026-10-17: behave like CYERPAutoQuery).
            if (IsProgramForm(hwnd)) return true;
            peers.Add(hwnd);
            return true;
        }, 0);
        return peers;
    }

    /// <summary>
    /// Visible top-level windows of the ERP process (not <paramref name="root"/>) that overlap
    /// <paramref name="area"/>, of any size: a Ribbon drop-down is such a small popup.
    /// </summary>
    public static IReadOnlyList<Rectangle> FindProcessPopupsNear(nint root, Rectangle area)
    {
        if (root == 0) return Array.Empty<Rectangle>();
        NativeMethods.GetWindowThreadProcessId(root, out var processId);
        if (processId == 0) return Array.Empty<Rectangle>();

        var popups = new List<Rectangle>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd == root || !NativeMethods.IsWindowVisible(hwnd)) return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != processId || !NativeMethods.GetWindowRect(hwnd, out var rect)) return true;
            var bounds = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
            if (bounds.Width is < 20 or > 1200 || bounds.Height is < 16 or > 800 || !bounds.IntersectsWith(area)) return true;
            popups.Add(bounds);
            return true;
        }, 0);
        return popups;
    }

    public static bool WindowTreeContainsText(nint hwnd, string expectedText)
    {
        var target = OcrTextNormalizer.Normalize(expectedText);
        if (target.Length == 0 || hwnd == 0) return false;
        if (OcrTextNormalizer.Normalize(NativeMethods.WindowText(hwnd)).Contains(target, StringComparison.Ordinal))
            return true;

        return EnumerateChildren(hwnd)
            .Where(c => c.Visible)
            .Select(c => OcrTextNormalizer.Normalize(c.Text))
            .Any(text => text.Contains(target, StringComparison.Ordinal));
    }

    public static string NormalizeLabel(string value)
    {
        return value.Trim()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("（", "(")
            .Replace("）", ")")
            .Replace("：", string.Empty)
            .Replace(":", string.Empty)
            .ToUpperInvariant();
    }

    public static WindowControl? FindInputRightOfLabel(nint root, string label)
    {
        var controls = EnumerateChildren(root);
        var norm = NormalizeLabel(label);
        var labelCtrl = controls.FirstOrDefault(c => c.Visible && NormalizeLabel(c.Text) == norm);
        if (labelCtrl is null) return null;

        var ly = (labelCtrl.Rect.Top + labelCtrl.Rect.Bottom) / 2;
        return controls
            .Where(c => c.Visible && c.Enabled && IsInputLike(c.ClassName))
            .Select(c => new
            {
                C = c,
                Dy = Math.Abs((c.Rect.Top + c.Rect.Bottom) / 2 - ly),
                Dx = c.Rect.Left - labelCtrl.Rect.Right
            })
            .Where(x => x.Dy <= 24 && x.Dx >= -20 && x.Dx <= 650)
            .OrderBy(x => x.Dy * 20 + Math.Abs(x.Dx))
            .Select(x => x.C)
            .FirstOrDefault();
    }

    public static bool IsInputLike(string className)
    {
        var s = className.ToUpperInvariant();
        return s.Contains("EDIT") || s.Contains("COMBO") || s.Contains("MEMO") ||
               s.Contains("MASK") || s.Contains("SPIN") || s.Contains("DATE") || s.Contains("LOOKUP");
    }
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
        if (ParkAt is { } park)
        {
            // Back into the 紙本發票待列印 box, so the person's mouse starts from there.
            Thread.Sleep(40);
            NativeMethods.SetCursorPos(park.X, park.Y);
        }
    }

    /// <summary>Where the cursor goes after each click while the 紙本發票待列印 box is shown; null otherwise.</summary>
    public static Point? ParkAt
    {
        get => _parkAt as Point?;
        set => _parkAt = value;
    }
    private static volatile object? _parkAt;

    /// <summary>Tick of the last Esc CY itself sent, so the global Esc watcher can ignore it.</summary>
    public static long LastSyntheticEscapeTick;

    public static bool IsRecentSyntheticEscape => Environment.TickCount64 - Interlocked.Read(ref LastSyntheticEscapeTick) < 600;

    public static void Press(ushort key)
    {
        if (key == NativeMethods.VK_ESCAPE) Interlocked.Exchange(ref LastSyntheticEscapeTick, Environment.TickCount64);
        var inputs = new[]
        {
            Key(key, 0),
            Key(key, NativeMethods.KEYEVENTF_KEYUP)
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    /// <summary>Keys held together, pressed in order and released in reverse (Ctrl+W).</summary>
    public static void Chord(params ushort[] keys)
    {
        var inputs = keys.Select(k => Key(k, 0)).Concat(keys.Reverse().Select(k => Key(k, NativeMethods.KEYEVENTF_KEYUP))).ToArray();
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    /// <summary>Set when CY closed an open IME before typing (for the log).</summary>
    public static int ImeClosedCount;

    public static void UnicodeText(string text, int delayMs = 24)
    {
        // A Chinese IME left open in ERP holds the typed characters in its composition
        // (dotted underline) instead of the field, and later drops them into whatever field
        // gets focus next (user, 2026-10-11). Switch the receiving field's IME off first.
        if (NativeMethods.CloseImeFor(NativeMethods.FocusedControlOfForeground()))
        {
            Interlocked.Increment(ref ImeClosedCount);
            Thread.Sleep(60);
        }
        foreach (var ch in text)
        {
            var inputs = new[]
            {
                Unicode(ch, 0),
                Unicode(ch, NativeMethods.KEYEVENTF_KEYUP)
            };
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
            if (delayMs > 0) Thread.Sleep(delayMs);
        }
    }

    public static void EndBackspace(int count)
    {
        Press(NativeMethods.VK_END);
        for (var i = 0; i < count; i++) Press(NativeMethods.VK_BACK);
    }

    private static NativeMethods.INPUT Key(ushort key, uint flags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT { wVk = key, dwFlags = flags }
        }
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
