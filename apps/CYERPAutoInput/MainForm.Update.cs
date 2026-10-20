namespace CYERPAutoInput;

/// <summary>
/// Updating itself from GitHub Releases (user, 2026-10-17: look for a newer Release and update
/// without asking; Actions artifacts do not count). Checked shortly after start and every four
/// hours; a downloaded update is put in place and the program restarted only while nothing runs
/// (no automation, batch, paper invoice or other window), otherwise when the program is closed.
/// It shows (user, 2026-10-20): a banner while it downloads and waits, a 10-second countdown
/// (「稍後再更新」 waits 30 minutes), the script's 「正在更新」 window, and after the restart
/// 「已更新」 or, when the files could not be replaced, 「更新失敗」 with the old version running
/// and another try in 8 hours.
/// </summary>
internal sealed partial class MainForm
{
    private const string ProgramName = "CYERPAutoInput";
    private const int UpdateCountdownSeconds = 10;
    private static readonly TimeSpan UpdateLater = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan UpdateRetryAfterFailure = TimeSpan.FromHours(8);

    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 30_000 };
    private readonly UpdateBanner _updateBanner = new();
    private DateTime _nextUpdateCheck = DateTime.Now.AddSeconds(20);
    private DateTime _updateNotBefore = DateTime.MinValue;
    private (string Folder, Version Version)? _updateReady;
    private bool _updating;

    private void StartUpdateChecks()
    {
        if (UiSnapshot.Active) return;
        var updater = new AppUpdater(_log);
        var fill = Controls.Cast<Control>().FirstOrDefault(c => c.Dock == DockStyle.Fill);
        Controls.Add(_updateBanner);
        fill?.BringToFront();
        ShowUpdateOutcome(updater.TakeOutcome());
        _updateTimer.Tick += async (_, _) => await UpdateTickAsync(updater);
        _updateTimer.Start();
        FormClosing += (_, _) =>
        {
            _updateTimer.Stop();
            if (_updateReady is { } ready && !_updating) TryApply(updater, ready, restart: false);
        };
    }

    private void ShowUpdateOutcome(UpdateOutcome? outcome)
    {
        if (outcome is null) return;
        if (outcome.Ok && outcome.Version == AppVersionInfo.Version)
        {
            _log.Info("update", $"updated to V{outcome.Version}");
            _updateBanner.Show(UpdateBannerKind.Success, $"已更新到 V{outcome.Version}。", TimeSpan.FromSeconds(10));
            return;
        }
        _log.Warn("update", $"update to V{outcome.Version} failed: {outcome.Detail}");
        _nextUpdateCheck = DateTime.Now + UpdateRetryAfterFailure;
        _updateBanner.Show(UpdateBannerKind.Failure,
            $"更新到 V{outcome.Version} 失敗，目前仍是 V{AppVersionInfo.Version}；8 小時後會再試，也可以手動更新（{UpdateOutcome.ReleasesPage} 下載 zip 解壓覆蓋）。");
    }

    private bool IdleForUpdate(int windows = 1) =>
        _automationCts is null && !_shopeeBatchRunning && _paperHold == 0 && _paperJobs.Count == 0 &&
        Application.OpenForms.Cast<Form>().Count(f => f.Visible) == windows && _automationSession?.Active != true;

    private async Task UpdateTickAsync(AppUpdater updater)
    {
        if (_updating) return;
        if (_updateReady is null)
        {
            if (DateTime.Now < _nextUpdateCheck) return;
            _nextUpdateCheck = DateTime.Now.AddHours(4);
            _updating = true;
            try
            {
                if (await updater.PrepareAsync(CancellationToken.None, offer =>
                        BeginInvoke(() => _updateBanner.Show(UpdateBannerKind.Progress, $"發現新版 V{offer.Version.ToString(3)}，下載中…"))) is { } ready)
                {
                    _updateReady = (ready.Folder, ready.Offer.Version);
                    _log.Info("update", $"V{ready.Offer.Version} ready; applied when idle");
                    _updateBanner.Show(UpdateBannerKind.Progress, $"新版 V{ready.Offer.Version.ToString(3)} 已下載，程式閒置時會自動更新。");
                }
            }
            catch (Exception ex) // an update must never stop the program; the next check tries again
            {
                _log.Warn("update", $"update check failed: {ex.GetType().Name}: {ex.Message}");
                _updateBanner.HideBanner();
            }
            finally
            {
                _updating = false;
            }
        }
        if (_updateReady is not { } update || DateTime.Now < _updateNotBefore || !IdleForUpdate()) return;

        _updating = true;
        DialogResult answer;
        using (var countdown = new UpdateCountdownForm(ProgramName, update.Version.ToString(3), UpdateCountdownSeconds, () => !IdleForUpdate(windows: 2)))
            answer = countdown.ShowDialog(this);
        _log.Info("update", $"countdown answer={answer}");
        if (answer == DialogResult.Ignore)
        {
            _updateNotBefore = DateTime.Now + UpdateLater;
            _updateBanner.Show(UpdateBannerKind.Progress, $"新版 V{update.Version.ToString(3)} 已延後，{_updateNotBefore:HH:mm} 後程式閒置時會自動更新。");
        }
        if (answer == DialogResult.OK && IdleForUpdate() && TryApply(updater, update, restart: true))
        {
            SetStatus("正在更新到新版，程式會自動重新開啟…");
            Close();
            return;
        }
        _updating = false;
    }

    private bool TryApply(AppUpdater updater, (string Folder, Version Version) update, bool restart)
    {
        try
        {
            updater.ApplyAfterExit(update.Folder, update.Version, restart);
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn("update", $"update not applied: {ex.GetType().Name}: {ex.Message}");
            _updateReady = null;
            _updateBanner.HideBanner();
            return false;
        }
    }
}
