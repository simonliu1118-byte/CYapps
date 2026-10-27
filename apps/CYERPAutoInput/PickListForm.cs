using System.Drawing.Printing;

namespace CYERPAutoInput;

/// <summary>
/// 撿貨單 (user, 2026-10-27): shown when a 全自動 batch ends, before its list of results; 列印
/// prints it on the A4 printer as a table (品號／品名／數量 單位, 10 pt), both sides when the
/// printer can.
/// </summary>
internal sealed class PickListForm : Form
{
    private readonly IReadOnlyList<PickItem> _items;
    private readonly string _heading;
    private readonly string _printer;
    private readonly AppLogger _log;

    public PickListForm(PickList list, string title, string printer, AppLogger log)
    {
        _items = list.Items;
        _printer = printer;
        _log = log;
        _heading = Heading(list, title);
        Text = "撿貨單";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 640);
        MinimizeBox = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = SystemColors.Window
        };
        grid.Columns.Add("Code", "品號");
        grid.Columns.Add("Name", "品名");
        grid.Columns.Add("Quantity", "數量");
        grid.Columns["Code"]!.FillWeight = 22;
        grid.Columns["Name"]!.FillWeight = 58;
        grid.Columns["Quantity"]!.FillWeight = 20;
        grid.Columns["Quantity"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        foreach (var item in _items) grid.Rows.Add(item.Code, item.Name, Quantity(item));
        var top = new Label { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 8, 8, 0), Text = _heading };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var close = new Button { Text = "關閉", AutoSize = true, DialogResult = DialogResult.OK };
        var print = new Button { Text = "列印（A4）", AutoSize = true, Enabled = printer.Length > 0 && _items.Count > 0 };
        print.Click += (_, _) => Print();
        buttons.Controls.AddRange([close, print]);
        Controls.Add(grid);
        Controls.Add(top);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;
    }

    public static string Heading(PickList list, string title) =>
        $"{title} 撿貨單　{DateTime.Now:yyyy/MM/dd HH:mm}　{list.Orders} 張單、{list.Items.Count} 個品號";

    private static string Quantity(PickItem item) => $"{ShipSheet.Show(item.Quantity)} {item.Unit}".TrimEnd();

    private void Print()
    {
        try
        {
            var pages = PrintTable(_printer, _heading, _items);
            _log.Info("pick-list", $"printed items={_items.Count} pages={pages}");
            MessageBox.Show(this, $"撿貨單已送到 {_printer}（{pages} 頁）。", "撿貨單", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _log.Warn("pick-list", $"not printed: {ex.GetType().Name}: {ex.Message}");
            MessageBox.Show(this, $"撿貨單沒有印出：{ex.Message}", "撿貨單", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>The table on A4, 10 pt, both sides when the printer can (else one side); pages printed.</summary>
    public static int PrintTable(string printer, string heading, IReadOnlyList<PickItem> items)
    {
        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = printer;
        if (!doc.PrinterSettings.IsValid) throw new InvalidOperationException($"找不到印表機 {printer}");
        doc.PrinterSettings.Duplex = doc.PrinterSettings.CanDuplex ? Duplex.Vertical : Duplex.Simplex;
        doc.DocumentName = "撿貨單";
        doc.PrintController = new StandardPrintController();
        using var font = new Font("Microsoft JhengHei", 10, FontStyle.Regular, GraphicsUnit.Point);
        using var bold = new Font("Microsoft JhengHei", 10, FontStyle.Bold, GraphicsUnit.Point);
        using var title = new Font("Microsoft JhengHei", 12, FontStyle.Bold, GraphicsUnit.Point);
        var next = 0;
        var pages = 0;
        doc.PrintPage += (_, e) =>
        {
            pages++;
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Millimeter;
            // The margins (1/100 inch) in mm, from the printable area's corner.
            var bounds = e.MarginBounds;
            float left = (bounds.Left - e.PageSettings.HardMarginX) * 0.254f, right = (bounds.Right - e.PageSettings.HardMarginX) * 0.254f;
            float top = (bounds.Top - e.PageSettings.HardMarginY) * 0.254f, bottom = (bounds.Bottom - e.PageSettings.HardMarginY) * 0.254f;
            using var pen = new Pen(Color.Black, 0.2f);
            g.DrawString(heading, title, Brushes.Black, left, top);
            g.DrawString($"第 {pages} 頁", font, Brushes.Black, new RectangleF(left, top, right - left, 8), new StringFormat { Alignment = StringAlignment.Far });
            var y = top + 9;
            float codeW = 32, quantityW = 28;
            var rowH = font.GetHeight(g) + 2.4f;
            var nowrap = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
            var rightAligned = new StringFormat(nowrap) { Alignment = StringAlignment.Far };
            void Row(string code, string name, string quantity, Font f)
            {
                g.DrawRectangle(pen, left, y, right - left, rowH);
                g.DrawLine(pen, left + codeW, y, left + codeW, y + rowH);
                g.DrawLine(pen, right - quantityW, y, right - quantityW, y + rowH);
                g.DrawString(code, f, Brushes.Black, new RectangleF(left + 1.5f, y, codeW - 3, rowH), nowrap);
                g.DrawString(name, f, Brushes.Black, new RectangleF(left + codeW + 1.5f, y, right - quantityW - left - codeW - 3, rowH), nowrap);
                g.DrawString(quantity, f, Brushes.Black, new RectangleF(right - quantityW + 1.5f, y, quantityW - 3, rowH), rightAligned);
                y += rowH;
            }
            Row("品號", "品名", "數量", bold);
            while (next < items.Count && y + rowH <= bottom)
            {
                var item = items[next++];
                Row(item.Code, item.Name, Quantity(item), font);
            }
            e.HasMorePages = next < items.Count;
        };
        doc.Print();
        PrintWatch.Watch(doc.PrinterSettings.PrinterName, doc.DocumentName, 60);
        return pages;
    }
}
