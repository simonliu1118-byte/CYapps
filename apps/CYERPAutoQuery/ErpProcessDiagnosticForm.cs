namespace CYERPAutoQuery;

/// <summary>Read-only, opt-in process diagnosis. Does not interact with the ERP automation service.</summary>
internal sealed class ErpProcessDiagnosticForm : Form
{
    private readonly string _configuredExe;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, RowHeadersVisible = false
    };
    private readonly Label _status = new() { AutoSize = true, Text = "尚未掃描。" };
    private readonly List<ErpProcessChange> _changes = [];
    private ErpProcessSnapshot? _last;
    private int _samples;

    public ErpProcessDiagnosticForm(string configuredExe)
    {
        _configuredExe = configuredExe;
        Text = "ERP 行程診斷（唯讀）";
        Font = new Font("Microsoft JhengHei UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(920, 540);
        MinimumSize = new Size(700, 400);
        ShowIcon = false;
        ShowInTaskbar = false;

        _grid.Columns.Add("name", "行程");
        _grid.Columns.Add("pid", "PID");
        _grid.Columns.Add("parent", "父 PID");
        _grid.Columns.Add("session", "工作階段");
        _grid.Columns.Add("started", "啟動時間");
        _grid.Columns.Add("memory", "記憶體 MiB");
        _grid.Columns.Add("windows", "ERP 視窗類型");
        _grid.Columns["pid"]!.FillWeight = 35;
        _grid.Columns["parent"]!.FillWeight = 42;
        _grid.Columns["session"]!.FillWeight = 48;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(12, 8, 8, 3) };
        var scan = new Button { Text = "立即掃描", AutoSize = true };
        var watch = new CheckBox { Text = "每 2 秒觀察變化", AutoSize = true, Checked = false, Margin = new Padding(16, 7, 8, 0) };
        var export = new Button { Text = "匯出遮蔽報告", AutoSize = true };
        scan.Click += (_, _) => Scan();
        watch.CheckedChanged += (_, _) => { _timer.Enabled = watch.Checked; };
        export.Click += (_, _) => Export();
        bar.Controls.Add(scan);
        bar.Controls.Add(watch);
        bar.Controls.Add(export);

        var footer = new Panel { Height = 59, Dock = DockStyle.Bottom, Padding = new Padding(12, 5, 12, 5) };
        var note = new Label
        {
            Dock = DockStyle.Top, Height = 23,
            Text = "只讀取本機工作階段可存取的行程資訊；停止／重啟 ERP 請由人員自行操作。本工具不會終止行程。",
            ForeColor = SystemColors.GrayText
        };
        _status.Dock = DockStyle.Bottom;
        footer.Controls.Add(note);
        footer.Controls.Add(_status);
        Controls.Add(_grid);
        Controls.Add(footer);
        Controls.Add(bar);

        _timer.Tick += (_, _) => Scan();
        Shown += (_, _) => Scan();
        FormClosed += (_, _) => _timer.Dispose();
    }

    private void Scan()
    {
        try
        {
            var snapshot = ErpProcessDiagnosticScanner.Scan(_configuredExe);
            if (_last is not null) _changes.AddRange(ErpProcessDiagnosticReport.Compare(_last, snapshot));
            _last = snapshot;
            _samples++;
            _grid.Rows.Clear();
            foreach (var p in snapshot.Processes.OrderBy(p => p.SessionId).ThenBy(p => p.Pid))
                _grid.Rows.Add(p.ProcessName, p.Pid, p.ParentPid?.ToString() ?? "?",
                    p.SessionId?.ToString() ?? "?", p.StartedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "?",
                    p.WorkingSetBytes is { } m ? (m / 1048576d).ToString("F1") : "?",
                    string.Join("、", p.WindowKinds));
            _status.Text = $"掃描 {_samples} 次；目前 {snapshot.Processes.Count} 個候選行程；觀察變化 {_changes.Count} 筆。";
        }
        catch (Exception ex)
        {
            _timer.Stop();
            _status.Text = "掃描失敗：" + ex.GetType().Name;
            MessageBox.Show(this, "無法取得 ERP 行程資訊：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Export()
    {
        if (_last is null) { Scan(); if (_last is null) return; }
        using var dialog = new SaveFileDialog
        {
            Title = "匯出 ERP 行程診斷（已遮蔽）",
            Filter = "文字報告 (*.txt)|*.txt",
            FileName = "CYERPAutoQuery_ProcessDiagnostic_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, ErpProcessDiagnosticReport.Format(_last, _changes, _samples));
            MessageBox.Show(this, "已儲存遮蔽報告。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "匯出失敗：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
