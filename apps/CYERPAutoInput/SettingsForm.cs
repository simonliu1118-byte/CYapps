namespace CYERPAutoInput;

internal sealed class SettingsForm : Form
{
    private readonly UserSettings _settings;
    private readonly DataGridView _grid = new();
    private readonly CheckBox _advanced = new();

    private static readonly string[] ConfigurableKeys =
    [
        "order_type", "dept_code", "currency", "salesperson", "receipt_salesperson",
        "employee_code", "payment_terms", "delivery_slot", "freight_type",
        "inv_copies", "tax_type", "customs", "tax_rate"
    ];

    public SettingsForm(UserSettings settings)
    {
        _settings = settings;
        Text = "CYERPAutoInput 設定";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(650, 610);
        MinimumSize = new Size(560, 480);
        Font = new Font("Microsoft JhengHei UI", 9F);
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "以下為本機預設值。實際 ERP 代碼只保存在這台電腦的 config/settings.json，不會寫入 Public source。",
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _advanced.Text = "啟動時使用進階模式";
        _advanced.Checked = _settings.AdvancedMode;
        _advanced.AutoSize = true;
        _advanced.Padding = new Padding(3, 5, 0, 0);
        root.Controls.Add(_advanced, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoGenerateColumns = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Label",
            HeaderText = "欄位",
            ReadOnly = true,
            Width = 210,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Value",
            HeaderText = "本機預設值",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        foreach (var key in ConfigurableKeys)
        {
            var field = FieldCatalog.All.First(f => f.Key == key);
            var value = _settings.Defaults.TryGetValue(key, out var saved) ? saved : string.Empty;
            var index = _grid.Rows.Add(field.Label, value);
            _grid.Rows[index].Tag = key;
        }
        root.Controls.Add(_grid, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(4, 8, 4, 0) };
        var save = new Button { Text = "儲存", Width = 100, Height = 32 };
        save.Click += (_, _) => SaveAndClose();
        var cancel = new Button { Text = "取消", Width = 100, Height = 32, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 3);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void SaveAndClose()
    {
        _settings.AdvancedMode = _advanced.Checked;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not string key) continue;
            var value = Convert.ToString(row.Cells["Value"].Value)?.Trim() ?? string.Empty;
            if (value.Length == 0) _settings.Defaults.Remove(key);
            else _settings.Defaults[key] = value;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
