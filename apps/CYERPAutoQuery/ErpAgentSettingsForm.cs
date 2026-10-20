namespace CYERPAutoQuery;

/// <summary>
/// CY Web 連線 settings: the CY Web address, the Agent token a Super Admin generated on the
/// 「ERP 品號查詢」 page (shown once there), and whether this PC answers Web queries.
/// </summary>
internal sealed class ErpAgentSettingsForm : Form
{
    private readonly ErpAgentSettings _settings;
    private readonly TextBox _url = new() { Dock = DockStyle.Fill };
    private readonly TextBox _token = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly CheckBox _enabled = new() { Text = "啟用 Web 查詢（程式開著時自動連線、接收查詢）", AutoSize = true };
    private bool _clearToken;

    public ErpAgentSettingsForm(ErpAgentSettings settings)
    {
        _settings = settings;
        Text = "Web 連線設定";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false; // secondary dialogs do not repeat the app icon (CY Desktop Visual Guide §11.1)
        ShowInTaskbar = false;
        Font = new Font("Microsoft JhengHei UI", 9.5F);
        ClientSize = new Size(560, 250);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(16, 14, 16, 10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(Label("CY Web 網址"), 0, 0);
        _url.Text = settings.BaseUrl;
        _url.PlaceholderText = "https://…";
        root.Controls.Add(_url, 1, 0);

        root.Controls.Add(Label("連線 Token"), 0, 1);
        _token.PlaceholderText = settings.HasToken ? "已設定（留空表示不變更）" : "貼上 CY Web 超級管理員產生的 Token";
        root.Controls.Add(_token, 1, 1);

        var clear = new CyButton { Text = "清除 Token", Width = 110, Height = 30, Enabled = settings.HasToken };
        clear.Click += (_, _) =>
        {
            _clearToken = true;
            _token.Text = string.Empty;
            _token.PlaceholderText = "儲存後清除";
            clear.Enabled = false;
        };
        root.Controls.Add(clear, 1, 2);

        _enabled.Checked = settings.Enabled;
        root.Controls.Add(_enabled, 1, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var ok = new CyPrimaryButton { Text = "儲存", Width = 92, Height = 34 };
        var cancel = new CyButton { Text = "取消", Width = 92, Height = 34, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Save();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 4);
        root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
        CyVisualTheme.Apply(this);
    }

    private static Label Label(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left };

    private void Save()
    {
        var url = _url.Text.Trim();
        if (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)))
        {
            MessageBox.Show(this, "CY Web 網址必須是 https:// 開頭的完整網址。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var token = _token.Text.Trim();
        if (token.Length > 0 && (token.Length < 32 || token.Any(char.IsWhiteSpace)))
        {
            MessageBox.Show(this, "Token 格式不正確，請重新從 CY Web 複製。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _settings.BaseUrl = url;
        if (_clearToken) _settings.ProtectedToken = string.Empty;
        if (token.Length > 0) _settings.SetToken(token);
        _settings.Enabled = _enabled.Checked;
        if (_settings.Enabled && !_settings.IsComplete)
        {
            MessageBox.Show(this, "要啟用 Web 查詢，請填入網址與 Token。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DialogResult = DialogResult.OK;
    }
}
