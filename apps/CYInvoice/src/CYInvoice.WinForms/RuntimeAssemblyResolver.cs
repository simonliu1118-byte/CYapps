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
        var smokeTest = Environment.GetCommandLineArgs().Contains("--startup-smoke-test", StringComparer.Ordinal);
        var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var webViewDirectory = Path.Combine(executableDirectory, "Runtime", "WebView2");
        var loadContext = AssemblyLoadContext.GetLoadContext(typeof(RuntimeAssemblyResolver).Assembly)
            ?? AssemblyLoadContext.Default;
        if (smokeTest)
        {
            Console.Error.WriteLine($"WebView2 loader: process={Environment.ProcessPath}");
            Console.Error.WriteLine($"WebView2 loader: base={AppContext.BaseDirectory}");
            Console.Error.WriteLine($"WebView2 loader: directory={webViewDirectory}");
        }
        loadContext.Resolving += (context, name) => Resolve(context, name, webViewDirectory);
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            Resolve(loadContext, new AssemblyName(args.Name), webViewDirectory);
        LoadIfPresent(loadContext, webViewDirectory, "Microsoft.Web.WebView2.Core", smokeTest);
        LoadIfPresent(loadContext, webViewDirectory, "Microsoft.Web.WebView2.WinForms", smokeTest);
    }

    private static void LoadIfPresent(
        AssemblyLoadContext context,
        string webViewDirectory,
        string simpleName,
        bool smokeTest)
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                string.Equals(assembly.GetName().Name, simpleName, StringComparison.Ordinal))) return;
        var path = Path.Combine(webViewDirectory, simpleName + ".dll");
        var assembly = Resolve(context, new AssemblyName(simpleName), webViewDirectory);
        if (smokeTest)
        {
            Console.Error.WriteLine($"WebView2 loader: {simpleName} exists={File.Exists(path)} loaded={assembly is not null}");
        }
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
