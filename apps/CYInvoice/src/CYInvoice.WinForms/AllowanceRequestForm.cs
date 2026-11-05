using System.Globalization;
using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Invoicing;

namespace CYInvoice.WinForms;

// One editor for the original invoice's products. The employee may remove
// products or change quantities/prices; only the gross remaining cap limits
// the refund, not any individual original product allocation.
internal sealed class AllowanceRequestForm : Form
{
    private const int WindowWidth = 700;
    private const int WindowHeight = 650;
    private readonly InvoiceRecord? original;
    private readonly FixedDecimal? remaining;
    private readonly TextBox reason = UiControls.TextBox(100);
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(128);
    private readonly CheckBox grossPrices = new() {
        Text = "以含稅單價輸入", AutoSize = true
    };
    private readonly NativeListViewHost historyHost = new(10F, 27);
    private readonly NativeListViewHost productsHost = new(10F, 27);
    private readonly NativeListCellEditor productEditor;
    private ListView officialHistory => historyHost.List;
    private ListView products => productsHost.List;
    private readonly Label totals = new() {
        Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,
        ForeColor=Color.Black,AutoEllipsis=true
    };
    private readonly Label balance = new() {
        Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
        AutoEllipsis=true
    };
    private readonly Button reset = UiControls.StandardButton("重置");
    private readonly Button submit = UiControls.StandardButton("提出申請");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private bool calculating;

    public AllowanceRequestForm(InvoiceRecord? invoice = null)
    {
        original=invoice;
        if(invoice is not null)remaining=InvoiceAllowanceMetadata.RemainingGross(invoice);
        Text="折讓申請";
        StartPosition=FormStartPosition.CenterParent;
        ClientSize=new Size(WindowWidth,WindowHeight);
        MinimumSize=new Size(680,600);
        FormBorderStyle=FormBorderStyle.Sizable;
        MaximizeBox=true;
        ShowInTaskbar=false;
        Font=new Font("Microsoft JhengHei UI",10F);
        ShowIcon=false;
        reason.Multiline=true; reason.ScrollBars=ScrollBars.Vertical;
        reason.TextAlign=HorizontalAlignment.Left;
        employeeNo.TextAlign=HorizontalAlignment.Left;
        password.TextAlign=HorizontalAlignment.Left;
        password.UseSystemPasswordChar=true;
        productEditor = new NativeListCellEditor(products);
        productEditor.Committed += (_, _) => UpdateTotals();
        productEditor.NavigationRequested += (row, column, _) => BeginInvoke((Action)(() => {
            if (column < 2) BeginProductEdit(row, column + 1);
            else if (row + 1 < products.Items.Count) BeginProductEdit(row + 1, 0);
            else reason.Focus();
        }));
        ConfigureTables();
        foreach (var item in invoice is null ? [] : InvoiceAllowanceMetadata.ReadOfficial(invoice))
            officialHistory.Items.Add(new ListViewItem([item.AllowanceNumber, InvoiceOperationHistoryControl.FormatAllowanceDate(item.AllowanceDate), item.TotalAmount, item.TaxAmount,
                InvoiceOperationHistoryControl.InclusiveAmount(item), item.CancelDate > 0 ? "已作廢" : item.InvoiceStatus == UploadStatuses.Complete ? "已完成" : "待核對"]));
        BuildLayout();
        if(invoice is not null)LoadOriginal(invoice);
        grossPrices.CheckedChanged += (_, _) => ConvertPrices();
        reset.Click+=(_,_)=>ResetProducts();
        Shown+=(_,_)=>reason.Focus();
    }

    public string Reason=>reason.Text.Trim().ReplaceLineEndings(" ");
    public long TaxInclusiveAmount {get;private set;}
    public IReadOnlyList<AllowanceProductItem> EditedItems {get;private set;}=[];
    public string EmployeeNo=>employeeNo.Text.Trim();
    public string Password=>password.Text;

    private void ConfigureTables()
    {
        foreach (var list in new[] { officialHistory, products })
        {
            list.GridLines = true;
            list.Sorting = SortOrder.None;
            list.ShowItemToolTips = true;
        }
        foreach (var label in new[] { "折讓單號", "日期", "未稅金額", "稅額", "含稅金額", "狀態" })
            officialHistory.Columns.Add(label);
        for (var i = 2; i < 5; i++) officialHistory.Columns[i].TextAlign = HorizontalAlignment.Right;
        historyHost.ConfigureFillColumns(0, [132, 96, 96, 72, 100, 100]);
        var names = new[] { "description", "quantity", "unit_price", "tax_type", "amount", "tax", "delete" };
        var labels = new[] { "品名", "數量", "未稅單價", "課稅別", "未稅金額", "稅額", "操作" };
        for (var i = 0; i < names.Length; i++)
            products.Columns.Add(new ColumnHeader { Name = names[i], Text = labels[i], TextAlign = i is 1 or 2 or 4 or 5 ? HorizontalAlignment.Right : HorizontalAlignment.Left });
        productsHost.ConfigureFillColumns(0, [150, 76, 132, 66, 96, 72, 48]);
        productsHost.ConfigureRowAction(6, "刪除", row => {
            productEditor.Commit(false);
            products.Items.Remove(row);
            UpdateTotals();
        });
        products.MouseUp += (_, e) => {
            if (e.Button != MouseButtons.Left) return;
            var hit = products.HitTest(e.Location);
            var column = hit.Item is not null && hit.SubItem is not null ? hit.Item.SubItems.IndexOf(hit.SubItem) : -1;
            if (hit.Item is not null && column is >= 0 and <= 2) BeginProductEdit(hit.Item.Index, column);
            else productEditor.Commit(false);
        };
        products.KeyDown += (_, e) => {
            if (e.KeyCode != Keys.Enter || products.FocusedItem is null) return;
            e.Handled = e.SuppressKeyPress = true;
            BeginProductEdit(products.FocusedItem.Index, 0);
        };
    }

    private void BeginProductEdit(int row, int column)
    {
        productEditor.Commit(false);
        if (row < 0 || row >= products.Items.Count || column is < 0 or > 2) return;
        productEditor.Begin(row, column, products.Items[row].SubItems[column].Text, column == 0 ? 256 : 64);
    }

    private void BuildLayout()
    {
        var root=new TableLayoutPanel {
            Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,
            Padding=new Padding(14,12,14,10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,106));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
        var header=new TableLayoutPanel {
            Dock=DockStyle.Fill,ColumnCount=2,RowCount=2
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,75));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
        balance.Text=remaining is null
            ?"請選擇要折讓的商品並修改數量或單價。"
            :$"原發票 {original!.Amount:N0} 元｜剩餘可折讓 {remaining.Value} 元";
        header.Controls.Add(balance,0,0);
        header.Controls.Add(grossPrices,1,0);
        header.Controls.Add(new Label {
            Text="可切換含稅／未稅單價，負單價可作折扣；程式自動換算未稅金額、稅額與總額。",
            Dock=DockStyle.Fill,ForeColor=Color.DimGray
        },0,1);
        header.SetColumnSpan(header.GetControlFromPosition(0,1)!,2);
        root.Controls.Add(header,0,0);
        root.Controls.Add(historyHost,0,1);
        root.Controls.Add(productsHost,0,2);
        var sumRow=new TableLayoutPanel {
            Dock=DockStyle.Fill,ColumnCount=2
        };
        sumRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));
        sumRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        sumRow.Controls.Add(reset,0,0);
        sumRow.Controls.Add(totals,1,0);
        root.Controls.Add(sumRow,0,3);
        var credentials=new TableLayoutPanel {
            Dock=DockStyle.Fill,ColumnCount=4,RowCount=2
        };
        for(var i=0;i<4;i++)credentials.ColumnStyles.Add(
            new ColumnStyle(i%2==0?SizeType.Absolute:SizeType.Percent,
                i%2==0?80:50));
        for(var i=0;i<2;i++)credentials.RowStyles.Add(
            new RowStyle(SizeType.Percent,50));
        Credentials(credentials,"折讓原因",reason,0,0);
        credentials.SetColumnSpan(reason,3);
        reason.Dock=DockStyle.Fill;
        Credentials(credentials,"員工編號",employeeNo,0,1);
        Credentials(credentials,"員工密碼",password,2,1);
        employeeNo.Anchor=AnchorStyles.Left; employeeNo.Width=90; password.Anchor=AnchorStyles.Left; password.Width=180;
        root.Controls.Add(credentials,0,4);
        var buttons=new FlowLayoutPanel {
            Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,
            WrapContents=false
        };
        buttons.Controls.Add(submit);
        buttons.Controls.Add(cancel);
        cancel.DialogResult=DialogResult.Cancel;
        submit.Click+=(_,_)=>Submit();
        root.Controls.Add(buttons,0,5);
        Controls.Add(root);
        CancelButton=cancel;
    }

    private static void Credentials(TableLayoutPanel table,string name,
        Control control,int column,int row)
    {
        table.Controls.Add(new Label {
            Text=name,Dock=DockStyle.Fill,Margin=new Padding(0,0,4,0),
            TextAlign=ContentAlignment.MiddleLeft
        },column,row);
        control.Dock=DockStyle.None;
        control.Anchor=AnchorStyles.Left|AnchorStyles.Right;
        control.Margin=new Padding(3,0,8,0);
        table.Controls.Add(control,column+1,row);
    }

    private void ResetProducts()
    {
        if (original is null) return;
        productEditor.Commit(true);
        calculating=true;
        try { products.Items.Clear(); LoadOriginal(original); }
        finally { calculating=false; }
        UpdateTotals();
    }

    private void LoadOriginal(InvoiceRecord invoice)
    {
        // Use the invoice's original input convention. Source DetailVat=1
        // prices are already gross, so do not silently convert on first load.
        grossPrices.Checked = invoice.DetailVat == 1;
        products.Columns["unit_price"]!.Text =
            grossPrices.Checked ? "含稅單價" : "未稅單價";
        foreach(var item in invoice.Items){
            var qty=item.QuantityDecimal.Length>0
                ?item.QuantityDecimal:item.Quantity.ToString(CultureInfo.InvariantCulture);
            var price=item.UnitPriceDecimal.Length>0
                ?item.UnitPriceDecimal:item.UnitPrice.ToString(CultureInfo.InvariantCulture);
            AddProduct(item.Description,qty,price,item.TaxType);
        }
        UpdateTotals();
    }

    private void AddProduct(string name,string quantity,string price,string taxKind)
    {
        if(products.Items.Count>=50){
            MessageBox.Show(this,"折讓明細最多 50 筆。","無法新增");
            return;
        }
        if (taxKind is not ("" or "1"))
            throw new InvalidOperationException("目前折讓申請僅支援應稅商品，不變更原發票稅別。");
        var kind="應稅";
        products.Items.Add(new ListViewItem([name,quantity,price,kind,"","","刪除"]));
        UpdateTotals();
    }

    private void ConvertPrices()
    {
        // Complete editing before switching. The visible taxable unit prices
        // change convention; zero-rated/exempt unit prices are unchanged.
        productEditor.Commit(false);
        if (calculating) return;
        calculating = true;
        try
        {
            foreach (ListViewItem row in products.Items)
            {
                if (Convert.ToString(row.SubItems[3].Text) != "應稅")
                    continue;
                var value = Convert.ToString(row.SubItems[2].Text) ?? "";
                try
                {
                    var price = FixedDecimal.Parse(value);
                    row.SubItems[2].Text = FixedDecimal.MultiplyRatio(
                        price, grossPrices.Checked ? 105 : 100,
                        grossPrices.Checked ? 100 : 105).ToString();
                }
                catch (Exception error) when (error is FormatException or OverflowException)
        {
            CYInvoice.Core.Storage.IssueDiagnosticLog.RecordProblem(error,"AllowanceRequestForm");
                    // Keep invalid text for user correction; submitting will
                    // fail validation rather than quietly resetting the price.
                }
            }
            products.Columns["unit_price"]!.Text =
                grossPrices.Checked ? "含稅單價" : "未稅單價";
        }
        finally { calculating = false; }
        UpdateTotals();
    }

    private (IReadOnlyList<AllowanceProductItem> Lines,FixedDecimal Net,
        FixedDecimal Tax,FixedDecimal Gross) Calculate()
    {
        if(original is null)throw new InvalidOperationException("缺少原發票資料");
        if(products.Items.Count==0)throw new InvalidOperationException("請至少留下 1 個折讓品項");
        var origDate=original.InvoiceDate.Replace("/","").Replace("-","");
        if(!int.TryParse(origDate,NumberStyles.None,CultureInfo.InvariantCulture,
            out var date)||origDate.Length!=8)
            throw new InvalidOperationException("原發票日期無法核對");
        var items=new List<AllowanceProductItem>();
        var net=new FixedDecimal(0);
        var tax=new FixedDecimal(0);
        foreach(ListViewItem row in products.Items){
            var description=(Convert.ToString(row.SubItems[0].Text)??"").Trim();
            if(description.Length==0||description.Length>256)
                throw new InvalidOperationException("商品名稱不可空白或超過 256 字");
            var quantity=FixedDecimal.Parse(Convert.ToString(row.SubItems[1].Text)??"");
            var price=FixedDecimal.Parse(Convert.ToString(row.SubItems[2].Text)??"");
            if(quantity.ScaledValue<=0)
                throw new InvalidOperationException("數量必須大於 0");
            var kind=Convert.ToString(row.SubItems[3].Text);
            var taxType=kind switch {"零稅率"=>2,"免稅"=>3,"應稅"=>1,
                _=>throw new InvalidOperationException("請選擇課稅別")};
            // Gross-entry mode: keep the exact typed gross unit price so the
            // Core validator can reproduce the gross-first rounding. DTOs
            // still carry a net unit price and net/tax amounts to AMEGO.
            var grossMode = grossPrices.Checked && taxType == 1;
            var netPrice = grossMode
                ? FixedDecimal.MultiplyRatio(price, 100, 105) : price;
            var lineGross = grossMode
                ? FixedDecimal.FromInt64(
                    FixedDecimal.Multiply(quantity, price).RoundInt64())
                : new FixedDecimal(0);
            var itemNet = grossMode
                ? FixedDecimal.FromInt64(
                    FixedDecimal.MultiplyRatio(lineGross, 100, 105).RoundInt64())
                : FixedDecimal.FromInt64(
                    FixedDecimal.Multiply(quantity, netPrice).RoundInt64());
            var itemTax = taxType != 1 ? new FixedDecimal(0) :
                grossMode
                    ? FixedDecimal.Add(lineGross,
                        new FixedDecimal(checked(-itemNet.ScaledValue)))
                    : FixedDecimal.FromInt64(
                        FixedDecimal.MultiplyRatio(itemNet, 5, 100).RoundInt64());
            row.SubItems[4].Text=itemNet.ToString();
            row.SubItems[5].Text=itemTax.ToString();
            net=FixedDecimal.Add(net,itemNet);
            tax=FixedDecimal.Add(tax,itemTax);
            items.Add(new AllowanceProductItem {
                OriginalInvoiceNumber=original.InvoiceNumber,
                OriginalInvoiceDate=date,OriginalDescription=description,
                Quantity=decimal.Parse(quantity.ToString(),CultureInfo.InvariantCulture),
                UnitPrice=decimal.Parse(netPrice.ToString(),CultureInfo.InvariantCulture),
                GrossUnitPrice=grossMode
                    ?decimal.Parse(price.ToString(),CultureInfo.InvariantCulture)
                    :null,
                Amount=decimal.Parse(itemNet.ToString(),CultureInfo.InvariantCulture),
                Tax=decimal.Parse(itemTax.ToString(),CultureInfo.InvariantCulture),
                TaxType=taxType
            });
        }
        return(items,net,tax,FixedDecimal.Add(net,tax));
    }

    private void UpdateTotals()
    {
        if(calculating)return;
        calculating=true;
        try{
            var sum=Calculate();
            var tooMuch=remaining is not null&&
                sum.Gross.ScaledValue>remaining.Value.ScaledValue;
            totals.ForeColor=tooMuch?Color.Firebrick:Color.Black;
            totals.Text=$"未稅 {sum.Net}｜稅 {sum.Tax}｜折讓含稅 {sum.Gross} 元"+
                (tooMuch?"（超過剩餘上限）":"");
        }
        catch (Exception error)when(error is InvalidOperationException or
            FormatException or OverflowException)
        {
            CYInvoice.Core.Storage.IssueDiagnosticLog.RecordProblem(error,"AllowanceRequestForm");
            totals.ForeColor=Color.Firebrick;
            totals.Text="請完成商品數量、單價與課稅別後再送出";
        }
        finally{calculating=false;}
    }

    private void Submit()
    {
        productEditor.Commit(false);
        if(Reason.Length==0){
            ValidationError("請輸入折讓原因。",reason);
            return;
        }
        try {
            var sum=Calculate();
            if(sum.Gross.ScaledValue<=0||
               sum.Gross.ScaledValue%FixedDecimal.Scale!=0)
                throw new InvalidOperationException(
                    "本次含稅折讓總額必須是大於 0 的整數元");
            if(remaining is not null&&
               sum.Gross.ScaledValue>remaining.Value.ScaledValue)
                throw new InvalidOperationException(
                    $"本次折讓不可超過剩餘可折讓 {remaining.Value} 元");
            if(EmployeeNo.Length==0){
                ValidationError("請輸入員工編號。",employeeNo);return;
            }
            if(password.Text.Length==0){
                ValidationError("請輸入員工密碼。",password);return;
            }
            TaxInclusiveAmount=sum.Gross.RoundInt64();
            EditedItems=sum.Lines;
            DialogResult=DialogResult.OK;
            Close();
        }
        catch (Exception error)when(error is InvalidOperationException or
            FormatException or OverflowException)
        {
            CYInvoice.Core.Storage.IssueDiagnosticLog.RecordProblem(error,"AllowanceRequestForm");
            MessageBox.Show(this,error.Message,"折讓明細未完成",
                MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }

    private void ValidationError(string message,Control control)
    {
        MessageBox.Show(this,message,"資料未完成",
            MessageBoxButtons.OK,MessageBoxIcon.Warning);
        control.Focus();
    }

    internal void VerifySmokeOriginalInvoiceEditing()
    {
        if (ShowIcon || productsHost.HeaderClicksEnabled || historyHost.HeaderClicksEnabled || reset.Text != "重置")
            throw new InvalidOperationException("折讓申請必須無圖示、固定應稅且提供重置。 ");
        if (original is null || remaining is null || products.Items.Count != 2)
            throw new InvalidOperationException("折讓視窗未帶入原發票及官方剩餘額");
        var initial = Calculate().Gross;
        products.Items.RemoveAt(1);
        ResetProducts();
        if (products.Items.Count != 2 || Calculate().Gross != initial)
            throw new InvalidOperationException("重置未還原原商品與金額。");
        products.Items.RemoveAt(1);
        products.Items[0].SubItems[2].Text = "100.1234567";
        var net = Calculate();
        if (initial.ScaledValue <= 0 || net.Gross.ToString() != "105" ||
            net.Gross.ScaledValue > remaining.Value.ScaledValue ||
            net.Lines[0].GrossUnitPrice is not null)
            throw new InvalidOperationException("未稅輸入／七位小數／額度計算失敗");
        grossPrices.Checked = true;
        var gross = Calculate();
        if (products.Columns["unit_price"]?.Text != "含稅單價" ||
            gross.Gross != net.Gross || gross.Lines[0].GrossUnitPrice is null)
            throw new InvalidOperationException("未稅轉含稅單價未同步明細與總額");
        products.Items[0].SubItems[2].Text = "105";
        gross = Calculate();
        if (gross.Gross.ToString() != "105" ||
            gross.Lines[0].UnitPrice != 100m ||
            gross.Lines[0].Amount != 100m ||
            gross.Lines[0].Tax != 5m ||
            gross.Lines[0].GrossUnitPrice != 105m)
            throw new InvalidOperationException("含稅輸入沒有正確自動換算未稅送出內容");
        grossPrices.Checked = false;
        var backToNet = Calculate();
        if (products.Columns["unit_price"]?.Text != "未稅單價" ||
            backToNet.Gross.ToString() != "105" ||
            backToNet.Lines[0].GrossUnitPrice is not null)
            throw new InvalidOperationException("含稅轉未稅單價或總額計算失敗");
    }

    internal void VerifySmokeLayout()
    {
        foreach (var host in new[] { historyHost, productsHost })
        {
            if (host.HeaderClicksEnabled || host.HorizontalScrollVisible ||
                host.List.Sorting != SortOrder.None ||
                (host.List.IsHandleCreated && Math.Abs(host.List.Columns.Cast<ColumnHeader>().Sum(column => column.Width) - host.List.ClientSize.Width) > 1))
                throw new InvalidOperationException($"折讓原生表格配置不正確：header={host.HeaderClicksEnabled}, horizontal={host.HorizontalScrollVisible}, columns={host.List.Columns.Cast<ColumnHeader>().Sum(column => column.Width)}, viewport={host.List.ClientSize.Width}。");
        }
        if (employeeNo.Parent is not TableLayoutPanel credentials || password.Parent != credentials ||
            credentials.GetRow(employeeNo) != credentials.GetRow(password) ||
            employeeNo.Bounds.Right > password.Bounds.Left || password.Bounds.Right > credentials.ClientSize.Width)
            throw new InvalidOperationException("員工編號與密碼必須同列對齊且不超出視窗。");
        if(Text!="折讓申請"||ClientSize.Width!=WindowWidth||
           ClientSize.Height!=WindowHeight||CancelButton!=cancel||
           !password.UseSystemPasswordChar||products.Columns.Count!=7||
            officialHistory.Columns.Count != 6 ||
            (products.Columns["unit_price"]?.Text != "未稅單價" &&
             products.Columns["unit_price"]?.Text != "含稅單價") ||
            grossPrices.Parent is null)
            throw new InvalidOperationException("折讓編輯視窗基本配置不正確");
    }
}
