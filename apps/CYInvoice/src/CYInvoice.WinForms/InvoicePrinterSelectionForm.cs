namespace CYInvoice.WinForms;

internal sealed class InvoicePrinterSelectionForm : Form
{
    private readonly ComboBox printers = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        Margin = new Padding(3, 5, 3, 5),
    };
    private readonly Label capability = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.DimGray,
        Margin = new Padding(3),
    };
    private readonly Button usePrinter = UiControls.StandardButton("使用此印表機");
    private readonly Button cancel = UiControls.StandardButton("取消");

    public InvoicePrinterSelectionForm(string currentPrinter)
    {
        Text = "選擇發票印表機";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 210);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        Icon = ApplicationIcon.Load();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 14, 18, 12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label
        {
            Text = "選擇 CYInvoice 專用的發票印表機",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
            Margin = Padding.Empty,
        }, 0, 0);
        root.Controls.Add(printers, 0, 1);
        root.Controls.Add(capability, 0, 2);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        cancel.DialogResult = DialogResult.Cancel;
        usePrinter.Click += (_, _) =>
        {
            if (printers.SelectedItem is not string selected || selected.Length == 0) return;
            SelectedPrinterName = selected;
            DialogResult = DialogResult.OK;
            Close();
        };
        actions.Controls.Add(usePrinter);
        actions.Controls.Add(cancel);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
        AcceptButton = usePrinter;
        CancelButton = cancel;

        foreach (var printer in InvoicePdfPrinter.InstalledPrinters()) printers.Items.Add(printer);
        if (printers.Items.Count == 0)
        {
            capability.Text = "Windows 目前沒有可用的已安裝印表機。";
            usePrinter.Enabled = false;
        }
        else
        {
            var selectedIndex = -1;
            for (var index = 0; index < printers.Items.Count; index++)
            {
                if (string.Equals(printers.Items[index]?.ToString(), currentPrinter, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = index;
                    break;
                }
            }
            printers.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            UpdateCapability();
        }
        printers.SelectedIndexChanged += (_, _) => UpdateCapability();
    }

    public string SelectedPrinterName { get; private set; } = string.Empty;

    private void UpdateCapability()
    {
        if (printers.SelectedItem is not string selected || selected.Length == 0)
        {
            capability.Text = "請選擇印表機。";
            return;
        }
        capability.Text = InvoicePdfPrinter.CanDuplex(selected)
            ? "此印表機支援雙面；一般消費者紙本將使用雙面、長邊翻轉。"
            : "此印表機未回報雙面能力；一般消費者紙本將使用單面。";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Icon?.Dispose();
        base.Dispose(disposing);
    }
}
