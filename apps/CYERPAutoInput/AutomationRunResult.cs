namespace CYERPAutoInput;

internal sealed class AutomationRunResult
{
    public string SalesOrderType { get; set; } = string.Empty;
    public string SalesOrderNumber { get; set; } = string.Empty;
    public string DocumentKey =>
        string.IsNullOrWhiteSpace(SalesOrderType) || string.IsNullOrWhiteSpace(SalesOrderNumber)
            ? SalesOrderNumber
            : $"{SalesOrderType}-{SalesOrderNumber}";
    public List<AutomationWarning> Warnings { get; } = [];
    public bool Saved { get; set; }
    public bool Printed { get; set; }
    /// <summary>Why the automatic 直接列印 did not happen although it was enabled; empty otherwise.</summary>
    public string PrintProblem { get; set; } = string.Empty;
    /// <summary>全熱感紙列印: the saved 銷貨單 exported by ERP 轉出至 Excel; empty when not done (see <see cref="PrintProblem"/>).</summary>
    public string ExportFile { get; set; } = string.Empty;
    /// <summary>Why auto-save did not run although it was enabled; empty when saved or disabled.</summary>
    public string SaveSkippedReason { get; set; } = string.Empty;
    /// <summary>Set when the run stopped before the details on purpose and a person takes over.</summary>
    public string HandedOff { get; set; } = string.Empty;
}

internal sealed record AutomationWarning(
    string Code,
    string SalesOrderType,
    string SalesOrderNumber,
    int DetailRow,
    string ItemCode,
    string Message)
{
    public string DocumentKey =>
        string.IsNullOrWhiteSpace(SalesOrderType) || string.IsNullOrWhiteSpace(SalesOrderNumber)
            ? SalesOrderNumber
            : $"{SalesOrderType}-{SalesOrderNumber}";
}
