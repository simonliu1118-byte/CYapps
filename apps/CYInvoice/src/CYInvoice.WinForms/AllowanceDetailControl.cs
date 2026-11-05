using CYInvoice.Core;

namespace CYInvoice.WinForms;

// Read-only presentation. The application/grant/ledger remain the business owners.
internal sealed class AllowanceDetailControl : UserControl
{
    private static readonly HashSet<string> OriginalFields = [
        "原發票", "原發票號碼", "發票號碼", "訂單編號", "原發票日期", "買受人", "統一編號", "使用環境", "原發票含稅金額", "目前可折讓含稅額",
    ];
    private static readonly HashSet<string> AllowanceFields = [
        "折讓單號", "折讓日期", "折讓原因", "作廢原因", "折讓作廢原因", "本次未稅金額", "本次稅額", "本次含稅折讓",
        "未稅折讓金額", "稅額", "含稅折讓總額", "原折讓未稅金額", "原折讓稅額", "最新可折含稅額", "未稅金額", "含稅總額",
    ];
    private readonly TableLayoutPanel sections = new() {
        Dock=DockStyle.Top, AutoSize=true, ColumnCount=1, Margin=Padding.Empty,
    };

    public AllowanceDetailControl(IReadOnlyList<KeyValuePair<string,string>> fields, IReadOnlyList<BusinessDetailTable> tables)
    {
        Dock=DockStyle.Fill;
        AutoScroll=true;
        BackColor=SystemColors.Control;
        Font=new Font("Microsoft JhengHei UI",10F);
        Padding=new Padding(2);
        var original=CreateSection("原發票資訊");
        AddFields(original,fields.Where(row=>OriginalFields.Contains(row.Key)&&row.Key is not ("原發票含稅金額" or "目前可折讓含稅額")));
        AddProducts(original,tables.FirstOrDefault(table=>table.Title=="原發票商品"),"原發票商品資料未取得");
        AddSummary(original,fields.Where(row=>row.Key is "原發票含稅金額" or "目前可折讓含稅額"));
        var history=tables.FirstOrDefault(table=>table.Title=="既有折讓");
        if(history?.Rows.Count>0) {
            AddRow(original,new Label { Text="既有折讓",AutoSize=true,Margin=new Padding(0,8,0,4) });
            AddProducts(original,history,"");
        }

        var current=CreateSection("本次折讓資訊");
        AddFields(current,fields.Where(row=>AllowanceFields.Contains(row.Key)&&!IsAmount(row.Key)));
        var currentTables=tables.Where(table=>table.Title is not ("原發票商品" or "既有折讓")).ToArray();
        if(currentTables.Length==0)AddProducts(current,null,"本次折讓商品資料未取得");
        foreach(var table in currentTables)AddProducts(current,table,"本次折讓商品資料未取得");
        AddSummary(current,fields.Where(row=>AllowanceFields.Contains(row.Key)&&IsAmount(row.Key)));
        AddRemaining(current,fields);

        var review=CreateSection("審核資訊");
        AddFields(review,fields.Where(row=>!OriginalFields.Contains(row.Key)&&!AllowanceFields.Contains(row.Key)));
        Controls.Add(sections);
    }

    private static bool IsAmount(string key) => key.Contains("金額",StringComparison.Ordinal)||
        key is "本次稅額" or "本次含稅折讓" or "稅額" or "含稅折讓總額" or "原折讓稅額" or "最新可折含稅額" or "含稅總額";

    private TableLayoutPanel CreateSection(string title)
    {
        var group=new GroupBox {
            Text=title,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
            Dock=DockStyle.Top,BackColor=Color.White,Padding=new Padding(12,8,12,8),
            Margin=new Padding(0,0,0,12),Tag=title,
        };
        var body=new TableLayoutPanel {
            Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=0,Margin=Padding.Empty,
        };
        group.Controls.Add(body);
        var index=sections.RowCount++;
        sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sections.Controls.Add(group,0,index);
        return body;
    }

    private static void AddRow(TableLayoutPanel section,Control control)
    {
        var index=section.RowCount++;
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
        section.Controls.Add(control,0,index);
    }

    private static string Display(string value) => string.IsNullOrWhiteSpace(value)||value is "-" or "－" ? "未取得" : value;

    private static void AddFields(TableLayoutPanel section,IEnumerable<KeyValuePair<string,string>> fields)
    {
        var rows=fields.ToArray();
        if(rows.Length==0) { AddRow(section,new Label { Text="資料未取得",AutoSize=true });return; }
        var grid=new TableLayoutPanel { AutoSize=true,Dock=DockStyle.Top,ColumnCount=4,Margin=new Padding(0,0,0,8) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        for(var i=0;i<rows.Length;i++) {
            if(i%2==0) { grid.RowCount++;grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
            var column=(i%2)*2;
            grid.Controls.Add(new Label {
                Text=rows[i].Key,AutoSize=true,ForeColor=Color.DimGray,
                Margin=new Padding(0,3,8,5),Anchor=AnchorStyles.Left|AnchorStyles.Top,
            },column,i/2);
            grid.Controls.Add(new Label {
                Text=Display(rows[i].Value),AutoSize=true,Margin=new Padding(0,3,16,5),
                Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,
            },column+1,i/2);
        }
        AddRow(section,grid);
    }

    private static void AddSummary(TableLayoutPanel section,IEnumerable<KeyValuePair<string,string>> fields)
    {
        var values=fields.ToArray();
        if(values.Length==0)return;
        AddRow(section,new Label {
            Text=string.Join("　｜　",values.Select(row=>$"{row.Key}  {Display(row.Value)}")),
            AutoSize=true,TextAlign=ContentAlignment.MiddleRight,Margin=new Padding(0,8,0,0),
        });
    }

    private static void AddProducts(TableLayoutPanel section,BusinessDetailTable? table,string emptyMessage)
    {
        if(table is null||table.Rows.Count==0) {
            AddRow(section,new Label { Text=emptyMessage,AutoSize=true,ForeColor=Color.DimGray,Margin=new Padding(0,4,0,8) });
            return;
        }
        var host=new NativeListViewHost(10F,27) { Tag=table.Title,Margin=Padding.Empty,Dock=DockStyle.None };
        var list=host.List;
        list.GridLines=true;
        list.Sorting=SortOrder.None;
        list.ShowItemToolTips=true;
        for(var i=0;i<table.Columns.Count;i++) {
            var title=table.Columns[i];
            list.Columns.Add(title,100,title.Contains("金額")||title.Contains("單價")||title.Contains("數量")||title.Contains("稅額")
                ?HorizontalAlignment.Right:HorizontalAlignment.Left);
        }
        foreach(var row in table.Rows)list.Items.Add(new ListViewItem(row.ToArray()));
        host.ConfigureTableAppearance();
        host.ConfigureFillColumns(0,table.Columns.Select((title,i)=>i==0?200:title.Contains("單價")?132:
            title.Contains("數量")?76:title is "稅別" or "課稅別"?66:title.Contains("單號")?128:title.Contains("日期")?100:title=="稅額"?80:110).ToArray());
        AddRow(section,host);
        void MeasureRows() {
            if(host.IsDisposed)return;
            host.Height=host.HeightForRows(Math.Min(5,table.Rows.Count));
            host.SetScrollNeeded(table.Rows.Count>5);
        }
        list.HandleCreated+=(_,_)=>MeasureRows();
        MeasureRows();
    }

    private static void AddRemaining(TableLayoutPanel section,IReadOnlyList<KeyValuePair<string,string>> fields)
    {
        string Value(string key)=>fields.FirstOrDefault(row=>row.Key==key).Value??"";
        var preflight=Value("最新可折含稅額");
        if(preflight.Length==0&&Value("處理狀態") is not ("待主管核准" or "等待主管核准" or "等待管理員人工處理"))return;
        var available=preflight.Length>0?preflight:Value("目前可折讓含稅額");
        var gross=Value("本次含稅折讓");
        if(gross.Length==0)gross=Value("含稅折讓總額");
        if(available.Length==0||gross.Length==0)return;
        try {
            var before=FixedDecimal.Parse(available.Replace(",",""));
            var amount=FixedDecimal.Parse(gross.Replace(",",""));
            var after=FixedDecimal.Add(before,new FixedDecimal(checked(-amount.ScaledValue)));
            AddRow(section,new Label {
                Text=$"可折讓 {before} 元　→　本次 {amount} 元　→　預估剩餘 {after} 元",
                AutoSize=true,TextAlign=ContentAlignment.MiddleRight,Margin=new Padding(0,6,0,0),
                ForeColor=after.ScaledValue<0?Color.Firebrick:Color.DimGray,
            });
        } catch(Exception error)when(error is FormatException or OverflowException) {
            AddRow(section,new Label { Text="未取得可確認的剩餘金額",AutoSize=true,ForeColor=Color.DimGray });
        }
    }

    internal static void VerifySmokeSections()
    {
        var invoice=new InvoiceRecord {
            InvoiceNumber="AA00000001",InvoiceDate="20261106",Amount=1050,DetailVat=1,
            Items=[new InvoiceItem { Description="原商品甲",Quantity=2,UnitPrice=200,Amount=400 },
                new InvoiceItem { Description="原商品乙",Quantity=1,UnitPrice=650,Amount=650 }],
        };
        CYInvoice.Core.Invoicing.InvoiceAllowanceMetadata.ApplyQuery(invoice,[]);
        IReadOnlyList<KeyValuePair<string,string>> fields=[new("折讓原因","部分退貨"),new("申請員工","3001 測試員工"),
            new("處理狀態","待主管核准"),new("本次含稅折讓","200")];
        IReadOnlyList<BusinessDetailTable> tables=[new("本次折讓商品",["品名","數量","未稅金額","稅額"],
            [new[]{"本次折讓品名","1","190","10"}])];
        var data=AllowanceDetailPresentation.Build(invoice,fields,tables);
        using var form=new ManualReviewDetailForm("折讓開立詳情",data.Fields,"核准並開立",true,tables:data.Tables,allowanceDetails:true);
        form.Show();Application.DoEvents();
        var view=Descendants(form).OfType<AllowanceDetailControl>().Single();
        var groups=view.sections.Controls.OfType<GroupBox>().ToArray();
        if(!groups.Select(group=>group.Text).SequenceEqual(new[]{"原發票資訊","本次折讓資訊","審核資訊"}))
            throw new InvalidOperationException("折讓詳情缺少三個分區");
        var source=Descendants(groups[0]).OfType<ListView>().Single();
        var current=Descendants(groups[1]).OfType<ListView>().Single();
        if(source.Items.Count!=2||source.Items[0].Text!="原商品甲"||source.Items[1].SubItems[3].Text!="650"||
            current.Items.Count!=1||current.Items[0].Text!="本次折讓品名")
            throw new InvalidOperationException("原發票商品與本次折讓商品未各自取自原紀錄");
        var missing=AllowanceDetailPresentation.Build(null,fields,tables);
        form.UpdateContent(missing.Fields,missing.Tables);Application.DoEvents();
        if(!Descendants(form).OfType<Label>().Any(label=>label.Text=="原發票商品資料未取得"))
            throw new InvalidOperationException("缺少原商品時未明示資料未取得");
        var approve=Descendants(form).OfType<Button>().Single(button=>button.Text=="核准並開立");
        approve.PerformClick();
        if(form.SelectedAction!=ManualReviewDetailAction.Primary)
            throw new InvalidOperationException("折讓核准未交回原動作owner");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach(Control child in root.Controls)
            foreach(var descendant in Descendants(child))yield return descendant;
    }
}
