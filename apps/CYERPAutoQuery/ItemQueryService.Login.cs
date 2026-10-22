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

    // Only this application's own Process.Start can grant ownership. Existing/manual ERP
    // sessions and similarly named processes must never be terminated automatically.
    private ErpManagedProcessIdentity? _managedErp;

    private ErpManagedProcessIdentity? CaptureStartedErp(Process? started, string configuredPath)
    {
        try
        {
            if (started is null || started.HasExited) return null;
            using var caller = Process.GetCurrentProcess();
            var actual = started.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(actual) ||
                !string.Equals(Path.GetFullPath(actual), Path.GetFullPath(configuredPath), StringComparison.OrdinalIgnoreCase) ||
                started.SessionId != caller.SessionId) return null;
            return new ErpManagedProcessIdentity(started.Id, started.StartTime.ToUniversalTime(), started.SessionId, Path.GetFullPath(actual));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or ArgumentException or IOException)
        {
            _log.Warn("erp-login", $"could not identify started ERP: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>
    /// Returns an open handle to the exact process started by this instance, if its identity still
    /// matches. Callers must dispose it. Never use GetProcessesByName() to terminate ERP.
    /// </summary>
    private Process? OpenOwnedErp()
    {
        if (_managedErp is not { } identity || Login is null) return null;
        Process? process = null;
        try
        {
            if (!string.Equals(identity.ExecutablePath, Path.GetFullPath(Login.ErpPath), StringComparison.OrdinalIgnoreCase))
                return null;
            using var caller = Process.GetCurrentProcess();
            process = Process.GetProcessById(identity.Pid);
            _ = process.Handle; // Acquire the specific process handle before rechecking identity.
            if (process.HasExited ||
                !identity.Matches(process.Id, process.StartTime.ToUniversalTime(), process.SessionId,
                    process.MainModule?.FileName ?? string.Empty, caller.SessionId))
            {
                process.Dispose();
                return null;
            }
            return process;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or ArgumentException or IOException)
        {
            process?.Dispose();
            _log.Warn("erp-login", $"cannot verify ERP ownership: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>
    /// Starts ERP and logs in when it is not running (or shows its login window), then waits for
    /// the main window. Returns false when automatic login is not set up.
    /// </summary>
    private async Task<bool> StartAndLoginAsync(CancellationToken cancellationToken)
    {
        if (Login is not { CanAutoLogin: true } login) return false;
        var exe = login.ErpPath;
        if (!File.Exists(exe))
            throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, $"ERP 程式不存在：{exe}\n請在「ERP 登入」設定重新選擇。");

        var window = FindLoginWindow(exe);
        if (window == 0)
        {
            _log.Info("erp-login", $"starting ERP file={Path.GetFileName(exe)}");
            using var started = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = true });
            _managedErp = CaptureStartedErp(started, exe);
            if (_managedErp is null)
                _log.Warn("erp-login", "ERP started but ownership could not be verified; automatic force-restart and idle-close are disabled for this instance");
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

    /// <summary>Force-closes every process of the ERP program, then starts ERP and logs in again.</summary>
    private async Task RestartErpAsync(string why, CancellationToken cancellationToken)
    {
        var name = Path.GetFileNameWithoutExtension(Login!.ErpPath);
        var processes = Process.GetProcessesByName(name);
        _log.Warn("erp-login", $"restarting ERP: {why} processes={processes.Length}");
        foreach (var process in processes)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _log.Warn("erp-login", $"kill failed: {ex.GetType().Name}");
            }
            finally
            {
                process.Dispose();
            }
        }
        await Pace.Delay(2000, cancellationToken);
        if (!await StartAndLoginAsync(cancellationToken) || Win32Automation.FindErpMainWindows().Count == 0)
            throw new QueryFailure(ErpAgentErrorCode.ERP_NOT_OPEN, $"ERP {why}，已強制關閉並重新開啟，但沒有回到主畫面。");
        _log.Info("erp-login", "ERP restarted");
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
