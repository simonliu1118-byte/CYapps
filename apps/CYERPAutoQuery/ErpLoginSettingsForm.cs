namespace CYERPAutoQuery;

/// <summary>
/// ERP 登入設定 (user, 2026-10-19): the ERP program (chosen like CYERPAutoInput's CYInvoice.exe),
/// 帳號、密碼、登入公司, and the idle minutes after which the 建立作業 forms and ERP are closed.
/// </summary>
internal sealed class ErpLoginSettingsForm : Form
{
    private readonly ErpLoginSettings _settings;
    private readonly TextBox _path = new() { Dock = DockStyle.Fill, ReadOnly = true, TabStop = false };
    private readonly TextBox _account = new() { Dock = DockStyle.Fill };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _company = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _formIdle = new() { Minimum = 0, Maximum = 600, Width = 80 };
    private readonly NumericUpDown _idle = new() { Minimum = 0, Maximum = 600, Width = 80 };
    private readonly CheckBox _startup = new() { Text = "開機時自動開啟本程式", AutoSize = true, Anchor = AnchorStyles.Left };

    /// <summary>Whether the program starts with Windows (written to the Run key by the caller).</summary>
    public bool StartWithWindows => _startup.Checked;

    public ErpLoginSettingsForm(ErpLoginSettings settings, bool startWithWindows)
    {
        _settings = settings;
        Text = "ERP 登入設定";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 9.5F);
        ClientSize = new Size(640, 410);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(16, 14, 16, 10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 8; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(Label("ERP 程式"), 0, 0);
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _path.Text = settings.ErpPath;
        _path.PlaceholderText = "選擇 SMART ERP 的執行檔";
        _path.Margin = new Padding(0, 4, 6, 4);
        var browse = new CyButton { Text = "選擇…", Width = 80, Height = 30 };
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "選擇 SMART ERP 執行檔", Filter = "程式 (*.exe)|*.exe", CheckFileExists = true };
            if (_path.Text.Length > 0 && Directory.Exists(Path.GetDirectoryName(_path.Text))) dialog.InitialDirectory = Path.GetDirectoryName(_path.Text);
            if (dialog.ShowDialog(this) == DialogResult.OK) _path.Text = dialog.FileName;
        };
        var clear = new CyButton { Text = "清除", Width = 70, Height = 30 };
        clear.Click += (_, _) => _path.Text = string.Empty;
        pathRow.Controls.Add(_path, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);
        pathRow.Controls.Add(clear, 2, 0);
        root.Controls.Add(pathRow, 1, 0);

        root.Controls.Add(Label("帳號"), 0, 1);
        _account.Text = settings.Account;
        root.Controls.Add(_account, 1, 1);

        root.Controls.Add(Label("密碼"), 0, 2);
        _password.PlaceholderText = settings.HasPassword ? "已設定（留空表示不變更）" : "ERP 登入密碼";
        root.Controls.Add(_password, 1, 2);

        root.Controls.Add(Label("登入公司"), 0, 3);
        _company.Text = settings.Company;
        _company.PlaceholderText = ErpLoginSettings.DefaultCompany;
        root.Controls.Add(_company, 1, 3);

        root.Controls.Add(Label("閒置關閉作業"), 0, 4);
        _formIdle.Value = Math.Clamp(settings.FormIdleMinutes, 0, 600);
        root.Controls.Add(IdleRow(_formIdle, "分鐘沒有查詢就關閉品號、客戶、廠商資料建立作業（0 表示不關閉）"), 1, 4);

        root.Controls.Add(Label("閒置關閉 ERP"), 0, 5);
        _idle.Value = Math.Clamp(settings.IdleCloseMinutes, 0, 600);
        root.Controls.Add(IdleRow(_idle, "分鐘沒有查詢就關閉 ERP，釋出登入名額（0 表示不關閉）"), 1, 5);

        _startup.Checked = startWithWindows;
        root.Controls.Add(Label("開機"), 0, 6);
        root.Controls.Add(_startup, 1, 6);

        root.Controls.Add(new Label
        {
            Text = "查詢時找不到 ERP 會自動開啟並登入，ERP 沒有回應時會強制關閉重開；密碼以 Windows 帳戶加密保存在本機。",
            AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = CyVisualTheme.TextSecondary, Anchor = AnchorStyles.Left
        }, 1, 7);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var ok = new CyPrimaryButton { Text = "儲存", Width = 92, Height = 34 };
        var cancel = new CyButton { Text = "取消", Width = 92, Height = 34, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Save();
        var diagnose = new Button { Text = "行程診斷（唯讀）", AutoSize = true, Height = 34 }; 
        diagnose.Click += (_, _) => { using var tool = new ErpProcessDiagnosticForm(_path.Text.Trim()); tool.ShowDialog(this); };
        buttons.Controls.Add(diagnose);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 8);
        root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
        CyVisualTheme.Apply(this);
    }

    private static Label Label(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };

    private static FlowLayoutPanel IdleRow(NumericUpDown minutes, string text)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        row.Controls.Add(minutes);
        row.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(6, 6, 0, 0) });
        return row;
    }

    private void Save()
    {
        var path = _path.Text.Trim();
        if (path.Length > 0 && !File.Exists(path))
        {
            MessageBox.Show(this, "找不到選擇的 ERP 程式，請重新選擇。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _settings.ErpPath = path;
        _settings.Account = _account.Text.Trim();
        if (_password.Text.Length > 0) _settings.SetPassword(_password.Text);
        _settings.Company = _company.Text.Trim().Length > 0 ? _company.Text.Trim() : ErpLoginSettings.DefaultCompany;
        _settings.FormIdleMinutes = (int)_formIdle.Value;
        _settings.IdleCloseMinutes = (int)_idle.Value;
        DialogResult = DialogResult.OK;
    }
}
