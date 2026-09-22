namespace CYInvoiceVisualShell;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Any(a => string.Equals(a, "--smoke-test", StringComparison.OrdinalIgnoreCase)))
        {
            using var form = new ButtonLabFormV6();
            form.CreateControl();
            form.PerformLayout();
            return;
        }

        Application.Run(new ButtonLabFormV6());
    }
}
