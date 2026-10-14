namespace CYInvoice.Core.AutoInput;

public sealed record AutoInputCommand(bool Issue, string RequestPath, string ResultPath);

public static class AutoInputCommandLine
{
    public static bool IsAutoInputInvocation(string[] arguments) =>
        arguments.Contains("--autoinput-issue", StringComparer.Ordinal) ||
        arguments.Contains("--autoinput-query", StringComparer.Ordinal);

    public static AutoInputCommand Parse(string[] arguments)
    {
        if (arguments.Length != 4 || arguments[2] != "--result" ||
            arguments[0] is not ("--autoinput-issue" or "--autoinput-query"))
            throw new InvalidDataException("Expected --autoinput-issue|--autoinput-query <request.json> --result <result.json>.");
        var request = arguments[1];
        var result = arguments[3];
        if (!Path.IsPathFullyQualified(request) || !Path.IsPathFullyQualified(result) ||
            string.Equals(Path.GetFullPath(request), Path.GetFullPath(result), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Request/result must be distinct absolute paths.");
        return new AutoInputCommand(arguments[0] == "--autoinput-issue", request, result);
    }
}
