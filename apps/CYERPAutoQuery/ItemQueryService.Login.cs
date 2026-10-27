using System.Diagnostics;

namespace CYERPAutoQuery;

/// <summary>
/// ERP 自動登入與閒置關閉 (user, 2026-10-19). When a query finds no ERP main window, ERP is started
/// from the configured program and logged in: 帳號 → Enter → 密碼; the 公司 box fills only after
/// that, and when it stays empty the user's way is: clear both, 帳號 → Enter → 密碼 → Enter (ERP
/// answers 「無法登入系統，可能是密碼或帳號錯誤！」), then 帳號 and 密碼 again, and the 公司 list
/// appears. Idle: the 建立作業 forms (INVI02／COPI01／PURI01) and ERP itself are closed after the
/// configured minutes without a query. Only window messages are used for closing (no clicks), and a
/// form or ERP asking something on close is left open and logged.
/// </summary>
internal sealed partial class ItemQueryService
{
    private const int ErpStartWaitMs = 60000, ErpLoginWaitMs = 120000;
    private const nint EsPassword = 0x0020;

    /// <summary>ERP 登入設定; null or incomplete means ERP is not started automatically.</summary>
    public ErpLoginSettings? Login { get; set; }

    /// <summary>
    /// CY Web's ERP 暫停 (user, 2026-10-27): until then ERP is not opened automatically, so the
    /// licence stays free for a colleague. Set from every Agent claim.
    /// </summary>
    public DateTimeOffset? HoldUntil { get; set; }

    /// <summary>
    /// Starts ERP and logs in when it is not running (or shows its login window), then waits for
    /// the main window. Returns false when automatic login is not set up.
    /// </summary>
    private async Task<bool> StartAndLoginAsync(CancellationToken cancellationToken)
    {
        if (Login is not { CanAutoLogin: true } login) return false;
        if (HoldUntil is { } hold && hold > DateTimeOffset.Now)
            throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, $"ERP 已由 CY Web 暫停，{hold.ToLocalTime():HH:mm} 前不會自動開啟。");
        var exe = login.ErpPath;
        if (!File.Exists(exe))
            throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, $"ERP 程式不存在：{exe}\n請在「ERP 登入」設定重新選擇。");

        var window = FindLoginWindow(exe);
        if (window == 0)
        {
            _log.Info("erp-login", $"starting ERP file={Path.GetFileName(exe)}");
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = true });
            if (!await WaitUntilAsync(() => (window = FindLoginWindow(exe)) != 0 || Win32Automation.FindErpMainWindows().Count > 0,
                    ErpStartWaitMs, cancellationToken))
                throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, "已啟動 ERP，但 60 秒內沒有出現登入畫面。");
            if (window == 0) return true; // ERP logged itself in
            await Pace.Delay(800, cancellationToken); // let the login form finish building
        }
        await LoginAsync(window, login, cancellationToken);
        return true;
    }

    /// <summary>
    /// The login form: a visible top-level window of the ERP program's process holding at least
    /// two edit boxes and a combo box (帳號、密碼、公司; the language combo is above them).
    /// </summary>
    private static nint FindLoginWindow(string exe)
    {
        var name = Path.GetFileNameWithoutExtension(exe);
        var pids = Process.GetProcessesByName(name).Select(p => (uint)p.Id).ToHashSet();
        if (pids.Count == 0) return 0;
        nint found = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) && pids.Contains(Win32Automation.ProcessId(hwnd)) && LoginFields(hwnd) is not null)
            {
                found = hwnd;
                return false;
            }
            return true;
        }, 0);
        return found;
    }

    private sealed record LoginBoxes(WindowControl Account, WindowControl Password, WindowControl Company);

    private static LoginBoxes? LoginFields(nint window)
    {
        var inputs = Win32Automation.InputControls(window, visibleOnly: true).Where(c => !Invi02Layout.IsCheckBox(c.ClassName)).ToList();
        var edits = inputs.Where(c => !Invi02Layout.IsCombo(c.ClassName)).OrderBy(c => c.Rect.Top).ToList();
        if (edits.Count < 2) return null;
        // Only the login form: its second box is a password box (ES_PASSWORD on it or its inner
        // edit), so the account and password are never typed into an ERP data form.
        bool IsPassword(nint h) => (NativeMethods.GetWindowLongPtr(h, NativeMethods.GWL_STYLE) & EsPassword) != 0;
        var second = edits[1];
        if (!IsPassword(second.Handle) && !Win32Automation.EnumerateChildren(second.Handle).Any(c => IsPassword(c.Handle))) return null;
        var company = inputs.Where(c => Invi02Layout.IsCombo(c.ClassName) && c.Rect.Top > edits[1].Rect.Top).OrderBy(c => c.Rect.Top).FirstOrDefault();
        return company is null ? null : new LoginBoxes(edits[0], edits[1], company);
    }

    private async Task LoginAsync(nint window, ErpLoginSettings login, CancellationToken cancellationToken)
    {
        var boxes = LoginFields(window) ?? throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "ERP 登入畫面找不到帳號、密碼、公司欄位。");
        _log.Info("erp-login", $"login form class={NativeMethods.ClassName(window)} account={boxes.Account.ClassName} password={boxes.Password.ClassName} company={boxes.Company.ClassName}");
        if (!Win32Automation.PrepareForeground(window, _log))
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "無法把 ERP 登入畫面帶到前景。");
        var account = login.Account.Trim();
        var password = login.ReadPassword();

        await TypeLoginAsync(window, boxes, account, password, enterAfterPassword: false, cancellationToken);
        if (!await WaitUntilAsync(() => CompanyListed(boxes.Company), 2000, cancellationToken))
        {
            // The user's way when 公司 stays empty: a failed login first, then the list appears.
            _log.Info("erp-login", "company list empty; failing once to load it");
            await TypeLoginAsync(window, boxes, account, password, enterAfterPassword: true, cancellationToken);
            await Pace.Delay(2000, cancellationToken);
            boxes = LoginFields(window) ?? boxes;
            await TypeLoginAsync(window, boxes, account, password, enterAfterPassword: false, cancellationToken);
            if (!await WaitUntilAsync(() => CompanyListed(boxes.Company), 4000, cancellationToken))
                throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, "ERP 登入畫面的公司清單沒有出現，請確認帳號密碼（「ERP 登入」設定）。");
        }
        await SelectCompanyAsync(window, boxes.Company, login.Company.Trim(), cancellationToken);

        if (FindButton(window, c => c.Replace(" ", string.Empty).Contains("登入", StringComparison.Ordinal)) is { } button)
        {
            NativeMethods.PostMessage(button.Handle, NativeMethods.BM_CLICK, 0, 0);
            _log.Info("erp-login", "登入 clicked");
        }
        else
        {
            await ClickUntilFocusedAsync(window, boxes.Password, Win32Automation.Center(boxes.Password.Rect), "ERP 登入的密碼", cancellationToken);
            InputSender.Press(NativeMethods.VK_RETURN);
            _log.Info("erp-login", "登入 button not a window; Enter pressed in 密碼");
        }

        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < ErpLoginWaitMs)
        {
            await Pace.Delay(500, cancellationToken);
            if (await FindSeatsFullBoxAsync() is var full && full != 0)
            {
                // ERP closes itself once the box is answered (user, 2026-10-27).
                _log.Warn("erp-login", "ERP 人數已滿");
                await DismissMessageAsync(full, cancellationToken);
                throw new QueryFailure(ErpAgentErrorCode.ERP_SEATS_FULL, "ERP 人數已滿，請稍後再查詢。") { FromErpMessage = true };
            }
            if (Win32Automation.FindErpMainWindows().Count > 0)
            {
                _log.Info("erp-login", $"logged in after_ms={Environment.TickCount64 - started}");
                await Pace.Delay(1500, cancellationToken); // let the main form finish its Ribbon
                return;
            }
            if (Environment.TickCount64 - started > 15000 && NativeMethods.IsWindow(window) && NativeMethods.IsWindowVisible(window))
            {
                LogChildren(window, "erp-login");
                throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, "ERP 登入失敗（仍停在登入畫面），請確認「ERP 登入」設定的帳號、密碼與公司。");
            }
        }
        throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, "ERP 登入後 2 分鐘內沒有出現主視窗。");
    }

    /// <summary>帳號 → Enter → 密碼 (→ Enter). Each box is emptied first; the password is typed, never pasted.</summary>
    private async Task TypeLoginAsync(nint window, LoginBoxes boxes, string account, string password, bool enterAfterPassword, CancellationToken cancellationToken)
    {
        await ReplaceTextAsync(window, boxes.Account, account, "ERP 登入的帳號", cancellationToken);
        InputSender.Press(NativeMethods.VK_RETURN);
        await Pace.Delay(400, cancellationToken);
        await ReplaceTextAsync(window, boxes.Password, password, "ERP 登入的密碼", cancellationToken);
        if (enterAfterPassword) InputSender.Press(NativeMethods.VK_RETURN);
        await Pace.Delay(400, cancellationToken);
    }

    private async Task ReplaceTextAsync(nint window, WindowControl box, string text, string what, CancellationToken cancellationToken)
    {
        var focus = await ClickUntilFocusedAsync(window, box, Win32Automation.Center(box.Rect), what, cancellationToken);
        if (NativeMethods.CloseImeFor(focus)) await Pace.Delay(60, cancellationToken);
        NativeMethods.SendMessage(focus, NativeMethods.EM_SETSEL, 0, -1);
        InputSender.Press(NativeMethods.VK_BACK);
        await Pace.Delay(60, cancellationToken);
        InputSender.UnicodeText(text);
        await Pace.Delay(120, cancellationToken);
    }

    private static bool CompanyListed(WindowControl company) =>
        (NativeMethods.QueryNumber(company.Handle, NativeMethods.CB_GETCOUNT) ?? 0) > 0 ||
        (NativeMethods.ControlText(company.Handle) ?? string.Empty).Trim().Length > 0;

    /// <summary>The 公司 box: kept when it already shows the company, else stepped through with the arrow keys.</summary>
    private async Task SelectCompanyAsync(nint window, WindowControl company, string target, CancellationToken cancellationToken)
    {
        string Shown() => (NativeMethods.ControlText(company.Handle) ?? string.Empty).Trim();
        if (Shown() == target) return;
        await ClickUntilFocusedAsync(window, company, Win32Automation.Center(company.Rect), "ERP 登入的公司", cancellationToken);
        InputSender.Press(NativeMethods.VK_HOME);
        await Pace.Delay(150, cancellationToken);
        var seen = new HashSet<string>();
        for (var i = 0; i < 40 && Shown() != target; i++)
        {
            if (!seen.Add(Shown()) && i > 0) break; // came round without finding it
            InputSender.Press(NativeMethods.VK_DOWN);
            await Pace.Delay(150, cancellationToken);
        }
        if (Shown() != target)
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"ERP 登入畫面的公司清單沒有「{target}」（目前「{Shown()}」），請確認「ERP 登入」設定。");
        // A list left open would take the next Enter or click.
        if ((NativeMethods.QueryNumber(company.Handle, NativeMethods.CB_GETDROPPEDSTATE) ?? 0) != 0) InputSender.Press(NativeMethods.VK_RETURN);
        _log.Info("erp-login", "company selected");
    }

    // ---- unresponsive ERP --------------------------------------------------------------

    private bool CanRestartErp => Login is { CanAutoLogin: true };

    /// <summary>A window that answers a harmless message within 5 seconds.</summary>
    private static bool Responds(nint hwnd) => NativeMethods.QueryNumber(hwnd, 0 /* WM_NULL */, 5000) is not null;

    /// <summary>
    /// Ends the processes that own an ERP main or 作業 window in this Windows session (see
    /// <see cref="ErpRestartTargets"/>), then starts ERP and logs in again. Each is first sent WM_CLOSE
    /// on its ERP windows and only killed when it has not exited 5 seconds later; never its process
    /// tree, never a process found by name (user, 2026-10-26: ERP server services must not be hit).
    /// </summary>
    private async Task RestartErpAsync(string why, CancellationToken cancellationToken)
    {
        var (targets, _) = await EndErpWindowOwnersAsync(why);
        if (targets == 0)
            throw new QueryFailure(ErpAgentErrorCode.ERP_STATE, $"ERP {why}，但這個 Windows 工作階段找不到 ERP 視窗，未強制關閉，請人工檢查 ERP。");
        await Pace.Delay(2000, cancellationToken);
        if (!await StartAndLoginAsync(cancellationToken) || Win32Automation.FindErpMainWindows().Count == 0)
            throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, $"ERP {why}，已強制關閉並重新開啟，但沒有回到主畫面。");
        _log.Info("erp-login", "ERP restarted");
    }

    /// <summary>
    /// Ends the processes that own an ERP main or 作業 window in this Windows session (see
    /// <see cref="ErpRestartTargets"/>): WM_CLOSE on their ERP windows first, killed when still
    /// running 5 seconds later; never a process tree. Returns how many there were and how many are left.
    /// </summary>
    private async Task<(int Targets, int Left)> EndErpWindowOwnersAsync(string why)
    {
        int session;
        using (var self = Process.GetCurrentProcess())
            session = self.SessionId;
        var windows = ErpWindows();
        foreach (var (owner, _) in windows.DistinctBy(w => (w.Owner.Pid, w.Owner.OwnsMainWindow)))
            _log.Info("erp-login", $"ERP window owner pid={owner.Pid} name={ProcessNameOf(owner.Pid)} session={owner.SessionId?.ToString() ?? "?"} main={owner.OwnsMainWindow}");
        var targets = ErpRestartTargets.Select(windows.Select(w => w.Owner), session, Environment.ProcessId);
        _log.Warn("erp-login", $"ending ERP processes: {why} targets={targets.Count} session={session}");
        if (targets.Count == 0) return (0, 0);

        foreach (var pid in targets)
        {
            Process process;
            try { process = Process.GetProcessById(pid); }
            catch (ArgumentException) { continue; } // already gone
            using (process)
            {
                try
                {
                    // Recheck: same session, and the PID has not been reused since the windows were read.
                    if (process.HasExited || process.SessionId != session ||
                        !ErpWindows().Any(w => w.Owner.Pid == pid))
                        continue;
                    foreach (var (_, hwnd) in windows.Where(w => w.Owner.Pid == pid))
                        NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, 0, 0);
                    if (process.WaitForExit(5000))
                    {
                        _log.Info("erp-login", $"ERP process closed pid={pid}");
                        continue;
                    }
                    process.Kill();
                    var gone = process.WaitForExit(10000);
                    _log.Warn("erp-login", $"ERP process killed pid={pid} exited={gone}");
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    _log.Warn("erp-login", $"ending ERP process failed pid={pid}: {ex.GetType().Name}");
                }
            }
        }
        var left = targets.Count(pid => { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } });
        return (targets.Count, left);
    }

    /// <summary>
    /// CY Web's 強制關閉 ERP (user, 2026-10-27: after a failed query the member decides). Returns
    /// NOT_OPEN, CLOSED, or FAILED when some ERP process would not end.
    /// </summary>
    public async Task<string> ForceCloseErpAsync()
    {
        using var gate = ErpAutomationGate.Enter();
        var (targets, left) = await EndErpWindowOwnersAsync("由 CY Web 強制關閉");
        return targets == 0 ? ErpAgentControl.NotOpen : left == 0 ? ErpAgentControl.Closed : ErpAgentControl.Failed;
    }

    /// <summary>ERP's main window (also when minimized) and the 作業 windows this program uses, with their owners.</summary>
    private static List<(ErpWindowOwner Owner, nint Hwnd)> ErpWindows()
    {
        var found = new List<(ErpWindowOwner, nint)>();
        void Add(nint hwnd, bool main)
        {
            var pid = (int)Win32Automation.ProcessId(hwnd);
            if (pid == 0) return;
            int? session = null;
            try
            {
                using var process = Process.GetProcessById(pid);
                session = process.SessionId;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            found.Add((new ErpWindowOwner(pid, session, main), hwnd));
        }
        foreach (var hwnd in Win32Automation.FindErpMainWindows()) Add(hwnd, main: true);
        if (Win32Automation.FindMinimizedErp() is { } minimized)
        {
            Add(minimized.Main, main: true);
            Add(minimized.Application, main: true);
        }
        foreach (var form in KeptForms)
            foreach (var hwnd in Win32Automation.FindFormWindows(form)) Add(hwnd, main: false);
        return found;
    }

    private static string ProcessNameOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "?";
        }
    }

    // ---- idle ------------------------------------------------------------------------

    private static readonly ErpForm[] KeptForms = [ErpForm.Invi02, ErpForm.Copi01, ErpForm.Puri01];

    /// <summary>Whether any 建立作業 form this program keeps open is open.</summary>
    public static bool AnyKeptFormOpen() => KeptForms.Any(f => Win32Automation.FindFormWindows(f).Count > 0);

    public static bool ErpRunning() => Win32Automation.FindErpMainWindows().Count > 0 || Win32Automation.FindMinimizedErp() is not null;

    /// <summary>
    /// Closes INVI02／COPI01／PURI01 (WM_CLOSE, no clicks). A form that asks something instead of
    /// closing (it was being edited) has the question cancelled and stays open. Returns how many closed.
    /// </summary>
    public async Task<int> CloseKeptFormsAsync()
    {
        using var gate = ErpAutomationGate.Enter();
        var closed = 0;
        foreach (var form in KeptForms)
            foreach (var hwnd in Win32Automation.FindFormWindows(form))
                if (await CloseWindowAsync(hwnd, form.Title)) closed++;
        _log.Info("idle", $"forms closed={closed}");
        return closed;
    }

    /// <summary>
    /// Closes ERP: the kept forms, then the main window. ERP's 「確定離開？」-style question is
    /// answered with 是／確定 (only a box with that button); anything else is logged and ERP stays.
    /// </summary>
    public async Task<bool> CloseErpAsync()
    {
        using var gate = ErpAutomationGate.Enter();
        foreach (var form in KeptForms)
            foreach (var hwnd in Win32Automation.FindFormWindows(form))
                await CloseWindowAsync(hwnd, form.Title);
        var mains = Win32Automation.FindErpMainWindows();
        if (mains.Count == 0) return !ErpRunning();
        var main = mains[0];
        var pid = Win32Automation.ProcessId(main);
        _log.Info("idle", $"closing ERP main class={NativeMethods.ClassName(main)}");
        NativeMethods.PostMessage(main, NativeMethods.WM_CLOSE, 0, 0);
        var deadline = Environment.TickCount64 + 20000;
        var answered = false;
        while (Environment.TickCount64 < deadline)
        {
            await Task.Delay(300);
            if (!ErpRunning()) break;
            var box = FindProcessMessageBox(pid);
            if (box != 0 && !answered)
            {
                var text = await ReadErpMessageAsync(box);
                var yes = FindButton(box, c => c.StartsWith("是", StringComparison.Ordinal) || c.StartsWith("確定", StringComparison.Ordinal) ||
                    c.Equals("YES", StringComparison.OrdinalIgnoreCase) || c.Equals("&YES", StringComparison.OrdinalIgnoreCase) || c.Equals("OK", StringComparison.OrdinalIgnoreCase));
                _log.Info("idle", $"ERP asked on close text={_log.Value(text)} yes_button={yes is not null}");
                LogChildren(box, "idle-close");
                if (yes is null) break;
                NativeMethods.PostMessage(yes.Handle, NativeMethods.BM_CLICK, 0, 0);
                answered = true;
            }
        }
        var gone = !ErpRunning();
        _log.Info("idle", $"ERP closed={gone}");
        return gone;
    }

    private async Task<bool> CloseWindowAsync(nint hwnd, string title)
    {
        var pid = Win32Automation.ProcessId(hwnd);
        NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, 0, 0);
        var deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline && NativeMethods.IsWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd))
        {
            await Task.Delay(200);
            if (FindProcessMessageBox(pid) is var box && box != 0)
            {
                _log.Warn("idle", $"{title} asked on close; cancelled, form kept text={_log.Value(await ReadErpMessageAsync(box))}");
                NativeMethods.PostMessage(box, NativeMethods.WM_CLOSE, 0, 0);
                return false;
            }
        }
        var closed = !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd);
        _log.Info("idle", $"{title} closed={closed}");
        return closed;
    }

    /// <summary>
    /// A visible message box of another program saying 「人數已滿」. It comes from ERP's
    /// 「Leader作業元件」, not necessarily from the MainMenu process, so any box is looked at;
    /// only one titled Leader… or already showing the text is read (OCR when it has no text yet).
    /// </summary>
    private async Task<nint> FindSeatsFullBoxAsync()
    {
        var boxes = new List<nint>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) && Win32Automation.ProcessId(hwnd) != (uint)Environment.ProcessId &&
                Invi02Layout.IsMessageDialogClass(NativeMethods.ClassName(hwnd)))
                boxes.Add(hwnd);
            return true;
        }, 0);
        foreach (var box in boxes)
        {
            var title = NativeMethods.WindowText(box);
            var shown = Win32Automation.EnumerateChildren(box).Any(c => c.Visible && Invi02Layout.IsSeatsFullMessage(c.Text));
            if (!shown && !title.Contains("Leader", StringComparison.OrdinalIgnoreCase)) continue;
            if (shown || Invi02Layout.IsSeatsFullMessage(await ReadErpMessageAsync(box))) return box;
        }
        return 0;
    }

    private static nint FindProcessMessageBox(uint pid)
    {
        nint found = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) && Win32Automation.ProcessId(hwnd) == pid &&
                Invi02Layout.IsMessageDialogClass(NativeMethods.ClassName(hwnd)) && FindButton(hwnd, _ => true) is not null)
            {
                found = hwnd;
                return false;
            }
            return true;
        }, 0);
        return found;
    }
}
