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
    private const int WindowWidth = 1000;
    private const int WindowHeight = 660;
    private readonly InvoiceRecord? original;
    private readonly FixedDecimal? remaining;
    private readonly TextBox reason = UiControls.TextBox(100);
    private readonly TextBox employeeNo = UiControls.TextBox(4);
    private readonly TextBox password = UiControls.TextBox(128);
    private readonly CheckBox grossPrices = new() {
        Text="以含稅單價輸入",AutoSize=true
    };
    private readonly DataGridView products = new() {
        Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,
        RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,
        MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
        EditMode=DataGridViewEditMode.EditOnEnter
    };
    private readonly Label totals = new() {
        Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,
        ForeColor=Color.Black,AutoEllipsis=true
    };
    private readonly Label balance = new() {
        Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
        AutoEllipsis=true
    };
    private readonly Button add = UiControls.StandardButton("＋ 新增品項");
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
        MinimumSize=new Size(820,560);
        FormBorderStyle=FormBorderStyle.Sizable;
        MaximizeBox=true;
        ShowInTaskbar=false;
        Font=new Font("Microsoft JhengHei UI",10F);
        Icon=ApplicationIcon.Load();
        reason.TextAlign=HorizontalAlignment.Left;
        employeeNo.TextAlign=HorizontalAlignment.Left;
        password.TextAlign=HorizontalAlignment.Left;
        password.UseSystemPasswordChar=true;
        BuildLayout();
        if(invoice is not null)LoadOriginal(invoice);
        grossPrices.CheckedChanged+=(_,_)=>ConvertPrices();
        products.CellEndEdit+=(_,_)=>UpdateTotals();
        products.CellValueChanged+=(_,_)=>UpdateTotals();
        products.CurrentCellDirtyStateChanged+=(_,_)=>{
            if(products.IsCurrentCellDirty)products.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        products.DataError+=(_,eventArgs)=>{eventArgs.ThrowException=false;};
        add.Click+=(_,_)=>AddProduct("新商品","1","0","1");
        Shown+=(_,_)=>reason.Focus();
    }

    public string Reason=>reason.Text.Trim();
    public long TaxInclusiveAmount {get;private set;}
    public IReadOnlyList<AllowanceProductItem> EditedItems {get;private set;}=[];
    public string EmployeeNo=>employeeNo.Text.Trim();
    public string Password=>password.Text;

    private void BuildLayout()
    {
        products.Columns.Add(new DataGridViewTextBoxColumn {
            Name="description",HeaderText="品名",FillWeight=240
        });
        products.Columns.Add(new DataGridViewTextBoxColumn {
            Name="quantity",HeaderText="數量",FillWeight=65
        });
        products.Columns.Add(new DataGridViewTextBoxColumn {
            Name="unit_price",HeaderText="單價",FillWeight=100
        });
        var taxType=new DataGridViewComboBoxColumn {
            Name="tax_type",HeaderText="課稅別",FillWeight=80,FlatStyle=FlatStyle.Flat
        };
        taxType.Items.AddRange("應稅","零稅率","免稅");
        products.Columns.Add(taxType);
        products.Columns.Add(new DataGridViewTextBoxColumn {
            Name="amount",HeaderText="未稅金額",ReadOnly=true,FillWeight=100
        });
        products.Columns.Add(new DataGridViewTextBoxColumn {
            Name="tax",HeaderText="稅額",ReadOnly=true,FillWeight=75
        });
        products.Columns.Add(new DataGridViewButtonColumn {
            Name="delete",HeaderText="操作",Text="刪除",UseColumnTextForButtonValue=true,
            FillWeight=65
        });
        products.CellContentClick+=(_,e)=>{
            if(e.RowIndex>=0&&e.ColumnIndex==products.Columns["delete"]!.Index){
                products.Rows.RemoveAt(e.RowIndex);UpdateTotals();
            }
        };
        var root=new TableLayoutPanel {
            Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,
            Padding=new Padding(14,12,14,10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,102));
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
            Text="刪除不需要折讓的商品，必要時可自行處理折扣。金額自動重算。",
            Dock=DockStyle.Fill,ForeColor=Color.DimGray
        },0,1);
        header.SetColumnSpan(header.GetControlFromPosition(0,1)!,2);
        root.Controls.Add(header,0,0);
        root.Controls.Add(products,0,1);
        var sumRow=new TableLayoutPanel {
            Dock=DockStyle.Fill,ColumnCount=2
        };
        sumRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));
        sumRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        sumRow.Controls.Add(add,0,0);
        sumRow.Controls.Add(totals,1,0);
        root.Controls.Add(sumRow,0,2);
        var credentials=new TableLayoutPanel {
            Dock=DockStyle.Fill,ColumnCount=4,RowCount=2
        };
        for(var i=0;i<4;i++)credentials.ColumnStyles.Add(
            new ColumnStyle(i%2==0?SizeType.Absolute:SizeType.Percent,
                i%2==0?80:50));
        for(var i=0;i<2;i++)credentials.RowStyles.Add(
            new RowStyle(SizeType.Percent,50));
        Credentials(credentials,"折讓原因",reason,0,0);
        Credentials(credentials,"員工編號",employeeNo,2,0);
        Credentials(credentials,"員工密碼",password,0,1);
        root.Controls.Add(credentials,0,3);
        var buttons=new FlowLayoutPanel {
            Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,
            WrapContents=false
        };
        buttons.Controls.Add(submit);
        buttons.Controls.Add(cancel);
        cancel.DialogResult=DialogResult.Cancel;
        submit.Click+=(_,_)=>Submit();
        root.Controls.Add(buttons,0,4);
        Controls.Add(root);
        CancelButton=cancel;
    }

    private static void Credentials(TableLayoutPanel table,string name,
        Control control,int column,int row)
    {
        table.Controls.Add(new Label {
            Text=name,Dock=DockStyle.Fill,
            TextAlign=ContentAlignment.MiddleLeft
        },column,row);
        control.Dock=DockStyle.Fill;
        table.Controls.Add(control,column+1,row);
    }

    private void LoadOriginal(InvoiceRecord invoice)
    {
        // invoice_query has already refreshed these official items before the
        // detail window is shown. Leave discounts as editable negative lines.
        grossPrices.Checked=invoice.DetailVat==1;
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
        if(products.Rows.Count>=50){
            MessageBox.Show(this,"折讓明細最多 50 筆。","無法新增");
            return;
        }
        var kind=taxKind switch {"2"=>"零稅率","3"=>"免稅",_=>"應稅"};
        products.Rows.Add(name,quantity,price,kind,"","","");
        UpdateTotals();
    }

    private void ConvertPrices()
    {
        if(calculating)return;
        calculating=true;
        try{
            foreach(DataGridViewRow row in products.Rows){
                try{
                    var kind=Convert.ToString(row.Cells["tax_type"].Value);
                    if(kind!="應稅")continue;
                    var price=FixedDecimal.Parse(
                        Convert.ToString(row.Cells["unit_price"].Value)??"");
                    var converted=FixedDecimal.MultiplyRatio(price,
                        grossPrices.Checked?105:100,grossPrices.Checked?100:105);
                    row.Cells["unit_price"].Value=converted.ToString();
                }catch(Exception error)when(error is FormatException or OverflowException){
                    totals.Text="有單價尚未完成輸入，請檢查。";
                }
            }
        }finally{calculating=false;}
        UpdateTotals();
    }

    private (IReadOnlyList<AllowanceProductItem> Lines,FixedDecimal Net,
        FixedDecimal Tax,FixedDecimal Gross) Calculate()
    {
        if(original is null)throw new InvalidOperationException("缺少原發票資料");
        if(products.Rows.Count==0)throw new InvalidOperationException("請至少留下 1 個折讓品項");
        var origDate=original.InvoiceDate.Replace("/","").Replace("-","");
        if(!int.TryParse(origDate,NumberStyles.None,CultureInfo.InvariantCulture,
            out var date)||origDate.Length!=8)
            throw new InvalidOperationException("原發票日期無法核對");
        var items=new List<AllowanceProductItem>();
        var net=new FixedDecimal(0);
        var tax=new FixedDecimal(0);
        foreach(DataGridViewRow row in products.Rows){
            var description=(Convert.ToString(row.Cells["description"].Value)??"").Trim();
            if(description.Length==0||description.Length>256)
                throw new InvalidOperationException("商品名稱不可空白或超過 256 字");
            var quantity=FixedDecimal.Parse(Convert.ToString(row.Cells["quantity"].Value)??"");
            var price=FixedDecimal.Parse(Convert.ToString(row.Cells["unit_price"].Value)??"");
            if(quantity.ScaledValue<=0)
                throw new InvalidOperationException("數量必須大於 0");
            var kind=Convert.ToString(row.Cells["tax_type"].Value);
            var taxType=kind switch {"零稅率"=>2,"免稅"=>3,"應稅"=>1,
                _=>throw new InvalidOperationException("請選擇課稅別")};
            var netPrice=grossPrices.Checked&&taxType==1
                ?FixedDecimal.MultiplyRatio(price,100,105):price;
            var rawNet=FixedDecimal.Multiply(quantity,netPrice);
            var itemNet=FixedDecimal.FromInt64(rawNet.RoundInt64());
            var itemTax=taxType==1
                ?FixedDecimal.FromInt64(FixedDecimal.MultiplyRatio(itemNet,5,100).RoundInt64())
                :new FixedDecimal(0);
            row.Cells["amount"].Value=itemNet.ToString();
            row.Cells["tax"].Value=itemTax.ToString();
            net=FixedDecimal.Add(net,itemNet);
            tax=FixedDecimal.Add(tax,itemTax);
            items.Add(new AllowanceProductItem {
                OriginalInvoiceNumber=original.InvoiceNumber,
                OriginalInvoiceDate=date,OriginalDescription=description,
                Quantity=decimal.Parse(quantity.ToString(),CultureInfo.InvariantCulture),
                UnitPrice=decimal.Parse(netPrice.ToString(),CultureInfo.InvariantCulture),
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
        catch(Exception error)when(error is InvalidOperationException or
            FormatException or OverflowException){
            totals.ForeColor=Color.Firebrick;
            totals.Text="請完成商品數量、單價與課稅別後再送出";
        }
        finally{calculating=false;}
    }

    private void Submit()
    {
        products.EndEdit();
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
        catch(Exception error)when(error is InvalidOperationException or
            FormatException or OverflowException){
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

    internal void VerifySmokeLayout()
    {
        if(Text!="折讓申請"||ClientSize.Width!=WindowWidth||
           ClientSize.Height!=WindowHeight||CancelButton!=cancel||
           !password.UseSystemPasswordChar||products.Columns.Count!=7)
            throw new InvalidOperationException("折讓編輯視窗基本配置不正確");
    }
}
