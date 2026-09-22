namespace CYInvoiceVisualShell;

internal static class DemoDialogs
{
    internal static void ShowSettings(IWin32Window owner, ThemePalette palette, DensityMetrics metrics)
    {
        using var form = CreateBase("設定（UI Shell）", new Size(520, 330), metrics);
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(20),
            BackColor = VisualTokens.Window,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = VisualTokens.Window,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.InputHeight + metrics.FieldGap));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.InputHeight + metrics.FieldGap));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, metrics.InputHeight + metrics.FieldGap));

        var company = NewTextBox("志遠醫療器材行", palette, metrics);
        var account = NewTextBox("管理員", palette, metrics);
        var endpoint = NewTextBox("https://example.invalid/api", palette, metrics);
        AddField(fields, 0, "公司名稱", company, metrics);
        AddField(fields, 1, "操作帳號", account, metrics);
        AddField(fields, 2, "API 位置", endpoint, metrics);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = VisualTokens.Window,
        };
        var save = new CyButton("儲存", CyButtonRole.Primary) { DialogResult = DialogResult.OK };
        var cancel = new CyButton("取消") { DialogResult = DialogResult.Cancel };
        save.ApplyVisual(palette, metrics);
        cancel.ApplyVisual(palette, metrics);
        footer.Controls.Add(save);
        footer.Controls.Add(cancel);

        root.Controls.Add(fields, 0, 0);
        root.Controls.Add(footer, 0, 1);
        form.Controls.Add(root);
        form.AcceptButton = save;
        form.CancelButton = cancel;
        form.ShowDialog(owner);
    }

    internal static void ShowInfo(IWin32Window owner, ThemePalette palette, DensityMetrics metrics)
    {
        using var form = CreateBase("UI Shell 說明", new Size(470, 250), metrics);
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(24),
            BackColor = VisualTokens.Window,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var text = new Label
        {
            Dock = DockStyle.Fill,
            Text = "這是純 UI / 假資料測試殼。\r\n不連接 AMEGO、不寫入資料庫，也不會開立或作廢任何發票。",
            ForeColor = VisualTokens.TextPrimary,
            Font = VisualTokens.Font(metrics.BodyPt),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = VisualTokens.Window };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var close = new CyButton("關閉") { DialogResult = DialogResult.OK };
        close.ApplyVisual(palette, metrics);
        footer.Controls.Add(close, 1, 0);

        root.Controls.Add(text, 0, 0);
        root.Controls.Add(footer, 0, 1);
        form.Controls.Add(root);
        form.AcceptButton = close;
        form.CancelButton = close;
        form.ShowDialog(owner);
    }

    internal static bool ConfirmDanger(IWin32Window owner, ThemePalette palette, DensityMetrics metrics, string subject)
    {
        using var form = CreateBase("刪除確認", new Size(470, 260), metrics);
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(24),
            BackColor = VisualTokens.Window,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var body = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"確定要刪除「{subject}」這筆測試資料嗎？\r\n\r\n這只會移除目前 UI Shell 記憶體中的假資料。",
            Font = VisualTokens.Font(metrics.BodyPt),
            ForeColor = VisualTokens.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = VisualTokens.Window,
        };
        var delete = new CyButton("刪除", CyButtonRole.Danger) { DialogResult = DialogResult.OK };
        var cancel = new CyButton("取消") { DialogResult = DialogResult.Cancel };
        delete.ApplyVisual(palette, metrics);
        cancel.ApplyVisual(palette, metrics);
        footer.Controls.Add(delete);
        footer.Controls.Add(cancel);
        root.Controls.Add(body, 0, 0);
        root.Controls.Add(footer, 0, 1);
        form.Controls.Add(root);
        form.AcceptButton = delete;
        form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK;
    }

    private static Form CreateBase(string title, Size size, DensityMetrics metrics) => new()
    {
        Text = title,
        StartPosition = FormStartPosition.CenterParent,
        ClientSize = size,
        MinimizeBox = false,
        MaximizeBox = false,
        ShowIcon = true,
        BackColor = VisualTokens.Window,
        Font = VisualTokens.Font(metrics.BodyPt),
        AutoScaleMode = AutoScaleMode.Dpi,
        FormBorderStyle = FormBorderStyle.FixedDialog,
    };

    private static CyTextBox NewTextBox(string text, ThemePalette palette, DensityMetrics metrics)
    {
        var box = new CyTextBox { Dock = DockStyle.Fill, Text = text };
        box.ApplyVisual(palette, metrics);
        return box;
    }

    private static void AddField(TableLayoutPanel table, int row, string text, Control control, DensityMetrics metrics)
    {
        var label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = VisualTokens.TextPrimary,
            Font = VisualTokens.Font(metrics.BodyPt),
            Margin = new Padding(0, 0, 8, 0),
        };
        table.Controls.Add(label, 0, row);
        table.Controls.Add(control, 1, row);
    }
}
