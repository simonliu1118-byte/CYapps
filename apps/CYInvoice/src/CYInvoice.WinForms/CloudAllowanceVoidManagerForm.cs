using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

// V3 CYID-only manager console. Never calls AMEGO directly: all authority,
// source checks, send-once claims and official status are controlled by Cloud.
// Explicit human confirmation is required for all irreversible actions.
internal sealed class CloudAllowanceVoidManagerForm : Form
{
    private readonly LocalRepository repository;
    private readonly HttpClient http = new();
    private readonly CloudAllowanceVoidClient client;
    private readonly CloudWorkItemClient workItems;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<string> attemptedSends = new(StringComparer.Ordinal);
    private readonly ListView queue = new() {
        Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,
        GridLines=true,MultiSelect=false,HideSelection=false
    };
    private readonly Label notice = new() {
        Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,
        Text="CYID 折讓作廢：新增申請可由一般使用者提出；覆核及作廢僅限 CYID 管理員。"
    };
    private readonly Button create = UiControls.StandardButton("新增作廢申請");
    private readonly Button lookup = UiControls.StandardButton("依工作單 ID 查詢");
    private readonly Button refresh = UiControls.StandardButton("管理員清單");
    private readonly Button review = UiControls.StandardButton("開始覆核");
    private readonly Button approve = UiControls.StandardButton("核准作廢");
    private readonly Button send = UiControls.StandardButton("首次送出 g0501");
    private readonly Button readback = UiControls.StandardButton("官方查回");
    private readonly Button closeVerified = UiControls.StandardButton("確認結案");
    private readonly List<Button> controls = [];
    private readonly PendingAllowanceVoidRequestStore pendingRequests;
    private string? uncertainCreateId;
    private bool busy;

    public CloudAllowanceVoidManagerForm(
        LocalRepository repository,string endpoint,string deviceToken,string workspaceId)
    {
        this.repository=repository??throw new ArgumentNullException(nameof(repository));
        if(!Uri.TryCreate(endpoint,UriKind.Absolute,out var url)||url.Scheme!="https")
            throw new ArgumentException("CYID Cloud URL 必須使用 HTTPS。",nameof(endpoint));
        client=new CloudAllowanceVoidClient(http,url,deviceToken);
        workItems=new CloudWorkItemClient(http,url,deviceToken,workspaceId);
        pendingRequests=new PendingAllowanceVoidRequestStore(repository.DataDirectory,
            workspaceId+"|"+url.AbsoluteUri);
        uncertainCreateId=pendingRequests.Peek();
        Text="CYID 跨裝置折讓作廢";
        Font=new Font("Microsoft JhengHei UI",10F);
        BackColor=Color.White;
        StartPosition=FormStartPosition.CenterParent;
        ClientSize=new Size(1140,560);
        MinimumSize=new Size(940,490);
        ShowIcon=false;
        BuildLayout();
        if(uncertainCreateId is not null)
            notice.Text="上一筆作廢申請尚未確認，原 Work Item ID："+uncertainCreateId+
                "｜只能查回，不得用新 ID 重複提出。";
        // A normal employee may create/get their own Work Item. Do NOT
        // automatically request a manager-only list when this form opens.
    }

    private void BuildLayout()
    {
        queue.Columns.Add("原發票",130);
        queue.Columns.Add("折讓單號",165);
        queue.Columns.Add("狀態",90);
        queue.Columns.Add("版本",60);
        queue.Columns.Add("申請人",120);
        queue.Columns.Add("原因",245);
        queue.Columns.Add("工作單 ID",230);
        var root=new TableLayoutPanel{
            Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,94));
        root.Controls.Add(notice,0,0);
        root.Controls.Add(queue,0,1);
        var actions=new FlowLayoutPanel{
            Dock=DockStyle.Fill,WrapContents=true,
            FlowDirection=FlowDirection.LeftToRight,AutoScroll=true
        };
        foreach(var button in new[]{
            create,lookup,refresh,review,approve,send,readback,closeVerified
        }){
            button.Margin=new Padding(3,4,3,3);
            actions.Controls.Add(button);
            controls.Add(button);
        }
        root.Controls.Add(actions,0,2);
        Controls.Add(root);

        create.Click+=async (_,_)=>await CreateRequestAsync();
        lookup.Click+=async (_,_)=>await LookupAsync();
        refresh.Click+=async (_,_)=>await ReloadManagerAsync();
        review.Click+=async (_,_)=>await SelectedManagerActionAsync(async (item,user,pwd)=>{
            if(item.Status!="open"){
                Warn("這筆申請目前不是待覆核狀態。請重新整理。");return;
            }
            if(!Confirm($"確認開始覆核？\n原發票：{item.OriginalInvoiceNumber}\n"+
                $"折讓單號：{item.AllowanceNumber}\n工作單：{item.ItemId}\n\n"+
                "本操作僅將申請改為覆核中，不會向光貿作廢。"))return;
            var current=await workItems.GetAsync(item.ItemId,user,pwd,lifetime.Token);
            if(current is null||current.Kind!="ALLOWANCE_VOID"||
                current.Status!="open"||current.Revision!=item.Revision||
                current.WorkspaceId!=repository.Settings.LoadOrCreate().CloudWorkspaceId){
                Warn("工作單已異動或身分不一致。請重新整理。");return;
            }
            var updated=await workItems.TransitionAsync(current,"review",user,pwd,lifetime.Token);
            MessageBox.Show(this,$"已開始覆核，版本 {updated.Revision}。\n"+
                "尚未核准，也沒有向光貿送出作廢。","覆核中",
                MessageBoxButtons.OK,MessageBoxIcon.Information);
            ReplaceRow(item with {Status="review",Revision=updated.Revision});
        });
        approve.Click+=async (_,_)=>await SelectedManagerActionAsync(async (item,user,pwd)=>{
            if(!Reviewable(item))return;
            if(!Confirm($"核准折讓作廢？\n原發票：{item.OriginalInvoiceNumber}\n"+
                $"折讓單號：{item.AllowanceNumber}\n\n"+
                "Cloud 將即時查驗光貿原折讓與原發票關聯，"+
                "核准留存永久稽核紀錄，但不會在此步送出 g0501。"))return;
            var approved=await client.ApproveAsync(item.ItemId,item.Revision,
                user,pwd,lifetime.Token);
            MessageBox.Show(this,
                $"已記錄管理員核准，有效至 {approved.ValidUntilUtc:yyyy/MM/dd HH:mm}（UTC）。\n"+
                "尚未送出折讓作廢。","核准完成",
                MessageBoxButtons.OK,MessageBoxIcon.Information);
        });
        send.Click+=async (_,_)=>await SelectedManagerActionAsync(async (item,user,pwd)=>{
            if(!Reviewable(item))return;
            if(attemptedSends.Contains(item.ItemId)){
                Warn("這張工作單本次視窗已嘗試送出 g0501，禁止再次點選。\n"+
                    "即使網路錯誤，也只能使用「官方查回」確認原單號。");return;
            }
            if(!Confirm($"確定首次送出光貿 g0501 作廢？\n\n"+
                $"原發票：{item.OriginalInvoiceNumber}\n"+
                $"要作廢的折讓單號：{item.AllowanceNumber}\n"+
                $"Work Item：{item.ItemId}\n\n"+
                "系統將重新查驗官方資料並先永久取得 D1 單次送出權。\n"+
                "如果逾時或失去回覆，不得重送，必須用原單號查回。"))return;
            // Claim could have been written before HTTP exception. Lock the
            // button for this form's lifetime regardless of the reply.
            attemptedSends.Add(item.ItemId);
            var result=await client.SendOnceAsync(item.ItemId,item.Revision,
                user,pwd,lifetime.Token);
            MessageBox.Show(this,
                $"折讓單號：{result.AllowanceNumber}\n送出結果：{result.Outcome}\n\n"+
                "g0501 已接受不等於官方作廢完成。\n"+
                "不能重送；請使用「官方查回」確認，之後才由管理員結案。",
                "g0501 單次送出結果",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        });
        readback.Click+=async (_,_)=>await SelectedManagerActionAsync(async (item,user,pwd)=>{
            if(!Reviewable(item))return;
            var status=await client.ReadbackAsync(item.ItemId,item.Revision,
                user,pwd,lifetime.Token);
            MessageBox.Show(this,
                $"折讓單號：{status.AllowanceNumber}\n"+
                $"官方類型：{status.OfficialType}\n上傳狀態：{status.OfficialUploadStatus}\n"+
                $"官方顯示已作廢：{(status.OfficialCancelled?"是":"尚未確認")}\n"+
                $"核對時間：{status.CheckedAtUtc:yyyy/MM/dd HH:mm:ss}（UTC）\n\n"+
                "查回為唯讀；不能視為本工作單自動結案，也不能解除禁止重送。",
                "光貿折讓作廢查回",MessageBoxButtons.OK,
                status.OfficialCancelled?MessageBoxIcon.Information:MessageBoxIcon.Warning);
        });
        closeVerified.Click+=async (_,_)=>await SelectedManagerActionAsync(async(item,user,pwd)=>{
            if(!Reviewable(item))return;
            // Official readback must happen before the closure prompt;
            // Cloud will independently read back again and perform D1 CAS.
            var official=await client.ReadbackAsync(item.ItemId,item.Revision,
                user,pwd,lifetime.Token);
            if(!official.OfficialCancelled){
                Warn($"原折讓單號 {item.AllowanceNumber} 尚未確認官方作廢完成。\n"+
                    "Work Item 保留待查，不得直接結案。");return;
            }
            if(!Confirm($"官方狀態為 {official.OfficialType}／{official.OfficialUploadStatus}。\n"+
                $"確認結案工作單？\n\n原發票：{item.OriginalInvoiceNumber}\n"+
                $"折讓單號：{item.AllowanceNumber}\n\n"+
                "結案前 Cloud 會再次核對官方狀態、管理員身分、"+
                "唯一 send claim 及 D1 工作單版次；這不是重送 g0501。"))return;
            var completed=await client.CloseVerifiedAsync(item.ItemId,item.Revision,
                user,pwd,lifetime.Token);
            MessageBox.Show(this,
                $"工作單已結案，版本 {completed.Revision}。\n"+
                "僅代表已核對官方作廢結果，未再次送出 g0501。","結案完成",
                MessageBoxButtons.OK,MessageBoxIcon.Information);
            RemoveRow(item.ItemId);
        });
    }

    private bool Reviewable(CloudAllowanceVoidSummary item)
    {
        if(item.Status=="review")return true;
        Warn("請先從清單選擇狀態為「review」的折讓作廢申請，"+
            "並確認版本沒有變動。");
        return false;
    }
    private CloudAllowanceVoidSummary? Selected()=>
        queue.SelectedItems.Count==1?
        queue.SelectedItems[0].Tag as CloudAllowanceVoidSummary:null;
    private void ReplaceRow(CloudAllowanceVoidSummary updated)
    {
        var view=queue.Items.Cast<ListViewItem>()
            .FirstOrDefault(x=>(x.Tag as CloudAllowanceVoidSummary)?.ItemId==updated.ItemId);
        if(view is null)return;
        view.Tag=updated;
        view.SubItems[2].Text=updated.Status;
        view.SubItems[3].Text=updated.Revision.ToString();
    }
    private void RemoveRow(string id)
    {
        var view=queue.Items.Cast<ListViewItem>()
            .FirstOrDefault(x=>(x.Tag as CloudAllowanceVoidSummary)?.ItemId==id);
        if(view is not null)queue.Items.Remove(view);
    }
    private void DisplayQueue(IEnumerable<CloudAllowanceVoidSummary> items)
    {
        queue.BeginUpdate();
        try{
            queue.Items.Clear();
            foreach(var item in items){
                var row=new ListViewItem(item.OriginalInvoiceNumber){Tag=item};
                row.SubItems.Add(item.AllowanceNumber);
                row.SubItems.Add(item.Status);
                row.SubItems.Add(item.Revision.ToString());
                row.SubItems.Add(item.ApplicantEmployeeId);
                row.SubItems.Add(item.Reason);
                row.SubItems.Add(item.ItemId);
                queue.Items.Add(row);
            }
        }finally{queue.EndUpdate();}
    }

    private bool Credentials(bool administrator,string caption,
        out string employee,out string password)
    {
        employee=string.Empty;password=string.Empty;
        using var login=new EmployeeAdminLoginForm(repository,caption,
            administratorRequired:administrator,collectCredentialsOnly:true);
        if(login.ShowDialog(this)!=DialogResult.OK)return false;
        employee=login.EnteredEmployeeNo;
        password=login.AuthenticatedPassword;
        return true;
    }
    private bool Confirm(string details)=>
        MessageBox.Show(this,details,"再次確認",
            MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2)==DialogResult.Yes;
    private void Warn(string details)=>
        MessageBox.Show(this,details,"CYID 折讓作廢",
            MessageBoxButtons.OK,MessageBoxIcon.Warning);
    private void SetBusy(bool value)
    {
        busy=value;
        foreach(var button in controls)button.Enabled=!value;
    }

    private async Task SelectedManagerActionAsync(
        Func<CloudAllowanceVoidSummary,string,string,Task> action)
    {
        if(busy)return;
        var item=Selected();
        if(item is null){Warn("請先在管理員清單選擇工作單。");return;}
        if(!Credentials(true,"CYID 折讓作廢－管理員覆核",
            out var employee,out var password))return;
        SetBusy(true);
        try{await action(item,employee,password);}
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(Exception e){Warn("這次操作沒有收到確定結果："+e.Message+
            "\n請先用原 Work Item ID／折讓單號查回；不能自動重送。");}
        finally{if(!IsDisposed)SetBusy(false);}
    }

    private async Task ReloadManagerAsync()
    {
        if(busy)return;
        if(!Credentials(true,"CYID 跨機折讓作廢－管理員清單",
            out var employee,out var password))return;
        SetBusy(true);
        try{
            var results=new List<CloudAllowanceVoidSummary>();
            CloudAllowanceVoidCursor? next=null;
            for(int pageIndex=0;pageIndex<10;pageIndex++){
                var batch=await client.ListForManagerAsync(50,next,
                    employee,password,lifetime.Token);
                results.AddRange(batch.Applications);
                next=batch.NextCursor;
                if(next is null)break;
            }
            DisplayQueue(results);
            notice.Text=$"CYID 跨機折讓作廢：{results.Count} 筆（最多 500 筆）。"+
                "「review」後才可核准／送出／查回／結案；不允許重送。";
        }
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(Exception e){Warn("管理員清單無法確認："+e.Message);}
        finally{if(!IsDisposed)SetBusy(false);}
    }

    private async Task LookupAsync()
    {
        if(busy)return;
        using var form=new WorkItemIdPrompt(uncertainCreateId);
        if(form.ShowDialog(this)!=DialogResult.OK)return;
        if(!Credentials(false,"CYID 折讓作廢－查回原工作單",
            out var employee,out var password))return;
        SetBusy(true);
        try{
            var result=await client.GetAsync(form.ItemId,employee,password,lifetime.Token);
            if(result is null){
                Warn($"原工作單 {form.ItemId} 尚未查到。\n"+
                    "如果上一個建立請求逾時，不能據此直接建立新申請，"+
                    "請由管理員確認 D1 及官方狀態。");
                return;
            }
            if(uncertainCreateId==result.ItemId){
                pendingRequests.Confirm(result.ItemId);
                uncertainCreateId=null;
                notice.Text="原工作單已查回；管理員可從清單開始覆核。";
            }
            MessageBox.Show(this,
                $"Work Item：{result.ItemId}\n原發票：{result.OriginalInvoiceNumber}\n"+
                $"折讓單號：{result.AllowanceNumber}\n目前狀態：{result.Status}\n"+
                $"版本：{result.Revision}\n\n此查詢不會觸發作廢或重新送出。",
                "工作單查回（唯讀）",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(Exception e){Warn("原工作單查回失敗："+e.Message+
            "\n結果不明，不可用新 ID 重複送出。");}
        finally{if(!IsDisposed)SetBusy(false);}
    }

    private async Task CreateRequestAsync()
    {
        if(busy)return;
        if(uncertainCreateId is not null){
            Warn("上一張申請的結果尚未確認，原 Work Item ID：\n"+
                uncertainCreateId+"\n請先按「依工作單 ID 查詢」，勿重新產生工作單。");
            return;
        }
        using var entry=new AllowanceVoidNewRequestForm();
        if(entry.ShowDialog(this)!=DialogResult.OK)return;
        var draft=entry.Draft;
        if(draft is null)return;
        if(!Credentials(false,"CYID 折讓作廢－申請人驗證",
            out var employee,out var password))return;
        if(!Confirm($"確認提出折讓作廢申請？\n原發票：{draft.OriginalInvoiceNumber}\n"+
            $"折讓單號：{draft.AllowanceNumber}\nWork Item：{draft.ItemId}\n"+
            $"原因：{draft.Reason}\n\n申請不會直接執行光貿 g0501。\n"+
            "請記住原工作單 ID，逾時時只能原號查回。"))return;
        // Preserve the original ID BEFORE the network request. A lost HTTP
        // response cannot be resolved by generating a different Work Item.
        try{pendingRequests.Remember(draft.ItemId);}
        catch(Exception error){
            Warn("無法先將 Work Item ID 永久保存到本機，取消本次申請："+error.Message);
            return;
        }
        uncertainCreateId=draft.ItemId;
        SetBusy(true);
        try{
            await client.CreateAsync(draft,employee,password,lifetime.Token);
            pendingRequests.Confirm(draft.ItemId);
            uncertainCreateId=null;
            MessageBox.Show(this,
                $"申請已保存。\nWork Item：{draft.ItemId}\n"+
                "已送往 CYID 待管理員覆核，尚未執行光貿作廢。",
                "申請已建立",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(Exception e){
            Warn($"建立結果不明，保留 Work Item：\n{draft.ItemId}\n\n"+
                "必須依原 ID 查回，不能換新號碼或重送。\n"+e.Message);
        }
        finally{if(!IsDisposed)SetBusy(false);}
    }

    // Windows startup smoke: construct a complete manager console without
    // connecting a CYID Device or showing a credential prompt.
    internal void VerifySmokeLayout()
    {
        if(queue.Columns.Count!=7||controls.Count!=8||
           controls.Any(x=>x.Parent is null)||
           !controls.Select(x=>x.Text).Contains("首次送出 g0501")||
           !controls.Select(x=>x.Text).Contains("官方查回")||
           !controls.Select(x=>x.Text).Contains("確認結案")||
           !controls.Select(x=>x.Text).Contains("開始覆核"))
            throw new InvalidOperationException("CYID allowance void controls are incomplete.");
        using var request=new AllowanceVoidNewRequestForm();
        request.VerifySmokeLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing){lifetime.Cancel();lifetime.Dispose();http.Dispose();}
        base.Dispose(disposing);
    }
}

// Contains no network calls. The Work Item ID is generated and displayed
// BEFORE the submission, making timeout recovery possible.
internal sealed class AllowanceVoidNewRequestForm : Form
{
    private readonly TextBox invoice = new(){Width=220,MaxLength=10};
    private readonly TextBox number = new(){Width=220,MaxLength=16};
    private readonly TextBox reason = new(){Multiline=true,Width=420,Height=110,MaxLength=500};
    private readonly TextBox workItem = new(){ReadOnly=true,Width=340};
    public CloudAllowanceVoidDraft? Draft{get;private set;}

    internal void VerifySmokeLayout()
    {
        if(!workItem.ReadOnly||!workItem.Text.StartsWith("wi_",StringComparison.Ordinal)||
           workItem.Text.Length<13||invoice.MaxLength!=10||
           number.MaxLength!=16||reason.MaxLength!=500||Draft is not null)
            throw new InvalidOperationException("Cancellation application form is not safe.");
    }

    public AllowanceVoidNewRequestForm()
    {
        Text="新增折讓作廢申請";
        StartPosition=FormStartPosition.CenterParent;
        FormBorderStyle=FormBorderStyle.FixedDialog;
        MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
        ClientSize=new Size(550,410);
        Font=new Font("Microsoft JhengHei UI",10F);
        workItem.Text=CloudAllowanceVoidClient.CreateItemId();
        var root=new TableLayoutPanel{
            Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=2,RowCount=6
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,115));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(var (label,control,row) in new[]{
            ("原發票號碼",(Control)invoice,0),("折讓單號",number,1),
            ("申請原因",reason,2),("Work Item ID",workItem,3)
        }){
            var text=new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left};
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,row==2?125:47));
            root.Controls.Add(text,0,row);
            control.Anchor=AnchorStyles.Left|AnchorStyles.Right;
            root.Controls.Add(control,1,row);
        }
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,37));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
        var warning=new Label{
            Text="請先保存 Work Item ID。結果不明時只能查回，不得重複申請。",
            ForeColor=Color.DarkRed,AutoSize=true
        };
        root.Controls.Add(warning,0,4);
        root.SetColumnSpan(warning,2);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var submit=UiControls.StandardButton("確認申請");
        var cancel=UiControls.StandardButton("取消");
        var copy=UiControls.StandardButton("複製工作單 ID");
        cancel.Click+=(_,_)=>{DialogResult=DialogResult.Cancel;Close();};
        copy.Click+=(_,_)=>{
            try{Clipboard.SetText(workItem.Text);}
            catch(Exception e){MessageBox.Show(this,"無法複製，請手動保存："+e.Message);}
        };
        submit.Click+=(_,_)=>{
            Draft=new CloudAllowanceVoidDraft(workItem.Text,
                number.Text.Trim(),invoice.Text.Trim().ToUpperInvariant(),reason.Text.Trim());
            DialogResult=DialogResult.OK;Close();
        };
        actions.Controls.Add(submit);actions.Controls.Add(cancel);actions.Controls.Add(copy);
        root.Controls.Add(actions,0,5);root.SetColumnSpan(actions,2);
        Controls.Add(root);
        AcceptButton=submit;CancelButton=cancel;
    }
}
internal sealed class WorkItemIdPrompt : Form
{
    private readonly TextBox entry = new(){Width=390};
    public string ItemId=>entry.Text.Trim();
    public WorkItemIdPrompt(string? initial)
    {
        Text="以原 Work Item ID 查詢";
        StartPosition=FormStartPosition.CenterParent;
        FormBorderStyle=FormBorderStyle.FixedDialog;
        ClientSize=new Size(520,135);
        MaximizeBox=false;MinimizeBox=false;
        ShowInTaskbar=false;
        Font=new Font("Microsoft JhengHei UI",10F);
        entry.Text=initial??"";
        entry.Location=new Point(16,20);
        Controls.Add(entry);
        var query=UiControls.StandardButton("查詢");
        query.Location=new Point(330,78);
        query.Click+=(_,_)=>{
            if(!System.Text.RegularExpressions.Regex.IsMatch(ItemId,@"^[A-Za-z0-9_-]{10,100}$")){
                MessageBox.Show(this,"請輸入有效的原 Work Item ID。");
                return;
            }
            DialogResult=DialogResult.OK;Close();
        };
        var cancel=UiControls.StandardButton("取消");
        cancel.Location=new Point(430,78);
        cancel.Click+=(_,_)=>{DialogResult=DialogResult.Cancel;Close();};
        Controls.Add(query);Controls.Add(cancel);
        AcceptButton=query;CancelButton=cancel;
    }
}


// No invoices, passwords or API keys are written to this marker: only the
// original Work Item ID needed to recover an ambiguous create after restart.
// A scope hash prevents different CYID Workspace/Cloud URLs from sharing it.
internal sealed class PendingAllowanceVoidRequestStore
{
    private readonly string marker;
    public PendingAllowanceVoidRequestStore(string directory,string scope)
    {
        if(string.IsNullOrWhiteSpace(directory)||string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Invalid pending allowance void scope.");
        var digest=System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(scope));
        var key=Convert.ToHexString(digest).ToLowerInvariant()[..20];
        marker=Path.Combine(directory,"cyinvoice-pending-allowance-void-"+key+".txt");
    }
    public string? Peek()
    {
        if(!File.Exists(marker))return null;
        var content=File.ReadAllText(marker,System.Text.Encoding.UTF8).Trim();
        if(!System.Text.RegularExpressions.Regex.IsMatch(content,
            @"^[A-Za-z0-9_-]{10,100}$"))
            throw new InvalidDataException("待查折讓工作單記錄損壞，禁止產生新單。");
        return content;
    }
    public void Remember(string itemId)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(itemId??"",
            @"^[A-Za-z0-9_-]{10,100}$"))
            throw new ArgumentException("Invalid pending Work Item ID.",nameof(itemId));
        if(Peek() is not null)
            throw new InvalidOperationException("A previous allowance void request remains unresolved.");
        var folder=Path.GetDirectoryName(marker)!;
        Directory.CreateDirectory(folder);
        var temp=marker+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            var bytes=System.Text.Encoding.UTF8.GetBytes(itemId);
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                stream.Write(bytes);
                stream.Flush(flushToDisk:true);
            }
            // Fail rather than replacing another outstanding Work Item.
            File.Move(temp,marker,overwrite:false);
        }finally{
            if(File.Exists(temp))File.Delete(temp);
        }
    }
    public void Confirm(string itemId)
    {
        if(Peek()!=itemId)
            throw new InvalidOperationException("Original Work Item identity changed.");
        File.Delete(marker);
    }
}
