using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace CYInvoice.WinForms;

internal static class RuntimeAssemblyResolver
{
    private static int configured;

    [ModuleInitializer]
    public static void Configure()
    {
        if (Interlocked.Exchange(ref configured, 1) != 0) return;
        var webViewDirectory = Path.Combine(AppContext.BaseDirectory, "Runtime", "WebView2");
        AssemblyLoadContext.Default.Resolving += (context, name) => Resolve(context, name, webViewDirectory);
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            Resolve(AssemblyLoadContext.Default, new AssemblyName(args.Name), webViewDirectory);
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name, string webViewDirectory)
    {
        var simpleName = name.Name ?? string.Empty;
        if (!simpleName.StartsWith("Microsoft.Web.WebView2.", StringComparison.Ordinal)) return null;
        var path = Path.GetFullPath(Path.Combine(webViewDirectory, simpleName + ".dll"));
        var allowedRoot = Path.GetFullPath(webViewDirectory) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        return context.LoadFromAssemblyPath(path);
    }
}
