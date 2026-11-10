using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed partial class MainForm
{
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 30_000 };
    private readonly CancellationTokenSource updateLifetime = new();
    private readonly UpdateBanner updateBanner = new();
    private DateTime nextUpdateCheck = DateTime.Now.AddSeconds(20);
    private DateTime updateNotBefore = DateTime.MinValue;
    private (string Folder, Version Version)? updateReady;
    private bool checkingUpdate;
    private bool updateChecksStarted;
    private bool applyingUpdate;

    private void StartUpdateChecks()
    {
        if (updateChecksStarted) return;
        updateChecksStarted = true;
        var updater = new AppUpdater();
        Controls.Add(updateBanner);
        updateBanner.BringToFront();
        if (updater.TakeOutcome() is { } outcome)
        {
            if (outcome.Ok && outcome.Version == ApplicationVersion.Read())
            {
                updateBanner.Show(UpdateBannerKind.Success, $"CYInvoice 已更新到 V{outcome.Version}。", TimeSpan.FromSeconds(12));
            }
            else
            {
                nextUpdateCheck = DateTime.Now.AddHours(8);
                updateBanner.Show(UpdateBannerKind.Failure,
                    $"自動更新到 V{outcome.Version} 未完成；目前仍是 V{ApplicationVersion.Read()}。可至 GitHub Release 手動安裝。");
            }
        }
        updateTimer.Tick += async (_, _) => await CheckForUpdateAsync(updater);
        updateTimer.Start();
        FormClosing += (_, eventArgs) =>
        {
            updateTimer.Stop();
            updateLifetime.Cancel();
            // The main handler already refused closing if AutoInput is processing.
            if (eventArgs.Cancel || applyingUpdate || checkingUpdate || updateReady is not { } next ||
                !CanUpdateSafely(allowClosing: true))
                return;
            TryStartUpdate(updater, next, restart: false);
        };
    }

    private bool CanUpdateSafely(bool allowClosing = false, int allowedWindows = 1)
    {
        if ((!allowClosing && shuttingDown) || IsDisposed || !IsHandleCreated ||
            autoInputPipe.IsProcessing || invoicePage.IsManualIssueInProgress ||
            service.HasActiveInvoiceOperation || syncCoordinator.IsRunning ||
            checkingConnections || connectionBlocker is not null ||
            LocalResetCoordinator.HasPendingReset(AppContext.BaseDirectory) ||
            LocalResetCoordinator.IsRevokedDeviceResetPending(AppContext.BaseDirectory))
            return false;
        return Application.OpenForms.Cast<Form>().Count(form => form.Visible) == allowedWindows;
    }

    private async Task CheckForUpdateAsync(AppUpdater updater)
    {
        if (checkingUpdate || applyingUpdate || shuttingDown || IsDisposed) return;
        if (updateReady is null)
        {
            if (DateTime.Now < nextUpdateCheck) return;
            nextUpdateCheck = DateTime.Now.AddHours(4);
            checkingUpdate = true;
            try
            {
                var ready = await updater.PrepareAsync(updateLifetime.Token, offer =>
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke((Action)(() =>
                        {
                            if (!IsDisposed)
                                updateBanner.Show(UpdateBannerKind.Progress,
                                    $"發現 CYInvoice 新版 V{offer.Version.ToString(3)}，正在下載及核對 SHA-256…");
                        }));
                });
                if (ready is { } found && !updateLifetime.IsCancellationRequested && !IsDisposed)
                {
                    updateReady = (found.Folder, found.Offer.Version);
                    updateBanner.Show(UpdateBannerKind.Progress,
                        $"CYInvoice V{found.Offer.Version.ToString(3)} 已下載並驗證，閒置後將更新。");
                }
            }
            catch (OperationCanceledException) when (updateLifetime.IsCancellationRequested) { }
            catch (Exception error)
            {
                IssueDiagnosticLog.RecordProblem(error, "CYInvoiceAutoUpdate");
                if (!IsDisposed)
                    updateBanner.Show(UpdateBannerKind.Failure,
                        "CYInvoice 自動更新檢查失敗；目前版本不受影響，稍後會重新檢查。",
                        TimeSpan.FromSeconds(12));
            }
            finally { checkingUpdate = false; }
        }
        if (updateReady is not { } next || DateTime.Now < updateNotBefore ||
            !CanUpdateSafely()) return;

        applyingUpdate = true;
        try
        {
            using var confirmation = new UpdateCountdownForm(
                next.Version.ToString(3), 10, () => !CanUpdateSafely(allowedWindows: 2));
            var decision = confirmation.ShowDialog(this);
            if (decision == DialogResult.Ignore)
            {
                updateNotBefore = DateTime.Now.AddMinutes(30);
                updateBanner.Show(UpdateBannerKind.Progress,
                    $"已延後更新，{updateNotBefore:HH:mm} 後閒置時自動更新。");
                return;
            }
            if (decision == DialogResult.OK && CanUpdateSafely() &&
                TryStartUpdate(updater, next, restart: true))
            {
                updateBanner.Show(UpdateBannerKind.Progress, "正在更新 CYInvoice，程式將自動重新開啟。");
                Close();
                return;
            }
        }
        finally { applyingUpdate = false; }
    }

    private bool TryStartUpdate(AppUpdater updater, (string Folder, Version Version) next, bool restart)
    {
        try
        {
            updater.ApplyAfterExit(next.Folder, next.Version, restart);
            return true;
        }
        catch (Exception error)
        {
            IssueDiagnosticLog.RecordProblem(error, "CYInvoiceAutoUpdate");
            updateReady = null;
            if (!IsDisposed)
                updateBanner.Show(UpdateBannerKind.Failure,
                    "更新程序未能啟動，原版程式保持不變。稍後會重新檢查。", TimeSpan.FromSeconds(15));
            return false;
        }
    }

    private void StopUpdateChecks()
    {
        updateTimer.Stop();
        if (!updateLifetime.IsCancellationRequested) updateLifetime.Cancel();
        updateTimer.Dispose();
        updateLifetime.Dispose();
        updateBanner.Dispose();
    }
}
