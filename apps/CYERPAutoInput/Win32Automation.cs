using System.Runtime.InteropServices;

namespace CYERPAutoInput;

internal static class Win32Automation
{
    public static nint FindCopi08Window()
    {
        nint visible = 0;
        nint any = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var title = NativeMethods.WindowText(hwnd);
            if (!title.Contains("銷貨單建立作業", StringComparison.Ordinal) ||
                !title.Contains("COPI08", StringComparison.OrdinalIgnoreCase))
                return true;

            if (any == 0) any = hwnd;
            if (NativeMethods.IsWindowVisible(hwnd))
            {
                visible = hwnd;
                return false;
            }
            return true;
        }, 0);
        return visible != 0 ? visible : any;
    }

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
            peers.Add(hwnd);
            return true;
        }, 0);
        return peers;
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
    }

    public static void Press(ushort key)
    {
        var inputs = new[]
        {
            Key(key, 0),
            Key(key, NativeMethods.KEYEVENTF_KEYUP)
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public static void UnicodeText(string text, int delayMs = 24)
    {
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
