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
