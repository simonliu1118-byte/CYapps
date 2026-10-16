using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

// V3 CYID Cloud manager workflow. Every protected operation collects fresh
// CYID credentials; the Cloud backend owns all roles, company keys and fences.
// No client-generated allowance number or blind HTTP retry is ever performed.
internal sealed class CloudAllowanceManagerForm : Form
{
    private readonly LocalRepository repository;
    private readonly HttpClient http = new();
    private readonly CloudAllowanceApplicationClient client;
    private readonly CloudWorkItemClient workItems;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ListView queue = new() {
        Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,
        GridLines=true,MultiSelect=false,HideSelection=false
    };
    private readonly Label hint = new() {
        Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft,
        Text="CYID 雲端折讓｜核准、配號與首次送出為獨立作業；結果不明只能按原號查回。"
    };
    private readonly List<Button> operations = [];
    private readonly Button refresh=UiControls.StandardButton("重新整理");
    private readonly Button review=UiControls.StandardButton("送交覆核");
    private readonly Button preview=UiControls.StandardButton("申請明細");
    private readonly Button inspect=UiControls.StandardButton("官方查驗");
    private readonly Button approve=UiControls.StandardButton("核准");
    private readonly Button reserve=UiControls.StandardButton("配號");
    private readonly Button issue=UiControls.StandardButton("首次送出");
    private readonly Button readback=UiControls.StandardButton("查回原號");
    private readonly Button reconcile=UiControls.StandardButton("官方對帳");
    private bool busy;

    public CloudAllowanceManagerForm(LocalRepository repository,string endpoint,string token)
    {
        this.repository=repository??throw new ArgumentNullException(nameof(repository));
        if(!Uri.TryCreate(endpoint,UriKind.Absolute,out var url) || url.Scheme!="https")
            throw new ArgumentException("CYID Cloud URL must be HTTPS.",nameof(endpoint));
        client=new CloudAllowanceApplicationClient(http,url,token);
        workItems=new CloudWorkItemClient(http,url,token,
            repository.Settings.LoadOrCreate().CloudWorkspaceId);
        Text="CYID 跨裝置折讓覆核";
        Font=new Font("Microsoft JhengHei UI",10F);
        BackColor=Color.White;
        StartPosition=FormStartPosition.CenterParent;
        ClientSize=new Size(1030,560);
        MinimumSize=new Size(940,480);
        ShowIcon=false;
        Build();
        Shown+=async (_,_)=>await ReloadAsync();
    }

    private void Build()
    {
        queue.Columns.Add("原發票號碼",135);
        queue.Columns.Add("折讓日",105);
        queue.Columns.Add("含稅金額",100);
        queue.Columns.Add("狀態",95);
        queue.Columns.Add("申請人",130);
        queue.Columns.Add("申請原因",280);
        queue.Columns.Add("Work Item",170);
        var root=new TableLayoutPanel {
            Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,
            Padding=new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,72));
        root.Controls.Add(hint,0,0);
        root.Controls.Add(queue,0,1);
        var buttons=new FlowLayoutPanel{
            Dock=DockStyle.Fill,FlowDirection=FlowDirection.LeftToRight,
            WrapContents=true
        };
        foreach(var button in new[]{refresh,review,preview,inspect,approve,reserve,issue,readback,reconcile})
        {
            button.Margin=new Padding(2,4,3,4);
            buttons.Controls.Add(button);
            operations.Add(button);
        }
        root.Controls.Add(buttons,0,2);
        Controls.Add(root);
        refresh.Click+=async (_,_)=>await ReloadAsync();
        review.Click+=async (_,_)=>await RunForSelectedAsync(async (entry,user,pwd)=>{
            if(entry.Status!="open"){
                MessageBox.Show(this,"此申請已送交覆核，不能重複送審。",
                    "已送審",MessageBoxButtons.OK,MessageBoxIcon.Information);
                return;
            }
            if(!Confirm("將折讓申請 "+entry.ItemId+" 送交管理員覆核？\n"+
                "送審不會配號或送出光貿。"))return;
            var item=await workItems.GetAsync(entry.ItemId,user,pwd,lifetime.Token)
                ?? throw new InvalidDataException("CYID Work Item 尚未查回");
            if(item.Kind!="ALLOWANCE"||item.Revision!=entry.Revision||
               item.Status!="open")
                throw new InvalidOperationException("Work Item 狀態已變更，請重新整理");
            var result=await workItems.TransitionAsync(item,"review",
                user,pwd,lifetime.Token);
            MessageBox.Show(this,"Work Item "+result.ItemId+" 已送交覆核。\n"+
                "尚未核准，也尚未開立折讓。",
                "送審完成",MessageBoxButtons.OK,MessageBoxIcon.Information);
        });
        preview.Click+=async (_,_)=>await RunForSelectedAsync(async (entry,user,pwd)=>{
            var p=await client.PreviewForManagerAsync(entry.ItemId,entry.Revision,
                user,pwd,lifetime.Token);
            var text=string.Join(Environment.NewLine,p.Items.Select(x=>
                $"{x.Description}｜數量 {x.Quantity}｜未稅小計 {x.Amount}｜稅額 {x.Tax}"));
            MessageBox.Show(this,
                $"原發票：{p.OriginalInvoiceNumber}\n申請理由：{p.Reason}\n"+
                $"申請含稅金額：{p.RequestedGrossAmount:N0}\n\n"+
                $"{text}\n\n尚未完成官方核對；此畫面不能直接視為核准。",
                "凍結申請明細（唯讀）",MessageBoxButtons.OK,MessageBoxIcon.Information);
        });
        inspect.Click+=async (_,_)=>await RunForSelectedAsync(async (entry,user,pwd)=>{
            var v=await client.InspectOfficialForManagerAsync(entry.ItemId,entry.Revision,
                user,pwd,lifetime.Token);
            // This UI shows the official gross allowance gate only.
            // Historical item allocation/remaining-quantity estimates are NOT
            // an eligibility condition and must not mislead the manager.
            var guidance=v.ItemCheck.Reason switch
            {
                "REQUEST_TOTALS_WITHIN_REMAINING" =>
                    "本次含稅金額未超過原發票扣除已生效折讓後的總額上限。",
                "REQUEST_EXCEEDS_REMAINING" =>
                    "本次折讓超過原發票扣除已生效折讓後的剩餘上限。",
                "OFFICIAL_HISTORY_UNVERIFIED" =>
                    "官方折讓摘要缺漏、重複或狀態不明，不能核准。",
                "OFFICIAL_STATE_NOT_SAFE" =>
                    "原發票未完成、已作廢或存在未確認的異動。",
                "REQUEST_TOTALS_MISMATCH" =>
                    "使用者編輯後的折讓明細總額與申請金額不一致。",
                "REQUEST_ITEM_INCOMPLETE" or "REQUEST_ITEMS_INVALID" =>
                    "折讓明細有必要欄位缺漏，請修正後再申請。",
                "REQUEST_ORIGINAL_INVOICE_MISMATCH" =>
                    "折讓來源發票號碼或日期不符。",
                _ => "官方狀態或本次折讓資料尚未完成核對。"
            };
            var histories=v.AllowanceHistory.Take(8).Select(h=>
                $"  折讓號 {h.AllowanceNumber??"未確認"}｜"+
                $"未稅 {h.TotalAmount??"未確認"}｜稅 {h.TaxAmount??"未確認"}｜"+
                $"狀態 {h.UploadStatus?.ToString()??"未確認"}｜"+
                $"作廢 {(h.CancelDate=="0"?"否":h.CancelDate is null?"未確認":"是")}");
            var summary=v.ExistingAllowanceCount==0
                ?(v.AllowanceSummaryExplicit?"官方摘要：沒有已記錄的折讓":"官方折讓摘要缺漏")
                :string.Join("\n",histories)+
                    (v.ExistingAllowanceCount>8?"\n（另有其他折讓紀錄）":"");
            MessageBox.Show(this,
                $"原發票：{v.OriginalInvoiceNumber}\n"+
                $"原發票含稅總額：{v.OfficialInvoiceGross:N0} 元\n"+
                $"本次申請含稅金額：{v.RequestedGrossAmount:N0} 元\n"+
                $"官方上傳狀態：{v.OfficialUploadStatus}\n"+
                $"官方已列折讓數：{v.ExistingAllowanceCount}\n"+
                $"已列折讓紀錄：\n{summary}\n\n"+
                $"總額核對：{guidance}\n"+
                $"核准條件：{(v.CanApprove?"本次查驗符合":"尚未符合")}\n"+
                $"查核時間：{v.CheckedAtUtc:yyyy/MM/dd HH:mm:ss}\n\n"+
                "這是唯讀查驗，不會核准、配號或向光貿送出折讓。",
                "光貿折讓總額查驗",MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
        approve.Click+=async (_,_)=>await RunForSelectedAsync(async (entry,user,pwd)=>{
            if(!Confirm($"確認核准折讓申請？\n原發票：{entry.OriginalInvoiceNumber}\n"+
                $"申請含稅金額：{entry.RequestedGrossAmount:N0}\n\n"+
                "系統將再次查核光貿並永久記錄核准人；不會在這一步送出折讓。"))return;
            var v=await client.ApproveForManagerAsync(entry.ItemId,entry.Revision,
                user,pwd,lifetime.Token);
            MessageBox.Show(this,
                $"已留下核准紀錄，至 {v.ValidUntilUtc:yyyy/MM/dd HH:mm} (UTC) 有效。\n"+
                "尚未配號，也尚未送出光貿。","核准完成",
                MessageBoxButtons.OK,MessageBoxIcon.Information);
        });
        reserve.Click+=async (_,_)=>await RunForSelectedAsync(async (entry,user,pwd)=>{
            if(!Confirm($"要為原發票 {entry.OriginalInvoiceNumber} 的核准申請配號？\n"+
                "配出的號碼永久保存，不能刪除、回收或自動重用。"))return;
            var v=await client.ReserveForManagerAsync(entry.ItemId,entry.Revision,
                user,pwd,lifetime.Token);
            MessageBox.Show(this,$"已配號：{v.AllowanceNumber}\n尚未向光貿開立。",
                "折讓配號完成",MessageBoxButtons.OK,MessageBoxIcon.Information);
        });
        issue.Click+=async (_,_)=>await RunForSelectedAsync(async (entry,user,pwd)=>{
            var current=await client.GetNumberStatusForManagerAsync(entry.ItemId,
                entry.Revision,user,pwd,lifetime.Token);
            if(current.Status!="reserved")
            {
                MessageBox.Show(this,$"目前單號 {current.AllowanceNumber} 狀態為 "+
                    $"{current.Status}，不能再次送出。\n請使用「查回原號」核對官方結果。",
                    "不可重送",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                return;
            }
            if(!Confirm($"確認首次送出光貿 g0401？\n\n"+
                $"原發票：{entry.OriginalInvoiceNumber}\n"+
                $"折讓單號：{current.AllowanceNumber}\n"+
                $"申請含稅金額：{entry.RequestedGrossAmount:N0}\n\n"+
                "本次操作不能自動重送。若網路逾時或結果不明，"+
                "必須以原折讓號碼查回，不能直接再按一次。"))return;
            var result=await client.SendOnceForManagerAsync(entry.ItemId,entry.Revision,
                user,pwd,lifetime.Token);
            MessageBox.Show(this,$"折讓單號：{result.AllowanceNumber}\n"+
                $"送出結果：{result.Result}\n\n"+
                "不論光貿目前是否接受，都不等於財政部完成。\n"+
                "請使用「查回原號」核對；系統不會自動重新送出。",
                "首次送出結果",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        });
        readback.Click+=async (_,_)=>await RunForSelectedAsync(async (entry,user,pwd)=>{
            var status=await client.GetNumberStatusForManagerAsync(entry.ItemId,entry.Revision,
                user,pwd,lifetime.Token);
            if(status.Status=="reserved")
            {
                MessageBox.Show(this,$"原號 {status.AllowanceNumber} 尚未送出。",
                    "配號狀態",MessageBoxButtons.OK,MessageBoxIcon.Information);
                return;
            }
            var result=await client.ReadbackNumberForManagerAsync(entry.ItemId,entry.Revision,
                user,pwd,lifetime.Token);
            MessageBox.Show(this,$"原折讓單號：{result.AllowanceNumber}\n"+
                $"官方可能對應本申請：{(result.PossibleMatch?"是":"未確認")}\n"+
                $"原發票與金額比對：{(result.GrossMatchesFrozenRequest?"相符":"未確認")}\n\n"+
                "查回僅為唯讀參考，不得自行視為已結案或重送。",
                "光貿原號查回",MessageBoxButtons.OK,MessageBoxIcon.Information);
        });
        reconcile.Click+=async (_,_)=>await RunForSelectedAsync(async (entry,user,pwd)=>{
            // Unlike direct original-number readback, this action first scans
            // both official date filters and records verified query evidence.
            var result=await client.ReconcileForManagerAsync(entry.ItemId,entry.Revision,
                user,pwd,lifetime.Token);
            if(result.Readback is null)
            {
                MessageBox.Show(this,
                    $"折讓單號：{result.AllowanceNumber}\n"+
                    "近十四日官方清單未查到此號；這不代表未送達。\n"+
                    "請維持原工作單，必要時使用「查回原號」人工核實。\n\n"+
                    "不得改號、重送或自動結案。",
                    "官方對帳－待人工確認",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                return;
            }
            var readback=result.Readback;
            MessageBox.Show(this,
                $"折讓單號：{result.AllowanceNumber}\n"+
                $"雙日期清單：{(result.ListReason=="CONFLICTING_LIST_RESULTS"?"紀錄衝突":"找到對應紀錄")}\n"+
                $"官方單號查回：{(readback.PossibleMatch?"可能相符":"仍未確認")}\n"+
                $"官方類型／狀態：{readback.OfficialInvoiceType}／{readback.OfficialUploadStatus}\n\n"+
                "已由服務端記錄原號官方查驗稽核。\n"+
                "結果仍須人工確認，不得自動重送或結案。",
                "官方對帳－原號查回",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        });
    }

    private CloudAllowanceApplicationSummary? Selected() =>
        queue.SelectedItems.Count==1?
          queue.SelectedItems[0].Tag as CloudAllowanceApplicationSummary:null;

    private bool GetCredentials(string label,out string employeeNo,out string password)
    {
        employeeNo=string.Empty;password=string.Empty;
        using var login=new EmployeeAdminLoginForm(repository,label,
            administratorRequired:true,collectCredentialsOnly:true);
        if(login.ShowDialog(this)!=DialogResult.OK)return false;
        employeeNo=login.EnteredEmployeeNo;
        password=login.AuthenticatedPassword;
        return true;
    }
    private bool Confirm(string message) =>
        MessageBox.Show(this,message,"請再次確認",
            MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2)==DialogResult.Yes;

    private async Task RunForSelectedAsync(Func<CloudAllowanceApplicationSummary,
        string,string,Task> action)
    {
        if(busy)return;
        var selected=Selected();
        if(selected is null)
        {
            MessageBox.Show(this,"請先選擇一筆待覆核折讓。","尚未選擇",
                MessageBoxButtons.OK,MessageBoxIcon.Information);
            return;
        }
        if(!GetCredentials("跨機折讓－CYID 管理員當次驗證",
            out var employeeNo,out var password))return;
        SetBusy(true);
        try{await action(selected,employeeNo,password);}
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(CloudApiException error) when(error.Code=="ALLOWANCE_APPROVAL_NOT_AUTHORIZED")
        {
            MessageBox.Show(this,
                "折讓核准未獲確認，可能是官方折讓剩餘額、申請總額或管理員權限需要進一步核對。\n"+
                "請先使用「官方查驗」確認原發票與已生效折讓，必要時至光貿人工核對。\n"+
                "保留原工作單；在確認核准前不可配號或送出折讓。",
                "折讓需要人工查核",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        catch(Exception error){
            MessageBox.Show(this,"此次操作未獲確認："+error.Message+
                "\n請先查回原工作單／折讓號碼，不要重新送出。",
                "作業待確認",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        finally{if(!IsDisposed)SetBusy(false);}
    }
    private async Task ReloadAsync()
    {
        if(busy)return;
        if(!GetCredentials("跨機折讓清單－CYID 管理員驗證",
            out var employeeNo,out var password))return;
        SetBusy(true);
        try{
            var results=new List<CloudAllowanceApplicationSummary>();
            CloudAllowanceApplicationCursor? cursor=null;
            for(var pages=0;pages<10;pages++)
            {
                var page=await client.ListForManagerAsync(50,cursor,
                    employeeNo,password,lifetime.Token);
                results.AddRange(page.Applications);
                cursor=page.NextCursor;
                if(cursor is null)break;
            }
            queue.BeginUpdate();
            try{
                queue.Items.Clear();
                foreach(var item in results){
                    var row=new ListViewItem(item.OriginalInvoiceNumber){Tag=item};
                    row.SubItems.Add(item.AllowanceDay);
                    row.SubItems.Add(item.RequestedGrossAmount.ToString("N0"));
                    row.SubItems.Add(item.Status);
                    row.SubItems.Add(item.ApplicantEmployeeId);
                    row.SubItems.Add(item.Reason);
                    row.SubItems.Add(item.ItemId);
                    queue.Items.Add(row);
                }
            }finally{queue.EndUpdate();}
            hint.Text=$"CYID 跨機折讓：{results.Count} 筆（最多顯示 500 筆）｜"+
                "異常回覆不得重送，請用原號查回。";
        }
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(Exception error){
            MessageBox.Show(this,"無法讀取管理員待覆核清單："+error.Message,
                "CYID 雲端查詢失敗",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        finally{if(!IsDisposed)SetBusy(false);}
    }
    private void SetBusy(bool value){
        busy=value;
        foreach(var b in operations)b.Enabled=!value;
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){lifetime.Cancel();lifetime.Dispose();http.Dispose();}
        base.Dispose(disposing);
    }
}
