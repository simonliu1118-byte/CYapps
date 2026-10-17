namespace CYERPAutoQuery;

/// <summary>
/// Updating itself from GitHub Releases (user, 2026-10-17: the same as CYERPAutoInput; copied from
/// CYERPAutoInput MainForm.Update.cs). Checked shortly after start and every four hours; a
/// downloaded update is put in place and the program restarted only while nothing runs (no local
/// or Web query, no other window such as an 憑證 window), otherwise when the program is closed.
/// The Web 連線 setting is kept, so the Agent comes back by itself after the restart.
/// </summary>
internal sealed partial class MainForm
{
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 30_000 };
    private DateTime _nextUpdateCheck = DateTime.Now.AddSeconds(20);
    private string? _updateFolder;
    private bool _updating;

    private void StartUpdateChecks()
    {
        var updater = new AppUpdater(_log);
        _updateTimer.Tick += async (_, _) => await UpdateTickAsync(updater);
        _updateTimer.Start();
        FormClosing += (_, _) =>
        {
            _updateTimer.Stop();
            if (_updateFolder is { } folder && !_updating) TryApply(updater, folder, restart: false);
        };
    }

    private bool IdleForUpdate =>
        _queryCts is null && _automationSession?.Active != true &&
        Application.OpenForms.Cast<Form>().Count(f => f.Visible) == 1;

    private async Task UpdateTickAsync(AppUpdater updater)
    {
        if (_updating) return;
        if (_updateFolder is null)
        {
            if (DateTime.Now < _nextUpdateCheck) return;
            _nextUpdateCheck = DateTime.Now.AddHours(4);
            _updating = true;
            try
            {
                if (await updater.PrepareAsync(CancellationToken.None) is { } ready)
                {
                    _updateFolder = ready.Folder;
                    _log.Info("update", $"V{ready.Offer.Version} ready; applied when idle");
                }
            }
            catch (Exception ex) // an update must never stop the program; the next check tries again
            {
                _log.Warn("update", $"update check failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _updating = false;
            }
        }
        if (_updateFolder is { } folder && IdleForUpdate && TryApply(updater, folder, restart: true))
        {
            _updating = true;
            SetStatus("正在更新到新版，程式會自動重新開啟…");
            Close();
        }
    }

    private bool TryApply(AppUpdater updater, string folder, bool restart)
    {
        try
        {
            updater.ApplyAfterExit(folder, restart);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn("update", $"update not applied: {ex.GetType().Name}: {ex.Message}");
            _updateFolder = null;
            return false;
        }
    }
}
