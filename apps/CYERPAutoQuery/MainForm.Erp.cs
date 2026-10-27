namespace CYERPAutoQuery;

/// <summary>
/// ERP 自動登入、閒置關閉與開機自動開啟 (user, 2026-10-19). The 建立作業 forms are closed after
/// the form idle minutes and ERP after the ERP idle minutes. Idle counts from the later of the last
/// query and the last keyboard／mouse use of this PC, so ERP is not closed on someone working here.
/// </summary>
internal sealed partial class MainForm
{
    private ErpLoginSettings _erpLogin = new();
    private readonly System.Windows.Forms.Timer _idleTimer = new() { Interval = 60_000 };
    private DateTime _lastErpUse = DateTime.Now;
    private bool _idleBusy;

    private void LoadErpLogin()
    {
        _erpLogin = ErpLoginSettings.Load(message => _log.Warn("erp-login", message));
        _service.Login = _erpLogin;
        try
        {
            StartupRegistration.Refresh();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _log.Warn("startup", $"run key not refreshed: {ex.GetType().Name}");
        }
    }

    private void StartIdleChecks()
    {
        _idleTimer.Tick += async (_, _) => await IdleTickAsync();
        _idleTimer.Start();
    }

    private async Task IdleTickAsync()
    {
        if (_idleBusy || _queryCts is not null || _automationSession?.Active == true) return;
        var now = DateTime.Now;
        var lastInput = now - TimeSpan.FromMilliseconds(NativeMethods.IdleMilliseconds());
        var lastUse = lastInput > _lastErpUse ? lastInput : _lastErpUse;
        _idleBusy = true;
        try
        {
            if (_erpLogin.IdleDue(lastUse, now) && ItemQueryService.ErpRunning())
            {
                _log.Info("idle", $"ERP idle minutes={(int)(now - lastUse).TotalMinutes}");
                if (await _service.CloseErpAsync()) SetStatus("ERP：閒置過久，已關閉 ERP（下次查詢會自動開啟並登入）");
            }
            else if (_erpLogin.FormsIdleDue(lastUse, now) && ItemQueryService.AnyKeptFormOpen())
            {
                _log.Info("idle", $"forms idle minutes={(int)(now - lastUse).TotalMinutes}");
                if (await _service.CloseKeptFormsAsync() > 0) SetStatus("ERP：閒置過久，已關閉品號／客戶／廠商資料建立作業");
            }
        }
        catch (QueryFailure ex) when (ex.Code == ErpAgentErrorCode.ERP_BUSY)
        {
            // Another CY program is driving ERP: try again next minute.
        }
        catch (Exception ex)
        {
            _log.Error("idle", ex);
        }
        finally
        {
            _idleBusy = false;
        }
    }

    /// <summary>
    /// CY Web's ERP 關閉 (user, 2026-10-27): CLOSE closes ERP the same way as the idle close (a
    /// question about unsaved work keeps ERP open); FORCE_CLOSE ends ERP's processes. Null when the
    /// program is busy, so CY Web's request is taken up again at the next claim.
    /// </summary>
    private async Task<string?> RunWebControlAsync(string request, CancellationToken cancellationToken)
    {
        if (_idleBusy || _queryCts is not null || _automationSession?.Active == true) return null;
        _idleBusy = true;
        try
        {
            if (request == ErpAgentControl.ForceClose)
            {
                var forced = await _service.ForceCloseErpAsync();
                _log.Info("control", $"force close result={forced}");
                SetStatus(forced == ErpAgentControl.Closed ? "ERP：CY Web 要求強制關閉，已關閉 ERP" : forced == ErpAgentControl.NotOpen ? "ERP：CY Web 要求強制關閉，ERP 沒有開著" : "ERP：CY Web 要求強制關閉，但有 ERP 程式沒有結束");
                return forced;
            }
            if (!ItemQueryService.ErpRunning() && !ItemQueryService.AnyKeptFormOpen()) return ErpAgentControl.NotOpen;
            var gone = await _service.CloseErpAsync();
            _log.Info("control", $"close result gone={gone}");
            SetStatus(gone ? "ERP：CY Web 要求關閉，已關閉 ERP" : "ERP：CY Web 要求關閉，但 ERP 有未完成的作業，沒有關閉");
            return gone ? ErpAgentControl.Closed : ErpAgentControl.KeptOpen;
        }
        catch (QueryFailure ex) when (ex.Code == ErpAgentErrorCode.ERP_BUSY)
        {
            return null; // another CY program is driving ERP: next claim
        }
        finally
        {
            _idleBusy = false;
        }
    }

    /// <summary>CY Web's ERP 暫停: ERP is not opened automatically until then.</summary>
    private void SetErpHold(DateTimeOffset? holdUntil)
    {
        if (_service.HoldUntil == holdUntil) return;
        _service.HoldUntil = holdUntil;
        _log.Info("control", $"hold until={(holdUntil is { } h ? h.ToLocalTime().ToString("HH:mm") : "-")}");
    }

    private void OpenErpLoginSettings()
    {
        bool startup;
        try { startup = StartupRegistration.IsEnabled(); } catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { startup = false; }
        using var form = new ErpLoginSettingsForm(_erpLogin, startup);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        _erpLogin.Save();
        _service.Login = _erpLogin;
        try
        {
            StartupRegistration.Set(form.StartWithWindows);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _log.Warn("startup", $"run key not written: {ex.GetType().Name}");
            MessageBox.Show(this, "無法設定開機自動開啟（Windows 拒絕寫入）。", "ERP 登入設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        _log.Info("erp-login", $"settings saved auto_login={_erpLogin.CanAutoLogin} form_idle={_erpLogin.FormIdleMinutes} erp_idle={_erpLogin.IdleCloseMinutes} startup={form.StartWithWindows}");
    }
}
