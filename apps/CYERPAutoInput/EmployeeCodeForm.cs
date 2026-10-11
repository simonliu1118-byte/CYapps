namespace CYERPAutoInput;

/// <summary>
/// Small start-up prompt for this session's 員工代號 (temporary until CYID; user,
/// 2026-10-11). Enter confirms; 略過 keeps the local default.
/// </summary>
internal sealed class EmployeeCodeForm : Form
{
    private readonly TextBox _code = new() { Width = 180, MaxLength = 20 };

    public string EmployeeCode => _code.Text.Trim();

    public EmployeeCodeForm(string current)
    {
        Text = "員工代號";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false; // secondary dialogs do not repeat the app icon (CY Desktop Visual Guide §11.1)
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 9.5F);
        ClientSize = new Size(360, 150);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16, 14, 16, 10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label { Text = "請輸入這次使用的員工代號（本次開啟期間都使用）：", AutoSize = true }, 0, 0);
        _code.Text = current;
        root.Controls.Add(_code, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var ok = new CyPrimaryButton { Text = "確定", Width = 92, Height = 34, DialogResult = DialogResult.OK };
        var skip = new CyButton { Text = "略過", Width = 92, Height = 34, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(skip);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = skip;
        CyVisualTheme.Apply(this);
        Shown += (_, _) => { _code.Focus(); _code.SelectAll(); };
    }
}
