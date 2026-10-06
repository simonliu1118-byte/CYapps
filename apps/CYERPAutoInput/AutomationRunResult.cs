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
    /// <summary>Why auto-save did not run although it was enabled; empty when saved or disabled.</summary>
    public string SaveSkippedReason { get; set; } = string.Empty;
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
