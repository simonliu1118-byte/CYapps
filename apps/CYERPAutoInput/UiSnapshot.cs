namespace CYERPAutoInput;

/// <summary>
/// <c>--ui-snapshot &lt;folder&gt;</c>: renders the main window (standard and advanced mode)
/// and the settings dialog to PNG so CI can publish them for layout review. It never
/// touches ERP and only reads the local settings file.
/// </summary>
internal static class UiSnapshot
{
    public static int Run(AppLogger log, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var form = new MainForm(log))
            {
                CyVisualTheme.Apply(form);
                Capture(form, Path.Combine(folder, "main-standard.png"));
                form.SetAdvancedModeForSnapshot(true);
                Capture(form, Path.Combine(folder, "main-advanced.png"));
            }

            using (var settings = new SettingsForm(new UserSettings()))
                Capture(settings, Path.Combine(folder, "settings.png"));

            Console.WriteLine($"ui snapshots written to {folder}");
            return 0;
        }
        catch (Exception ex)
        {
            log.Error("ui-snapshot", ex);
            Console.Error.WriteLine($"ui snapshot failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void Capture(Form form, string path)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.ShowInTaskbar = false;
        form.Location = new Point(-32000, -32000);
        if (!form.Visible) form.Show();
        Application.DoEvents();
        form.PerformLayout();
        Application.DoEvents();

        var size = form.WindowState == FormWindowState.Maximized ? new Size(1600, 900) : form.Size;
        if (form.WindowState == FormWindowState.Maximized)
        {
            form.WindowState = FormWindowState.Normal;
            form.Size = size;
            Application.DoEvents();
        }

        using var bitmap = new Bitmap(size.Width, size.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
