namespace CYInvoiceVisualShell;

internal sealed class InputLabFormV1 : Form
{
    private readonly ErrorProvider errorProvider = new()
    {
        BlinkStyle = ErrorBlinkStyle.NeverBlink,
    };

    private readonly Label dpiLabel = new();
    private readonly Label compactMeasure = new();
    private readonly Label standardMeasure = new();

    private readonly TextBox compactText = new();
    private readonly ComboBox compactCombo = new();
    private readonly DateTimePicker compactDate = new();
    private readonly TextBox compactReadOnly = new();
    private readonly TextBox compactDisabled = new();
    private readonly TextBox compactError = new();

    private readonly TextBox standardText = new();
    private readonly ComboBox standardCombo = new();
    private readonly DateTimePicker standardDate = new();
    private readonly TextBox standardReadOnly = new();
    private readonly TextBox standardDisabled = new();
    private readonly TextBox standardError = new();

    private readonly TextBox compareText = new();
    private readonly ComboBox compareCombo = new();
    private readonly DateTimePicker compareDate = new();
    private readonly TextBox forcedTallText = new();

    internal InputLabFormV1()
    {
        Text = "CY Input Control Lab V1 — Native Height Validation";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1080, 760);
        MinimumSize = new Size(980, 700);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = VisualTokens.Window;
        Font = VisualTokens.Font(10f);

        errorProvider.ContainerControl = this;

        BuildLayout();
        Shown += (_, _) => BeginInvoke(UpdateMeasurements);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24),
            BackColor = VisualTokens.Window,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildDensityColumns(), 0, 1);
        root.Controls.Add(BuildSameRowComparison(), 0, 2);
        root.Controls.Add(BuildForcedHeightReference(), 0, 3);
        root.Controls.Add(BuildFooterNote(), 0, 4);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 18),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titleBlock = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        var title = new Label
        {
            AutoSize = true,
            Text = "Input Control Lab — 原生自然高度",
            Font = VisualTokens.Font(16f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
            Margin = Padding.Empty,
        };
        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            Text = "這版不替 TextBox / ComboBox / DatePicker 畫外框，也不硬把三者塞成同一個 Height。先量 WinForms 原生自然高度，再決定正式 layout 應如何對齊。",
            Font = VisualTokens.Font(9.5f),
            ForeColor = VisualTokens.TextSecondary,
            Margin = new Padding(0, 6, 0, 0),
        };
        titleBlock.Controls.Add(title);
        titleBlock.Controls.Add(note);

        dpiLabel.AutoSize = true;
        dpiLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        dpiLabel.Font = VisualTokens.Font(9.5f, FontStyle.Bold);
        dpiLabel.ForeColor = VisualTokens.GetPalette(CyTheme.Blue).Accent;
        dpiLabel.Margin = new Padding(16, 4, 0, 0);

        header.Controls.Add(titleBlock, 0, 0);
        header.Controls.Add(dpiLabel, 1, 0);
        return header;
    }

    private Control BuildDensityColumns()
    {
        var columns = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 18),
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var compact = BuildDensityCard(
            "A. Compact — 9.5 pt",
            9.5f,
            compactText,
            compactCombo,
            compactDate,
            compactReadOnly,
            compactDisabled,
            compactError,
            compactMeasure);
        compact.Margin = new Padding(0, 0, 10, 0);

        var standard = BuildDensityCard(
            "B. Standard — 10 pt",
            10f,
            standardText,
            standardCombo,
            standardDate,
            standardReadOnly,
            standardDisabled,
            standardError,
            standardMeasure);
        standard.Margin = new Padding(10, 0, 0, 0);

        columns.Controls.Add(compact, 0, 0);
        columns.Controls.Add(standard, 1, 0);
        return columns;
    }

    private Panel BuildDensityCard(
        string title,
        float fontPt,
        TextBox normal,
        ComboBox combo,
        DateTimePicker date,
        TextBox readOnly,
        TextBox disabled,
        TextBox error,
        Label measure)
    {
        var frame = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            BackColor = Color.White,
            Padding = new Padding(16),
            BorderStyle = BorderStyle.FixedSingle,
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 8,
            Margin = Padding.Empty,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var heading = new Label
        {
            AutoSize = true,
            Text = title,
            Font = VisualTokens.Font(11.5f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
            Margin = new Padding(0, 0, 0, 12),
        };
        table.SetColumnSpan(heading, 2);
        table.Controls.Add(heading, 0, 0);

        ConfigureTextBox(normal, "王小明", fontPt);
        ConfigureCombo(combo, fontPt);
        ConfigureDate(date, fontPt);
        ConfigureTextBox(readOnly, "唯讀內容", fontPt);
        readOnly.ReadOnly = true;
        readOnly.BackColor = VisualTokens.ReadOnly;
        ConfigureTextBox(disabled, "停用內容", fontPt);
        disabled.Enabled = false;
        ConfigureTextBox(error, "ABC-123", fontPt);

        AddField(table, 1, "姓　　名", normal);
        AddField(table, 2, "載具類型", combo);
        AddField(table, 3, "日　　期", date);
        AddField(table, 4, "唯　　讀", readOnly);
        AddField(table, 5, "停　　用", disabled);
        AddField(table, 6, "錯　　誤", BuildErrorHost(error, fontPt));

        measure.AutoSize = true;
        measure.Font = VisualTokens.Font(9f);
        measure.ForeColor = VisualTokens.TextSecondary;
        measure.Margin = new Padding(0, 10, 0, 0);
        table.SetColumnSpan(measure, 2);
        table.Controls.Add(measure, 0, 7);

        frame.Controls.Add(table);
        return frame;
    }

    private Control BuildSameRowComparison()
    {
        var section = NewSectionFrame("C. 同排比較 — Standard 10 pt / 不強制 Height");
        var body = (TableLayoutPanel)section.Controls[0];

        var caption = new Label
        {
            AutoSize = true,
            Text = "這一排最重要：看 TextBox / ComboBox / DatePicker 的可見外框高度、文字基準與垂直位置。Layout 只做置中對齊，不改原生 control height。",
            Font = VisualTokens.Font(9f),
            ForeColor = VisualTokens.TextSecondary,
            Margin = new Padding(0, 0, 0, 10),
        };
        body.SetColumnSpan(caption, 3);
        body.Controls.Add(caption, 0, 1);

        ConfigureTextBox(compareText, "一般文字", 10f);
        ConfigureCombo(compareCombo, 10f);
        ConfigureDate(compareDate, 10f);

        body.Controls.Add(BuildTopLabeledControl("TextBox", compareText), 0, 2);
        body.Controls.Add(BuildTopLabeledControl("ComboBox", compareCombo), 1, 2);
        body.Controls.Add(BuildTopLabeledControl("DatePicker", compareDate), 2, 2);
        return section;
    }

    private Control BuildForcedHeightReference()
    {
        var section = NewSectionFrame("D. 反例對照 — 強制拉高單行 TextBox");
        var body = (TableLayoutPanel)section.Controls[0];

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(850, 0),
            Text = "右邊只用來驗證我們之前看到的問題：若關掉 AutoSize 後硬拉高單行 TextBox，文字通常仍靠原生文字區位置，不會因外框變高而真正垂直置中。正式規範不預設採這種做法。",
            Font = VisualTokens.Font(9f),
            ForeColor = VisualTokens.TextSecondary,
            Margin = new Padding(0, 0, 0, 10),
        };
        body.SetColumnSpan(note, 3);
        body.Controls.Add(note, 0, 1);

        var natural = new TextBox();
        ConfigureTextBox(natural, "自然高度", 10f);
        ConfigureTextBox(forcedTallText, "強制 36px", 10f);
        forcedTallText.AutoSize = false;
        forcedTallText.Height = 36;

        body.Controls.Add(BuildTopLabeledControl("Natural", natural), 0, 2);
        body.Controls.Add(BuildTopLabeledControl("Forced 36 px", forcedTallText), 1, 2);

        var verdict = new Label
        {
            AutoSize = true,
            Text = "← 比較文字是否仍偏上",
            Font = VisualTokens.Font(9f, FontStyle.Bold),
            ForeColor = VisualTokens.GetPalette(CyTheme.Coral).Pressed,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(12, 23, 0, 0),
        };
        body.Controls.Add(verdict, 2, 2);
        return section;
    }

    private Control BuildFooterNote()
    {
        return new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(1000, 0),
            Text = "Error 狀態這版先用 WinForms 原生 ErrorProvider + helper text，刻意不為紅框重畫 TextBox。請先看高度與基準；紅框策略可在高度定案後再決定是否值得增加實作成本。",
            Font = VisualTokens.Font(9f),
            ForeColor = VisualTokens.TextSecondary,
            Margin = new Padding(0, 8, 0, 0),
        };
    }

    private static Panel NewSectionFrame(string title)
    {
        var frame = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Color.White,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 14),
            BorderStyle = BorderStyle.FixedSingle,
        };
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 3,
            Margin = Padding.Empty,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));

        var heading = new Label
        {
            AutoSize = true,
            Text = title,
            Font = VisualTokens.Font(11.5f, FontStyle.Bold),
            ForeColor = VisualTokens.TextPrimary,
            Margin = new Padding(0, 0, 0, 10),
        };
        body.SetColumnSpan(heading, 3);
        body.Controls.Add(heading, 0, 0);
        frame.Controls.Add(body);
        return frame;
    }

    private Control BuildErrorHost(TextBox box, float fontPt)
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
        };
        box.Dock = DockStyle.Top;
        var helper = new Label
        {
            AutoSize = true,
            Text = "格式錯誤，請重新輸入",
            Font = VisualTokens.Font(Math.Max(8.5f, fontPt - 1f)),
            ForeColor = VisualTokens.Danger,
            Margin = new Padding(2, 3, 0, 0),
        };
        host.Controls.Add(box, 0, 0);
        host.Controls.Add(helper, 0, 1);
        errorProvider.SetError(box, "格式錯誤");
        errorProvider.SetIconAlignment(box, ErrorIconAlignment.MiddleRight);
        return host;
    }

    private static Control BuildTopLabeledControl(string caption, Control control)
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 14, 0),
        };
        var label = new Label
        {
            AutoSize = true,
            Text = caption,
            Font = VisualTokens.Font(9f, FontStyle.Bold),
            ForeColor = VisualTokens.TextSecondary,
            Margin = new Padding(0, 0, 0, 5),
        };
        control.Width = 240;
        control.Anchor = AnchorStyles.Left;
        control.Margin = Padding.Empty;
        host.Controls.Add(label, 0, 0);
        host.Controls.Add(control, 0, 1);
        return host;
    }

    private static void AddField(TableLayoutPanel table, int row, string labelText, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label
        {
            AutoSize = true,
            Text = labelText,
            Font = control.Font,
            ForeColor = VisualTokens.TextPrimary,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 5, 10, 8),
        };
        control.Dock = DockStyle.Top;
        control.Margin = new Padding(0, 0, 20, 8);
        table.Controls.Add(label, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static void ConfigureTextBox(TextBox box, string text, float fontPt)
    {
        box.Text = text;
        box.Font = VisualTokens.Font(fontPt);
        box.BorderStyle = BorderStyle.Fixed3D;
        box.Width = 300;
        box.Margin = Padding.Empty;
        // Intentionally leave single-line AutoSize at its native default.
    }

    private static void ConfigureCombo(ComboBox combo, float fontPt)
    {
        combo.Font = VisualTokens.Font(fontPt);
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.System;
        combo.Width = 300;
        combo.Items.Clear();
        combo.Items.AddRange(new object[] { "一般消費者", "手機條碼載具", "公司統編" });
        combo.SelectedIndex = 0;
        combo.Margin = Padding.Empty;
    }

    private static void ConfigureDate(DateTimePicker date, float fontPt)
    {
        date.Font = VisualTokens.Font(fontPt);
        date.Format = DateTimePickerFormat.Custom;
        date.CustomFormat = "yyyy/MM/dd";
        date.Width = 300;
        date.Margin = Padding.Empty;
    }

    private void UpdateMeasurements()
    {
        dpiLabel.Text = $"Windows DPI：{DeviceDpi} ({DeviceDpi / 96.0:P0})";
        compactMeasure.Text = MeasureLine(compactText, compactCombo, compactDate);
        standardMeasure.Text = MeasureLine(standardText, standardCombo, standardDate);
    }

    private static string MeasureLine(TextBox text, ComboBox combo, DateTimePicker date)
    {
        return $"實際 Height：TextBox {text.Height}px　ComboBox {combo.Height}px　DatePicker {date.Height}px";
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(UpdateMeasurements);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            errorProvider.Dispose();
        base.Dispose(disposing);
    }
}
