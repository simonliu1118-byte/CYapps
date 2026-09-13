using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using CYInvoice.Core.Imports;

namespace CYInvoice.WinForms;

internal static class ExcelComRows
{
    public static Task<IReadOnlyList<IReadOnlyList<string>>> ReadFirstWorksheetAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(password);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("MO店+ Excel 匯入僅支援 Windows");
        }

        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到要匯入的 MO店+ Excel", fullPath);
        }

        var completion = new TaskCompletionSource<IReadOnlyList<IReadOnlyList<string>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(ReadFirstWorksheet(fullPath, password, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "CYInvoice Excel importer",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadFirstWorksheet(
        string filePath,
        string password,
        CancellationToken cancellationToken)
    {
        object? excel = null;
        object? workbooks = null;
        object? workbook = null;
        object? worksheets = null;
        object? worksheet = null;
        object? usedRange = null;
        object? cells = null;
        Exception? operationError = null;
        Exception? cleanupError = null;

        try
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application", throwOnError: false)
                ?? throw new InvalidOperationException("啟動 Microsoft Excel 失敗，請確認電腦已安裝 Excel");
            excel = Activator.CreateInstance(excelType)
                ?? throw new InvalidOperationException("啟動 Microsoft Excel 失敗，請確認電腦已安裝 Excel");

            SetProperty(excel, "Visible", false);
            SetProperty(excel, "DisplayAlerts", false);
            SetRequiredSafetyProperty(excel, "AutomationSecurity", 3, "停用 Excel 巨集失敗");
            SetRequiredSafetyProperty(excel, "EnableEvents", false, "停用 Excel 事件失敗");
            SetRequiredSafetyProperty(excel, "AskToUpdateLinks", false, "停用 Excel 外部連結提示失敗");

            workbooks = GetProperty(excel, "Workbooks");
            try
            {
                workbook = InvokeMethod(workbooks, "Open", filePath, 0, true, 5, password);
            }
            catch (Exception error)
            {
                throw new InvalidDataException("Excel 開啟失敗，請確認檔案是 MO店+ 原始 OrderExport 且保護密碼正確", Unwrap(error));
            }

            var openBook = workbook ?? throw new InvalidOperationException("Excel 開啟活頁簿後沒有回傳可讀取物件");
            worksheets = GetProperty(openBook, "Worksheets");
            worksheet = GetProperty(worksheets, "Item", 1);
            usedRange = GetProperty(worksheet, "UsedRange");
            var rowCount = CollectionCount(usedRange, "Rows");
            var columnCount = CollectionCount(usedRange, "Columns");
            if (rowCount < 1 || columnCount < 1)
            {
                throw new InvalidDataException("Excel 沒有可匯入的資料");
            }
            if (rowCount > XlsxRows.MaximumRows || columnCount > XlsxRows.MaximumColumns)
            {
                throw new InvalidDataException($"Excel 使用範圍異常（{rowCount} 列 × {columnCount} 欄），已停止匯入");
            }

            cells = GetProperty(usedRange, "Cells");
            var rows = new List<IReadOnlyList<string>>(rowCount);
            for (var rowIndex = 1; rowIndex <= rowCount; rowIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = new string[columnCount];
                for (var columnIndex = 1; columnIndex <= columnCount; columnIndex++)
                {
                    object? cell = null;
                    try
                    {
                        cell = GetProperty(cells, "Item", rowIndex, columnIndex);
                        var displayed = GetOptionalProperty(cell, "Text");
                        var raw = GetOptionalProperty(cell, "Value2");
                        row[columnIndex - 1] = SpreadsheetCellValue.FromExcel(
                            Convert.ToString(displayed, CultureInfo.InvariantCulture), raw);
                    }
                    catch (Exception error)
                    {
                        throw new InvalidDataException(
                            $"讀取 Excel 第 {rowIndex} 列第 {columnIndex} 欄內容失敗", Unwrap(error));
                    }
                    finally
                    {
                        Release(ref cell);
                    }
                }
                rows.Add(row);
            }
            return rows;
        }
        catch (Exception error)
        {
            operationError = error;
            throw;
        }
        finally
        {
            Release(ref cells);
            Release(ref usedRange);
            Release(ref worksheet);
            Release(ref worksheets);

            if (workbook is not null)
            {
                var openWorkbook = workbook;
                TryCleanup(() => InvokeMethod(openWorkbook, "Close", false), ref cleanupError);
            }
            Release(ref workbook);
            Release(ref workbooks);

            if (excel is not null)
            {
                var runningExcel = excel;
                TryCleanup(() => InvokeMethod(runningExcel, "Quit"), ref cleanupError);
            }
            Release(ref excel);

            if (operationError is null && cleanupError is not null)
            {
                throw new InvalidOperationException("關閉 Excel 匯入工作階段失敗", cleanupError);
            }
        }
    }

    private static void SetRequiredSafetyProperty(object target, string name, object value, string message)
    {
        try
        {
            SetProperty(target, name, value);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"{message}，為保護電腦已停止匯入", Unwrap(error));
        }
    }

    private static int CollectionCount(object target, string collectionName)
    {
        object? collection = null;
        try
        {
            collection = GetProperty(target, collectionName);
            var count = GetProperty(collection, "Count");
            return Convert.ToInt32(count, CultureInfo.InvariantCulture);
        }
        catch (Exception error)
        {
            throw new InvalidDataException($"取得 Excel {collectionName} 數量失敗", Unwrap(error));
        }
        finally
        {
            Release(ref collection);
        }
    }

    private static object GetProperty(object target, string name, params object?[] arguments) =>
        InvokeMember(target, name, BindingFlags.GetProperty, arguments)
        ?? throw new InvalidOperationException($"Excel {name} 沒有回傳物件");

    private static object? GetOptionalProperty(object target, string name) =>
        InvokeMember(target, name, BindingFlags.GetProperty, []);

    private static void SetProperty(object target, string name, object value) =>
        _ = InvokeMember(target, name, BindingFlags.SetProperty, [value]);

    private static object? InvokeMethod(object target, string name, params object?[] arguments) =>
        InvokeMember(target, name, BindingFlags.InvokeMethod, arguments);

    private static object? InvokeMember(object target, string name, BindingFlags operation, object?[] arguments) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.Public | BindingFlags.Instance | operation,
            binder: null,
            target,
            arguments,
            CultureInfo.InvariantCulture);

    private static Exception Unwrap(Exception error)
    {
        while (error is TargetInvocationException { InnerException: not null } invocation)
        {
            error = invocation.InnerException!;
        }
        return error;
    }

    private static void TryCleanup(Action action, ref Exception? firstError)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            firstError ??= Unwrap(error);
        }
    }

    private static void Release(ref object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
        value = null;
    }
}
