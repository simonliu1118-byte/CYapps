using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal static class LocalResetApplication
{
    private static LocalResetExecutionRequest? pending;

    public static bool IsPending => pending is not null;

    public static void ScheduleRevokedDeviceRestart()
    {
        if (!LocalResetCoordinator.IsRevokedDeviceResetPending(AppContext.BaseDirectory))
            throw new InvalidOperationException("裝置清除尚未持久化。");
        Application.Exit();
    }

    public static void Schedule(LocalResetExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (pending is not null)
            throw new InvalidOperationException("CYInvoice 已排定另一個本機重設作業。");
        pending = request;
        Application.Exit();
    }

    public static bool TryTake(out LocalResetExecutionRequest? request)
    {
        request = pending;
        pending = null;
        return request is not null;
    }
}
