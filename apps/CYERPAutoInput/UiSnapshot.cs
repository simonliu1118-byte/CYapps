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

            using (var settings = new SettingsForm(new UserSettings(), () => Task.FromResult(string.Empty)))
                Capture(settings, Path.Combine(folder, "settings.png"), settings.Size);

            // Fictitious MO店+ orders: one ready, one handed off, one needing the invoice name.
            var mapping = new MoMapping("T01", "C0001", "MO店+訂單", "D901", "S100", MoOrderImport.ParseFreightTable("7-11=F1"));
            var sample = new List<ImportedOrder>
            {
                MoOrderImport.ToImported(new MoOrder(2, "90000000000001", [new MoOrderLine("X00001", 2, 300, 560, "測試商品")],
                    "7-11店出", "T0000001", "", "", "", "", 605), mapping),
                MoOrderImport.ToImported(new MoOrder(3, "90000000000002", [new MoOrderLine("X00002+ X00003", 1, 360, 360, "組合")],
                    "7-11店出", "", "", "", "", "", 360), mapping),
                MoOrderImport.ToImported(new MoOrder(4, "90000000000003", [new MoOrderLine("X00004", 1, 500, 500, "測試商品")],
                    "全家店出", "", "", "12345678", "", "", 500), mapping)
            };
            using (var preview = new OrderImportForm(MoOrderImport.Source, "OrderExport_TEST.xls", sample,
                       ["第 9 列訂單 90000000000009：運費對不上：開立發票金額推算為 45，運費欄位合計為 0。"], autoSave: true, _ => null))
                Capture(preview, Path.Combine(folder, "mo-import.png"), preview.Size);

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

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private static void Capture(Form form, string path, Size size)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.ShowInTaskbar = false;
        if (!form.Visible) form.Show();
        // A maximized window would take the CI screen size; render the intended size instead.
        form.WindowState = FormWindowState.Normal;
        // Form.SetBoundsCore caps the size at the CI screen; size the native window directly
        // (WM_GETMINMAXINFO above lifts the OS limit).
        NativeMethods.SetWindowPos(form.Handle, 0, -32000, -32000, size.Width, size.Height, SwpNoZOrder | SwpNoActivate);
        for (var i = 0; i < 3; i++) { Application.DoEvents(); form.PerformLayout(); }
        if (form is MainForm main && main.DetailRowsShortfall() is > 0 and var shortfall)
        {
            // Same growth the real window does on Load: the detail grid shows exactly 10 rows.
            NativeMethods.SetWindowPos(form.Handle, 0, -32000, -32000, size.Width, size.Height + shortfall, SwpNoZOrder | SwpNoActivate);
            for (var i = 0; i < 3; i++) { Application.DoEvents(); form.PerformLayout(); }
        }

        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
