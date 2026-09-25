namespace CYERPAutoInput;

internal sealed class AutomationRunResult
{
    public string SalesOrderNumber { get; set; } = string.Empty;
    public List<AutomationWarning> Warnings { get; } = [];
}

internal sealed record AutomationWarning(
    string Code,
    string SalesOrderNumber,
    int DetailRow,
    string ItemCode,
    string Message);
