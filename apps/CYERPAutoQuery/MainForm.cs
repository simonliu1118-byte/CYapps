namespace CYERPAutoQuery;

/// <summary>
/// Enter one 品號 and query it in SMART ERP INVI02; the result opens in <see cref="ResultForm"/>.
/// The Web entry planned next will call the same <see cref="ItemQueryService"/>.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly AppLogger _log;
    private readonly ItemQueryService _service;
    private readonly TextBox _code = new() { Width = 220, MaxLength = ItemCodeRules.MaxLength };
    private readonly CyPrimaryButton _query = new() { Text = "查詢", Width = 92, Height = 34 };
    private readonly Label _status = new() { AutoSize = true, Text = "請先在 ERP 開啟「品號資料建立作業」。查詢中按 Esc 可中止。" };
    private bool _busy;

    public MainForm(AppLogger log)
    {
        _log = log;
        _service = new ItemQueryService(log);

        Text = $"SMART ERP 自動查詢工具 {AppVersionInfo.Display}";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Font = new Font("Microsoft JhengHei UI", 9.5F);
        ClientSize = new Size(460, 150);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16, 14, 16, 10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label { Text = "品號：", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);

        var line = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _code.Margin = new Padding(0, 6, 10, 0);
        line.Controls.Add(_code);
        line.Controls.Add(_query);
        root.Controls.Add(line, 0, 1);
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);

        AcceptButton = _query;
        _query.Click += async (_, _) => await RunQueryAsync();
        Shown += (_, _) => _code.Focus();
        CyVisualTheme.Apply(this);
        _status.ForeColor = CyVisualTheme.TextSecondary;
    }

    private async Task RunQueryAsync()
    {
        if (_busy) return;
        if (!ItemCodeRules.TryNormalize(_code.Text, out var code, out var error))
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _busy = true;
        _query.Enabled = false;
        var progress = new Progress<string>(s => _status.Text = s);
        try
        {
            var result = await _service.QueryAsync(code, progress, CancellationToken.None);
            _status.Text = $"完成：{result.ItemCode}（{result.Elapsed.TotalSeconds:0.0} 秒）";
            Activate();
            using var form = new ResultForm(result);
            form.ShowDialog(this);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "已按 Esc 中止。";
            _log.Info("query", "cancelled by Esc");
            Activate();
        }
        catch (Exception ex)
        {
            _status.Text = "查詢失敗。";
            _log.Error("query", ex);
            Activate();
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
            _query.Enabled = true;
            _code.Focus();
            _code.SelectAll();
        }
    }
}
