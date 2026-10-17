namespace CYInvoice.Core.AutoInput;

public sealed record AutoInputCommand(bool Issue, string RequestPath, string ResultPath, bool Pdf = false);

public static class AutoInputCommandLine
{
    public static bool IsAutoInputInvocation(string[] arguments) =>
        arguments.Any(value => value.StartsWith("--autoinput-", StringComparison.Ordinal));

    public static AutoInputCommand Parse(string[] arguments)
    {
        if (arguments.Length != 4 || arguments[2] != "--result" ||
            arguments[0] is not ("--autoinput-issue" or "--autoinput-query" or "--autoinput-pdf"))
            throw new InvalidDataException("Expected --autoinput-issue|--autoinput-query|--autoinput-pdf <request.json> --result <result.json>.");
        var request = arguments[1];
        var result = arguments[3];
        if (!Path.IsPathFullyQualified(request) || !Path.IsPathFullyQualified(result) ||
            string.Equals(Path.GetFullPath(request), Path.GetFullPath(result), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Request/result must be distinct absolute paths.");
        return new AutoInputCommand(arguments[0] == "--autoinput-issue", request, result,
            Pdf: arguments[0] == "--autoinput-pdf");
    }
}
