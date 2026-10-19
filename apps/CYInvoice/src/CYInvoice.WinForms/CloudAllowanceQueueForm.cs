using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

// V3.2-C4c - Cloud allowance reviewer and applicant readback.
// No new official AMEGO issue button, no g0401/g0501 or automatic retries.
// List results may be seen only after CYID credentials + bound Device checks
// on the Worker, not because any Windows button happens to be visible.
internal sealed class CloudAllowanceQueueForm : Form
{
    private readonly HttpClient http=new(new HttpClientHandler{AllowAutoRedirect=false});
    private readonly CloudAllowanceClient client=null!;
    private readonly TextBox employeeNo=new(){Width=80,MaxLength=4};
    private readonly TextBox password=new(){Width=170,MaxLength=128,UseSystemPasswordChar=true};
    private readonly ListView items=new(){Dock=DockStyle.Fill,View=View.Details,
        FullRowSelect=true,MultiSelect=false,GridLines=true,HideSelection=false};
    private readonly Label status=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
        ForeColor=Color.DimGray,AutoEllipsis=true};
    private readonly Button refresh=UiControls.StandardButton("重新整理");
    private readonly Button review=UiControls.StandardButton("認領覆核");
    private readonly Button preview=UiControls.StandardButton("查官方狀態");
    private readonly Button approve=UiControls.StandardButton("主管核准");
    private readonly Button reserve=UiControls.StandardButton("預留單號");
    private readonly Button readback=UiControls.StandardButton("查回原狀態");
    private readonly Button close=UiControls.StandardButton("關閉");
    private CloudAllowanceSummary? selected;
    private string? previewApplicationId;
    private int previewRevision;
    private bool previewComplete;
    private bool busy;

    private CloudAllowanceQueueForm()
    {
        Text="跨機折讓申請／覆核";
        StartPosition=FormStartPosition.CenterParent;
        ClientSize=new Size(970,540);
        MinimumSize=new Size(790,460);
        Font=new Font("Microsoft JhengHei UI",10F);
        BackColor=Color.White;
        ShowIcon=false;
        BuildLayout();
        FormClosed+=(_,_)=>http.Dispose();
    }
    public CloudAllowanceQueueForm(LocalRepository repository):this()
    {
        ArgumentNullException.ThrowIfNull(repository);
        var settings=repository.Settings.LoadOrCreate();
        if(settings.CloudMode==CloudModes.LocalOnly||
           settings.CloudIdentityProvider!="CYID"||
           !settings.CloudEmployeeAuthorityReady||
           string.IsNullOrWhiteSpace(settings.CloudBaseUrl)||
           string.IsNullOrWhiteSpace(settings.CloudWorkspaceId)||
           string.IsNullOrWhiteSpace(settings.CloudDeviceId))
            throw new InvalidOperationException("CYID Cloud Device is required.");
        var token=repository.Settings.CloudDeviceToken(settings);
        client=new CloudAllowanceClient(http,
            new Uri(settings.CloudBaseUrl,UriKind.Absolute),token);
        Shown+=(_,_)=>{employeeNo.Focus();};
    }
    private void BuildLayout()
    {
        employeeNo.AccessibleName="CYID 員工編號";
        password.AccessibleName="CYID 當次驗證密碼";
        items.AccessibleName="跨機折讓申請狀態";
        items.Columns.Add("更新時間",145);
        items.Columns.Add("申請狀態",95);
        items.Columns.Add("環境",85);
        items.Columns.Add("折讓含稅額",115);
        items.Columns.Add("版次",55);
        items.Columns.Add("申請 ID",300);
        items.SelectedIndexChanged+=(_,_)=>{
            selected=items.SelectedItems.Count==1?
                items.SelectedItems[0].Tag as CloudAllowanceSummary:null;
            previewApplicationId=null;previewComplete=false;
            UpdateButtons();
        };
        var login=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=false,
            FlowDirection=FlowDirection.LeftToRight,WrapContents=false,
            Padding=new Padding(4),Margin=Padding.Empty};
        login.Controls.Add(new Label{Text="CYID 員工",AutoSize=true,
            TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(0,7,0,0)});
        login.Controls.Add(employeeNo);
        login.Controls.Add(new Label{Text="當次密碼",AutoSize=true,
            TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(7,7,0,0)});
        login.Controls.Add(password);
        login.Controls.Add(refresh);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=false,
            FlowDirection=FlowDirection.LeftToRight,WrapContents=false,
            Padding=Padding.Empty,Margin=Padding.Empty};
        foreach(var b in new[]{review,preview,approve,reserve,readback,close})
            actions.Controls.Add(b);
        refresh.Click+=async(_,_)=>await LoadAsync();
        review.Click+=async(_,_)=>await ReviewAsync();
        preview.Click+=async(_,_)=>await PreviewAsync();
        approve.Click+=async(_,_)=>await ApproveAsync();
        reserve.Click+=async(_,_)=>await ReserveAsync();
        readback.Click+=async(_,_)=>await StatusAsync();
        close.Click+=(_,_)=>Close();
        AcceptButton=refresh;
        CancelButton=close;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,
            RowCount=4,Padding=new Padding(12),Margin=Padding.Empty};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        root.Controls.Add(login,0,0);
        root.Controls.Add(items,0,1);
        root.Controls.Add(actions,0,2);
        root.Controls.Add(status,0,3);
        Controls.Add(root);
        status.Text="請輸入當次 CYID 帳號密碼後重新整理；單號預留不是光貿完成。";
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        refresh.Enabled=!busy;
        var exists=selected is not null&&!busy;
        review.Enabled=exists&&selected!.Status=="open";
        preview.Enabled=exists&&selected!.Status=="review";
        approve.Enabled=exists&&selected!.Status=="review"&&previewComplete&&
            previewApplicationId==selected.ApplicationId&&
            previewRevision==selected.Revision;
        reserve.Enabled=exists&&selected!.Status=="approved";
        readback.Enabled=exists;
    }
    private bool CredentialsReady()
    {
        if(employeeNo.Text.Length==4&&password.Text.Length>0)return true;
        MessageBox.Show(this,"每次操作請輸入 CYID 員工編號與密碼。",
            "需要身分驗證",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        return false;
    }
    private async Task Guarded(Func<Task> action)
    {
        if(busy||!CredentialsReady())return;
        busy=true;UpdateButtons();status.Text="正在查詢已授權的 Cloud 狀態…";
        try{await action();}
        catch(Exception ex)when(ex is CloudApiException or HttpRequestException or
              OperationCanceledException or InvalidDataException or ArgumentException)
        {
            // Do not show raw HTTP JSON, credentials or original invoice PII.
            status.Text="Cloud 結果未確認。請用原申請 ID 查回，不要重送或新建申請。";
            MessageBox.Show(this,"Cloud 請求未確認。請查回同一申請 ID 的狀態。",
                "狀態需要查回",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        finally{busy=false;UpdateButtons();}
    }
    private async Task LoadAsync()
    {
        await Guarded(async()=>{
            var result=await client.ListAsync(employeeNo.Text,password.Text);
            items.BeginUpdate();
            try{
                items.Items.Clear();selected=null;previewComplete=false;
                foreach(var a in result){
                    var v=new ListViewItem(new[]{
                        a.UpdatedAt.LocalDateTime.ToString("yyyy/MM/dd HH:mm"),
                        a.Status,a.Environment,a.RequestedGross,
                        a.Revision.ToString(),a.ApplicationId}){Tag=a};
                    items.Items.Add(v);
                }
            }finally{items.EndUpdate();}
            status.Text=$"已讀取 {result.Count} 筆申請；所有操作仍須 CYID 重新驗權。";
        });
    }
    private async Task ReviewAsync()
    {
        var a=selected;if(a is null||a.Status!="open")return;
        await Guarded(async()=>{
            var receipt=await client.ReviewAsync(a.ApplicationId,a.Revision,
                employeeNo.Text,password.Text);
            previewComplete=false;
            status.Text=$"已認領覆核（revision {receipt.Revision}）；請重新整理並查官方資料。";
        });
    }
    private async Task PreviewAsync()
    {
        var a=selected;if(a is null||a.Status!="review")return;
        await Guarded(async()=>{
            var p=await client.PreviewAsync(a.ApplicationId,a.Revision,
                employeeNo.Text,password.Text);
            previewApplicationId=a.ApplicationId;
            previewRevision=a.Revision;
            previewComplete=p.OfficialSummaryComplete&&p.OfficialUploadStatus==99;
            status.Text=$"官方 {p.OriginalInvoiceNumber}：狀態 {p.OfficialUploadStatus}，已有折讓 {p.ExistingAllowanceCount} 筆；"+
                (previewComplete?"摘要完整，但核准時仍會重新查官方逐筆資料。":
                "官方資料不完整或待處理，禁止核准。");
            UpdateButtons();
        });
    }
    private async Task ApproveAsync()
    {
        var a=selected;
        if(a is null||a.Status!="review"||!previewComplete||
           previewApplicationId!=a.ApplicationId||previewRevision!=a.Revision)return;
        if(MessageBox.Show(this,
            "核准前 Cloud 會重新查光貿原發票完整折讓及剩餘額。\n核准只建立授權紀錄，不會開立折讓。確定繼續？",
            "確認主管核准",MessageBoxButtons.YesNo,MessageBoxIcon.Question)
           !=DialogResult.Yes)return;
        await Guarded(async()=>{
            var receipt=await client.ApproveAsync(a.ApplicationId,a.Revision,
                employeeNo.Text,password.Text);
            previewComplete=false;
            status.Text=$"申請已核准，revision {receipt.Revision}。尚未向光貿開立。";
        });
    }
    private async Task ReserveAsync()
    {
        var a=selected;if(a is null||a.Status!="approved")return;
        if(MessageBox.Show(this,
            "這只會預留折讓單號，不會執行光貿 g0401。確定預留？",
            "預留折讓單號",MessageBoxButtons.YesNo,MessageBoxIcon.Question)
           !=DialogResult.Yes)return;
        await Guarded(async()=>{
            var result=await client.ReserveAsync(a.ApplicationId,a.Revision,
                employeeNo.Text,password.Text);
            status.Text=$"已預留單號 {result.AllowanceNumber}；尚未正式開立。";
        });
    }
    private async Task StatusAsync()
    {
        var a=selected;if(a is null)return;
        await Guarded(async()=>{
            var r=await client.StatusAsync(a.ApplicationId,
                employeeNo.Text,password.Text);
            status.Text=$"原申請 {r.ApplicationId}：{r.Status}，Ledger：{r.LedgerStatus??"未配號"}，"+
                $"原號：{r.AllowanceNumber??"無"}。此查回不宣稱光貿已完成。";
        });
    }
    internal static void VerifySmokeLayout(IWin32Window owner)
    {
        using var form=new CloudAllowanceQueueForm();
        if(form.Text!="跨機折讓申請／覆核"||
           !passwordSmoke(form)||form.items.Columns.Count!=6||
           !form.status.Text.Contains("單號預留")||
           form.approve.Enabled||form.reserve.Enabled)
            throw new InvalidOperationException("Cloud allowance review layout safety smoke failed.");
    }
    private static bool passwordSmoke(CloudAllowanceQueueForm form)=>
        form.password.UseSystemPasswordChar&&form.password.MaxLength==128;
}
