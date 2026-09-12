using CYInvoice.Core.Imports;
using CYInvoice.Core.Imports.Coupang;
using CYInvoice.Core.Imports.Mo;

namespace CYInvoice.WinForms;

internal static class PlatformImportReader
{
    public static async Task<IReadOnlyList<MoOrder>> ReadMoOrderExportAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default)
    {
        var rows = await ExcelComRows.ReadFirstWorksheetAsync(filePath, password, cancellationToken)
            .ConfigureAwait(false);
        return MoImporter.ParseRows(rows);
    }

    public static Task<IReadOnlyList<CoupangOrder>> ReadCoupangExportAsync(
        string filePath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = XlsxRows.ReadFirstWorksheet(filePath);
            cancellationToken.ThrowIfCancellationRequested();
            return CoupangImporter.ParseRows(rows);
        }, cancellationToken);
}
