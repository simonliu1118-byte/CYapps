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
}
