namespace CYInvoice.WinForms;

internal static class RoundedButtonSmokeTests
{
    internal static void Verify()
    {
        using var form = new DialogKeyForm { ClientSize = new Size(420, 160), ShowInTaskbar = false };
        using var button = new RoundedButton { Text = "確認", Bounds = new Rectangle(12, 12, 100, 34) };
        using var cancel = new RoundedButton { Text = "取消", Bounds = new Rectangle(124, 12, 100, 34) };
        form.Controls.Add(button);
        form.Controls.Add(cancel);
        form.AcceptButton = button;
        form.CancelButton = cancel;
        var clicks = 0;
        var cancels = 0;
        button.Click += (_, _) => clicks++;
        cancel.Click += (_, _) => cancels++;
        form.Show();
        button.Focus();
        Application.DoEvents();
        if (!form.Dispatch(Keys.Enter) || clicks != 1 || !form.Dispatch(Keys.Escape) || cancels != 1)
            throw new InvalidOperationException("圓角按鈕未保留原生 Enter / Escape 行為");
        button.Enabled = false;
        button.PerformClick();
        if (clicks != 1) throw new InvalidOperationException("停用圓角按鈕仍執行操作");

        using var image = new Bitmap(24, 24);
        using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.Magenta);
        button.Enabled = true;
        button.Image = image;
        button.ImageAlign = ContentAlignment.TopCenter;
        button.TextAlign = ContentAlignment.BottomCenter;
        button.TextImageRelation = TextImageRelation.ImageAboveText;
        button.Size = new Size(100, 70);
        using var bitmap = new Bitmap(button.Width, button.Height);
        button.DrawToBitmap(bitmap, button.ClientRectangle);
        if (bitmap.GetPixel(button.Width / 2, 10).ToArgb() != Color.Magenta.ToArgb())
            throw new InvalidOperationException("圓角按鈕未保留圖片內容");
        if (bitmap.GetPixel(0, 0).ToArgb() != form.BackColor.ToArgb())
            throw new InvalidOperationException("圓角按鈕角落未顯示父容器背景");
    }

    private sealed class DialogKeyForm : Form
    {
        internal bool Dispatch(Keys key) => ProcessDialogKey(key);
    }
}
