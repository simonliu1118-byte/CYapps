using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class ConnectionBlockForm : Form
{
    private bool mayClose;
    private readonly Label message = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    public bool ExitRequested { get; private set; }

    public ConnectionBlockForm(ServiceConnectionState state, Func<Task> recheck)
    {
        Text = "服務連線中斷";
        ShowIcon = false;
        ShowInTaskbar = false;
        ControlBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 190);
        Font = new Font("Microsoft JhengHei UI", 10F);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        UpdateState(state);
        layout.Controls.Add(message, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var exit = UiControls.StandardButton("關閉程式");
        var retry = UiControls.StandardButton("重新檢查");
        exit.Click += (_, _) => { ExitRequested = true; mayClose = true; Close(); };
        retry.Click += async (_, _) => {
            retry.Enabled = false;
            try { await recheck(); }
            finally { if (!IsDisposed) retry.Enabled = true; }
        };
        buttons.Controls.Add(exit);
        buttons.Controls.Add(retry);
        layout.Controls.Add(buttons, 0, 1);
        Controls.Add(layout);
        FormClosing += (_, e) => { if (!mayClose && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
    }

    public void RestoreConnection()
    {
        mayClose = true;
        Close();
    }

    public void UpdateState(ServiceConnectionState state)
    {
        message.Text = state.CloudRejected ? "雲端已拒絕目前裝置或綁定，所有業務功能已暫停。\n\n不能使用單機備援。請由管理員確認授權後重新檢查。\n原資料與輸入內容會保留。" : state.CloudRequired
            ? "目前無法連線光貿與雲端驗證服務，所有業務功能已暫停。\n\n程式會持續檢查；任一服務恢復後即可繼續。\n原資料與輸入內容會保留。"
            : "目前無法連線光貿，所有業務功能已暫停。\n\n程式會持續檢查，光貿恢復後即可繼續。\n原資料與輸入內容會保留。";
    }

    internal static void VerifySmoke(Form owner)
    {
        foreach (var state in new[] { new ServiceConnectionState(false, false, true),
            new ServiceConnectionState(false, false, false), new ServiceConnectionState(true, false, true, CloudRejected: true) })
        {
            ConnectionBlockForm? current = null;
            var retries = 0;
            using var dialog = new ConnectionBlockForm(state, () => {
                retries++;
                current!.RestoreConnection();
                return Task.CompletedTask;
            });
            current = dialog;
            Exception? failure = null;
            dialog.Shown += (_, _) => dialog.BeginInvoke((Action)(() => {
                try
                {
                    var layout = (TableLayoutPanel)dialog.Controls[0];
                    var message = (Label)layout.GetControlFromPosition(0, 0)!;
                    var buttons = (FlowLayoutPanel)layout.GetControlFromPosition(0, 1)!;
                    var expected = state.CloudRejected ? "雲端已拒絕" : state.CloudRequired ? "光貿與雲端" : "無法連線光貿";
                    var height = TextRenderer.MeasureText(message.Text, message.Font,
                        new Size(message.ClientSize.Width, int.MaxValue), TextFormatFlags.WordBreak).Height;
                    if (!dialog.Modal || dialog.ControlBox || !message.Text.Contains(expected, StringComparison.Ordinal)
                        || height > message.ClientSize.Height || buttons.Controls.Count != 2
                        || buttons.Controls.Cast<Control>().Any(button => !UiControls.HasLogicalSize(button,
                            UiControls.StandardButtonWidth, UiControls.StandardButtonHeight)))
                        throw new InvalidOperationException("連線阻擋視窗必須為原生 modal、文字完整及共通按鈕尺寸");
                    dialog.Close();
                    if (!dialog.Visible || dialog.IsDisposed || dialog.ExitRequested)
                        throw new InvalidOperationException("關閉阻擋視窗不能略過連線檢查");
                    ((Button)buttons.Controls[1]).PerformClick();
                }
                catch (Exception error) { failure = error; dialog.RestoreConnection(); }
            }));
            dialog.ShowDialog(owner);
            if (failure is not null) throw failure;
            if (retries != 1 || dialog.ExitRequested)
                throw new InvalidOperationException("重新檢查恢復時應解除原阻擋視窗，不重啟程式");
        }

        using var exitDialog = new ConnectionBlockForm(new(false, false, true), () => Task.CompletedTask);
        exitDialog.Shown += (_, _) => exitDialog.BeginInvoke((Action)(() =>
            ((Button)((FlowLayoutPanel)((TableLayoutPanel)exitDialog.Controls[0]).GetControlFromPosition(0, 1)!).Controls[0]).PerformClick()));
        exitDialog.ShowDialog(owner);
        if (!exitDialog.ExitRequested) throw new InvalidOperationException("阻擋時仍須允許使用者關閉程式");
    }
}
