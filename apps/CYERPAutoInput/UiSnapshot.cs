namespace CYERPAutoInput;

/// <summary>
/// <c>--ui-snapshot &lt;folder&gt;</c>: renders the main window (standard and advanced mode)
/// and the settings dialog to PNG so CI can publish them for layout review. It never
/// touches ERP and only reads the local settings file.
/// </summary>
internal static class UiSnapshot
{
    private const int WmGetMinMaxInfo = 0x0024;

    /// <summary>True only while rendering snapshots; lets forms exceed the CI screen size.</summary>
    internal static bool Active { get; private set; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    /// <summary>Called from form WndProc: the CI desktop is smaller than the real window sizes.</summary>
    internal static void AllowOversize(ref Message m)
    {
        if (!Active || m.Msg != WmGetMinMaxInfo) return;
        var info = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(m.LParam);
        info.MaxTrackSize = new Point(4000, 4000);
        System.Runtime.InteropServices.Marshal.StructureToPtr(info, m.LParam, false);
    }

    public static int Run(AppLogger log, string folder)
    {
        try
        {
            Active = true;
            Directory.CreateDirectory(folder);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var form = new MainForm(log))
            {
                CyVisualTheme.Apply(form);
                Capture(form, Path.Combine(folder, "main-standard.png"), new Size(1160, 720));
                form.SetAdvancedModeForSnapshot(true);
                Capture(form, Path.Combine(folder, "main-advanced.png"), new Size(1600, 900));
            }

            using (var settings = new SettingsForm(new UserSettings()))
                Capture(settings, Path.Combine(folder, "settings.png"), settings.Size);

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

    private static void Capture(Form form, string path, Size size)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.ShowInTaskbar = false;
        if (!form.Visible) form.Show();
        // A maximized window would take the CI screen size; render the intended size instead.
        form.WindowState = FormWindowState.Normal;
        form.Bounds = new Rectangle(-32000, -32000, size.Width, size.Height);
        for (var i = 0; i < 3; i++) { Application.DoEvents(); form.PerformLayout(); }

        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
