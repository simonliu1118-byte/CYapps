using CYInvoice.Core.Imports;
using CYInvoice.Core.Imports.Coupang;
using CYInvoice.Core.Imports.Digiwin;
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

    public static async Task<IReadOnlyList<CoupangOrder>> ReadCoupangExportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        IReadOnlyList<IReadOnlyList<string>> rows;
        if (extension == ".xls")
        {
            rows = await ExcelComRows.ReadFirstWorksheetAsync(
                filePath, string.Empty, cancellationToken, "酷澎").ConfigureAwait(false);
        }
        else if (extension is ".xlsx" or ".xlsm")
        {
            rows = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return XlsxRows.ReadFirstWorksheet(filePath);
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new InvalidDataException("酷澎匯入只支援 .xls、.xlsx、.xlsm Excel 檔案");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return CoupangImporter.ParseRows(rows);
    }

    public static async Task<DigiwinOrder> ReadDigiwinExportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("鼎新 ERP 標準匯入只支援 .xlsx Excel 檔案");

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var head = XlsxRows.ReadWorksheet(filePath, "單頭資料");
            cancellationToken.ThrowIfCancellationRequested();
            var detail = XlsxRows.ReadWorksheet(filePath, "單身資料");
            cancellationToken.ThrowIfCancellationRequested();
            return DigiwinImporter.ParseRows(head, detail);
        }, cancellationToken).ConfigureAwait(false);
    }
}
