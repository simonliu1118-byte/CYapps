using System.Runtime.InteropServices;
using System.Text;
using Accessibility;

namespace CYERPAutoInput;

/// <summary>
/// Read-only ERP structure probe. Writes the Win32 control tree and the MSAA
/// (IAccessible) tree of COPI08 and of the other visible ERP windows (for example an
/// open F2 lookup) to a local text file, so automation can move from screen
/// coordinates / OCR to control-level access. It never sends input, clicks or
/// changes focus, and every walk is bounded by node, depth and time limits.
/// </summary>
internal static class ErpProbe
{
    private const int MaxDepth = 14;
    private const int MaxNodes = 4000;
    private const int TimeLimitMs = 20000;
    private const uint ObjIdClient = 0xFFFFFFFC;
    private static readonly Guid IidIAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(nint hwnd, uint objectId, ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object accessible);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleChildren(IAccessible container, int childStart, int children,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] result, out int obtained);

    /// <summary>Runs on a dedicated STA thread (MSAA is COM) and returns the output path.</summary>
    public static Task<string> RunAsync(AppLogger log)
    {
        var done = new TaskCompletionSource<string>();
        var thread = new Thread(() =>
        {
            try { done.SetResult(Run(log)); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true, Name = "ErpProbe" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static string Run(AppLogger log)
    {
        var windows = Win32Automation.FindCopi08Windows();
        if (windows.Count == 0) throw new InvalidOperationException("找不到 COPI08 視窗；請先開啟銷貨單建立作業再探測。");
        if (windows.Count > 1) throw new InvalidOperationException($"偵測到 {windows.Count} 個 COPI08 視窗；請只保留一個再探測。");

        var root = windows[0];
        var budget = new Budget();
        var sb = new StringBuilder();
        sb.AppendLine($"CYERPAutoInput ERP structure probe {AppVersionInfo.Display}");
        sb.AppendLine($"time={DateTime.Now:yyyy-MM-dd HH:mm:ss} diagnostic={log.Diagnostic} (diagnostic=False: data-bearing text shown as <n chars>)");
        sb.AppendLine("Read-only: no input, clicks or focus changes were sent to ERP.");

        var targets = new List<nint> { root };
        targets.AddRange(Win32Automation.FindVisibleProcessPeerWindows(root));
        foreach (var hwnd in targets)
        {
            NativeMethods.GetWindowRect(hwnd, out var rect);
            sb.AppendLine();
            sb.AppendLine($"=== WINDOW 0x{hwnd:X} class={NativeMethods.ClassName(hwnd)} title={ProbeRedaction.Show(NativeMethods.WindowText(hwnd), true)} rect={rect.Left},{rect.Top},{rect.Width}x{rect.Height}");
            sb.AppendLine("--- Win32 control tree (rect relative to window) ---");
            WriteWin32Tree(sb, hwnd, rect, log.Diagnostic, budget);
            sb.AppendLine("--- MSAA tree ---");
            WriteMsaaTree(sb, hwnd, rect, log.Diagnostic, budget);
            if (budget.Exhausted) break;
        }

        if (budget.Exhausted) sb.AppendLine().AppendLine($"[stopped: {budget.Reason}]");
        var path = Path.Combine(log.LogDirectory, $"erp-probe_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        log.Info("probe", $"ERP structure probe written nodes={budget.Nodes} stopped={budget.Exhausted} path={path}");
        return path;
    }

    private static void WriteWin32Tree(StringBuilder sb, nint root, NativeMethods.RECT origin, bool diagnostic, Budget budget)
    {
        var controls = Win32Automation.EnumerateChildren(root);
        var byParent = controls.ToLookup(c => c.Parent);

        void Walk(nint parent, int depth)
        {
            if (depth > MaxDepth) return;
            foreach (var c in byParent[parent])
            {
                if (!budget.Take()) return;
                var text = ProbeRedaction.Show(c.Text, ProbeRedaction.KeepWindowText(c.ClassName, diagnostic));
                sb.Append(' ', depth * 2)
                  .AppendLine($"0x{c.Handle:X} {c.ClassName} visible={c.Visible} enabled={c.Enabled} rect={c.Rect.Left - origin.Left},{c.Rect.Top - origin.Top},{c.Rect.Width}x{c.Rect.Height} text={text}");
                Walk(c.Handle, depth + 1);
            }
        }

        Walk(root, 0);
    }

    private static void WriteMsaaTree(StringBuilder sb, nint hwnd, NativeMethods.RECT origin, bool diagnostic, Budget budget)
    {
        var iid = IidIAccessible;
        if (AccessibleObjectFromWindow(hwnd, ObjIdClient, ref iid, out var obj) != 0 || obj is not IAccessible acc)
        {
            sb.AppendLine("(no IAccessible for this window)");
            return;
        }

        void Walk(IAccessible node, object childId, int depth)
        {
            if (depth > MaxDepth || !budget.Take()) return;
            var role = Safe(() => Convert.ToInt32(node.get_accRole(childId)), -1);
            var name = Safe(() => node.get_accName(childId), null);
            var value = Safe(() => node.get_accValue(childId), null);
            var shortcut = Safe(() => node.get_accKeyboardShortcut(childId), null);
            var state = Safe(() => Convert.ToInt64(node.get_accState(childId)), 0L);
            var location = Safe(() =>
            {
                node.accLocation(out var l, out var t, out var w, out var h, childId);
                return $"{l - origin.Left},{t - origin.Top},{w}x{h}";
            }, "?");

            sb.Append(' ', depth * 2).Append($"role=0x{role:X2} name={ProbeRedaction.Show(name, ProbeRedaction.KeepMsaaName(role, diagnostic))}");
            if (!string.IsNullOrEmpty(value)) sb.Append($" value={ProbeRedaction.Show(value, diagnostic)}");
            if (!string.IsNullOrEmpty(shortcut)) sb.Append($" shortcut=\"{shortcut}\"");
            sb.AppendLine($" state=0x{state:X} loc={location}");

            if (childId is not int id || id != 0) return; // simple child elements have no children
            var count = Safe(() => node.accChildCount, 0);
            if (count <= 0) return;
            var children = new object[Math.Min(count, 500)];
            if (AccessibleChildren(node, 0, children.Length, children, out var obtained) != 0) return;
            for (var i = 0; i < obtained && !budget.Exhausted; i++)
            {
                switch (children[i])
                {
                    case IAccessible child:
                        Walk(child, 0, depth + 1);
                        break;
                    case int simpleId:
                        Walk(node, simpleId, depth + 1);
                        break;
                }
            }
        }

        Walk(acc, 0, 0);
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }

    private sealed class Budget
    {
        private readonly long _deadline = Environment.TickCount64 + TimeLimitMs;
        public int Nodes { get; private set; }
        public string Reason { get; private set; } = string.Empty;
        public bool Exhausted => Reason.Length > 0;

        public bool Take()
        {
            if (Exhausted) return false;
            if (++Nodes > MaxNodes) Reason = $"node limit {MaxNodes}";
            else if (Environment.TickCount64 > _deadline) Reason = $"time limit {TimeLimitMs} ms";
            return !Exhausted;
        }
    }
}
