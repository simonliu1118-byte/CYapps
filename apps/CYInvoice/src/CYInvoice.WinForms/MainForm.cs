namespace CYInvoice.WinForms;

internal sealed class MainForm : Form
{
    public MainForm()
    {
        Text = "CY 電子發票 V1.1.0-cs.1（C# 重製測試版）";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 850);
        ClientSize = new Size(1164, 811);
        Font = new Font("Microsoft JhengHei UI", 10F);

        var notice = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "C# 重製測試版\r\n並非 Go V1.1.0 的後續正式版本\r\n\r\n目前僅建立可驗證的核心與 WinForms 基礎，尚不可用於開立發票。",
            ForeColor = Color.FromArgb(0, 82, 180),
            Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
        };
        Controls.Add(notice);
    }
}
