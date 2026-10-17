namespace CYERPAutoQuery;

/// <summary>
/// ERP's export lands in a TEMP folder next to the program (user, 2026-10-16), one fresh file
/// per query, read and then deleted at once: the workbook holds costs and stock values and
/// must not pile up on disk.
/// </summary>
internal static class ExportFiles
{
    /// <summary>One prefix per ERP program whose export is read (INVI02, INVQ01, COPI01, PURI01, PURR18, PURR22, COPR15, COPR16, ACRR18).</summary>
    private static readonly string[] Prefixes =
        [Invi02Layout.FormCode + "_", Invq01Layout.FormCode + "_", ErpForm.Copi01.Code + "_", ErpForm.Puri01.Code + "_", PurchaseReportLayout.Report.Code + "_", PurchaseReportLayout.Returns.Code + "_", .. SalesReport.All.Select(r => r.Code + "_"), ReceivableLayout.Report.Code + "_"];

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "TEMP");

    /// <summary>A unique path that does not exist yet (ERP would otherwise ask to overwrite).</summary>
    public static string NewPath(string program = Invi02Layout.FormCode)
    {
        Directory.CreateDirectory(Folder);
        return Path.Combine(Folder, $"{program}_{DateTime.Now:yyyyMMddHHmmssfff}.XLSX");
    }

    /// <summary>Waits until ERP has finished writing the file, then reads every sheet.</summary>
    public static async Task<IReadOnlyList<(string Name, List<string[]> Rows)>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + 10000;
        Exception? last = null;
        while (Environment.TickCount64 < deadline)
        {
            if (File.Exists(path))
            {
                try
                {
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return XlsxReader.ReadSheets(stream);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException)
                {
                    last = ex; // still being written
                }
            }
            await Pace.Delay(200, cancellationToken);
        }
        throw new InvalidOperationException(File.Exists(path)
            ? $"讀取 ERP 匯出的 Excel 失敗：{last?.Message}"
            : "ERP 沒有在指定位置產生 Excel 檔案。");
    }

    public static void Delete(string path, AppLogger log)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn("export", $"temp export not deleted: {ex.GetType().Name}");
        }
    }

    /// <summary>Removes exports left by an earlier run that was stopped midway.</summary>
    public static void CleanUp(AppLogger log)
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var prefix in Prefixes)
                foreach (var file in Directory.EnumerateFiles(Folder, prefix + "*.XLSX"))
                    Delete(file, log);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn("export", $"temp folder clean-up skipped: {ex.GetType().Name}");
        }
    }
}
