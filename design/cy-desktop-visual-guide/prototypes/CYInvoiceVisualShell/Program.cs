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
            using (var tab = new TabLabFormV1())
            {
                tab.CreateControl();
                tab.PerformLayout();
            }

            using (var table = new TableLabFormV1())
            {
                table.CreateControl();
                table.PerformLayout();
            }
            return;
        }

        Application.Run(new TabLabFormV1());
    }
}
