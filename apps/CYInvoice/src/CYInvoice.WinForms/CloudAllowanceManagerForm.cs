using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

// 3.2-C4c. A CYID-scoped cross-device allowance review queue; never an
// independent AMEGO sender. The server revalidates the manager and official
// evidence at approve; preview alone is not permission to send.
// Application IDs and pending Ledger states are shown, not buyer PII.
internal sealed class CloudAllowanceManagerForm : Form
{
    private readonly HttpClient http=new(new HttpClientHandler{AllowAutoRedirect=false});
    private readonly CloudAllowanceClient? client;
    private readonly CancellationTokenSource lifetime=new();
    private readonly TextBox employeeNo=new(){Width=85,MaxLength=4,PlaceholderText="員工編號"};
    private readonly TextBox password=new(){Width=190,UseSystemPasswordChar=true,
        PlaceholderText="每次操作輸入 CYID 密碼"};
    private readonly TextBox searchId=new(){Width=270,MaxLength=100,
        PlaceholderText="原申請 ID（回覆遺失時查回）"};
    private readonly Label status=new(){Dock=DockStyle.Fill,AutoEllipsis=true,
        TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.DimGray};
    private readonly ListView queue=new(){Dock=DockStyle.Fill,
        View=View.Details,FullRowSelect=true,MultiSelect=false,GridLines=true,
        HideSelection=false};
    private readonly Button refresh=UiControls.StandardButton("重新整理");
    private readonly Button readback=UiControls.StandardButton("依 ID 查狀態");
    private readonly Button details=UiControls.StandardButton("查看申請原因");
    private readonly Button review=UiControls.StandardButton("接手覆核");
    private readonly Button preview=UiControls.StandardButton("預覽官方資料");
    private readonly Button approve=UiControls.StandardButton("主管核准");
    private readonly Button reserve=UiControls.StandardButton("預留號碼");
    private readonly Button inspect=UiControls.StandardButton("查單次送出證據");
    private readonly Button sendOnce=UiControls.StandardButton("單次送出 g0401（受控）");
    private readonly Button manualReview=UiControls.StandardButton("記錄人工異常分類");
    private readonly Button manualHistory=UiControls.StandardButton("查人工覆核稽核");
    private readonly Button close=UiControls.StandardButton("關閉");
    private bool busy;

    private CloudAllowanceManagerForm()
    {
        Text="CYID 跨機折讓（送出須獨立授權）";
        Font=new Font("Microsoft JhengHei UI",10F);
        StartPosition=FormStartPosition.CenterParent;
        ClientSize=new Size(990,580);MinimumSize=new Size(790,430);
        ShowIcon=false;BackColor=Color.White;
        queue.Columns.Add("申請 ID",255);
        queue.Columns.Add("狀態",85);
        queue.Columns.Add("版次",60);
        queue.Columns.Add("含稅金額",90);
        queue.Columns.Add("環境",75);
        queue.Columns.Add("更新時間",170);
        queue.SelectedIndexChanged+=(_,_)=>Actions();
        searchId.TextChanged+=(_,_)=>Actions();
        refresh.Click+=async(_,_)=>await ListAsync();
        readback.Click+=async(_,_)=>await StatusAsync();
        details.Click+=async(_,_)=>await DetailsAsync();
        review.Click+=async(_,_)=>await ReviewAsync();
        preview.Click+=async(_,_)=>await PreviewAsync();
        approve.Click+=async(_,_)=>await ApproveAsync();
        reserve.Click+=async(_,_)=>await ReserveAsync();
        inspect.Click+=async(_,_)=>await InspectAttemptAsync();
        sendOnce.Click+=async(_,_)=>await SendOnceAsync();
        manualReview.Click+=async(_,_)=>await RecordManualReviewAsync();
        manualHistory.Click+=async(_,_)=>await ShowManualHistoryAsync();
        close.Click+=(_,_)=>Close();
        FormClosing+=(_,_)=>lifetime.Cancel();
        FormClosed+=(_,_)=>{http.Dispose();lifetime.Dispose();};
        BuildLayout();Actions();
    }
    public CloudAllowanceManagerForm(LocalRepository repository):this()
    {
        ArgumentNullException.ThrowIfNull(repository);
        var setting=repository.Settings.LoadOrCreate();
        if(setting.CloudMode==CloudModes.LocalOnly||
           setting.CloudIdentityProvider!="CYID"||
           !setting.CloudEmployeeAuthorityReady||
           string.IsNullOrWhiteSpace(setting.CloudBaseUrl)||
           setting.CloudDeviceId.Length==0||
           setting.CloudWorkspaceId.Length==0)
            throw new InvalidOperationException("CYID Cloud Device is required.");
        client=new CloudAllowanceClient(http,
            new Uri(setting.CloudBaseUrl,UriKind.Absolute),
            repository.Settings.CloudDeviceToken(setting));
        status.Text="輸入本次 CYID 主管帳密，再按重新整理。申請與送出分開處理。";
    }
    private void BuildLayout()
    {
        var credentials=new FlowLayoutPanel{Dock=DockStyle.Fill,
            FlowDirection=FlowDirection.LeftToRight,WrapContents=true,
            AutoScroll=true,Padding=new Padding(4)};
        credentials.Controls.Add(new Label{AutoSize=true,Text="CYID 主管身份",Margin=new Padding(4,10,5,0)});
        credentials.Controls.Add(employeeNo);credentials.Controls.Add(password);
        credentials.Controls.Add(refresh);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,
            FlowDirection=FlowDirection.LeftToRight,WrapContents=true,
            AutoScroll=true,Padding=new Padding(4)};
        actions.Controls.Add(searchId);actions.Controls.Add(readback);
        actions.Controls.Add(details);
        actions.Controls.Add(review);actions.Controls.Add(preview);
        actions.Controls.Add(approve);actions.Controls.Add(reserve);
        actions.Controls.Add(inspect);actions.Controls.Add(sendOnce);
        actions.Controls.Add(manualReview);actions.Controls.Add(manualHistory);
        actions.Controls.Add(close);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,
            ColumnCount=1,RowCount=4,Padding=new Padding(12)};
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,53));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        root.Controls.Add(credentials,0,0);root.Controls.Add(queue,0,1);
        root.Controls.Add(actions,0,2);root.Controls.Add(status,0,3);
        Controls.Add(root);
        AcceptButton=refresh;CancelButton=close;
    }
    private CloudAllowanceSummary? Selected()=>
        queue.SelectedItems.Count==1?queue.SelectedItems[0].Tag as CloudAllowanceSummary:null;
    private void Actions()
    {
        var selected=Selected();
        bool ready=client is not null&&!busy;
        refresh.Enabled=ready;
        readback.Enabled=ready&&searchId.Text.Trim().Length>=8;
        details.Enabled=ready&&selected is not null;
        review.Enabled=ready&&selected?.Status=="open";
        preview.Enabled=ready&&selected?.Status=="review";
        approve.Enabled=ready&&selected?.Status=="review";
        reserve.Enabled=ready&&selected?.Status=="approved";
        inspect.Enabled=ready&&selected?.Status=="approved";
        sendOnce.Enabled=ready&&selected?.Status=="approved";
        manualReview.Enabled=ready&&selected?.Status=="approved";
        manualHistory.Enabled=ready&&selected?.Status=="approved";
        close.Enabled=!busy;
    }
    private async Task RunAsync(Func<string,string,Task> action)
    {
        if(client is null||busy)return;
        var no=employeeNo.Text.Trim();
        var pass=password.Text;
        if(no.Length!=4||!no.All(char.IsDigit)||pass.Length==0)
        {
            status.Text="每次操作都需要 4 位員工編號與本次 CYID 密碼。";
            return;
        }
        busy=true;Actions();
        try{await action(no,pass);}
        catch(OperationCanceledException)when(!lifetime.IsCancellationRequested)
        {status.Text="連線逾時：不可盲目重試，請先依原申請 ID 查回。";}
        catch(CloudApiException ex)
        {status.Text="Cloud 未確認："+ex.Code+"；請按原申請 ID 查回，勿重複建立或發送。";}
        catch(Exception)
        {status.Text="操作結果不明。請依原申請 ID 查回，切勿自行重送。";}
        finally{
            password.Clear();
            busy=false;Actions();
        }
    }
    private void Fill(IReadOnlyList<CloudAllowanceSummary> records)
    {
        queue.BeginUpdate();
        try{
            queue.Items.Clear();
            foreach(var record in records)
            {
                var row=new ListViewItem(record.ApplicationId){Tag=record};
                row.SubItems.Add(record.Status);
                row.SubItems.Add(record.Revision.ToString());
                row.SubItems.Add(record.RequestedGross);
                row.SubItems.Add(record.Environment);
                row.SubItems.Add(record.UpdatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
                queue.Items.Add(row);
            }
        }finally{queue.EndUpdate();}
    }
    private async Task ListAsync()=>await RunAsync(async(no,pass)=>{
        var rows=await client!.ListAsync(no,pass,limit:50,cancellationToken:lifetime.Token);
        Fill(rows);
        status.Text=$"已讀取 {rows.Count} 筆可見申請；跨機資料以 CYID 及 Cloud 狀態為準。";
    });
    private async Task StatusAsync()=>await RunAsync(async(no,pass)=>{
        var id=searchId.Text.Trim();
        var current=await client!.StatusAsync(id,no,pass,lifetime.Token);
        status.Text=$"原 ID {current.ApplicationId}／{current.Status}／rev {current.Revision}"+
            $"／Ledger {current.LedgerStatus??"尚未配號"}／單號 {current.AllowanceNumber??"無"}；未代表官方已完成。";
    });
    private async Task DetailsAsync()
    {
        var selected=Selected();if(selected is null)return;
        await RunAsync(async(no,pass)=>{
            var detail=await client!.GetAsync(selected.ApplicationId,no,pass,lifetime.Token);
            searchId.Text=detail.ApplicationId;
            MessageBox.Show(this,
                "申請 ID：" + detail.ApplicationId +
                "\n折讓日：" + detail.AllowanceDay +
                "\n含稅金額：" + detail.RequestedGross +
                "\n申請原因：" + detail.Reason +
                "\n\n此資料不代表光貿已開立折讓。",
                "CYID Cloud 折讓申請（受權限保護）",
                MessageBoxButtons.OK,MessageBoxIcon.Information);
        });
    }
    private async Task ReviewAsync()
    {
        var row=Selected();if(row is null)return;
        await RunAsync(async(no,pass)=>{
            var res=await client!.ReviewAsync(row.ApplicationId,row.Revision,no,pass,lifetime.Token);
            searchId.Text=res.ApplicationId;
            status.Text=$"已接手覆核，revision {res.Revision}。請刷新並預覽官方資訊。";
            queue.Items.Clear();
        });
    }
    private async Task PreviewAsync()
    {
        var row=Selected();if(row is null)return;
        await RunAsync(async(no,pass)=>{
            var info=await client!.PreviewAsync(row.ApplicationId,row.Revision,no,pass,lifetime.Token);
            searchId.Text=info.ApplicationId;
            status.Text=$"官方唯讀：{info.OriginalInvoiceNumber}；上傳 {info.OfficialUploadStatus}／"+
                $"歷史折讓 {info.OfficialAllowanceCount}；摘要完整 {info.OfficialSummaryComplete}。此預覽不能核准發送。";
        });
    }
    private async Task ApproveAsync()
    {
        var row=Selected();if(row is null)return;
        if(MessageBox.Show(this,"核准會重新查詢光貿官方資料並建立不可變 Grant，但不會開立折讓。確定核准？",
           "確認主管核准",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        await RunAsync(async(no,pass)=>{
            var res=await client!.ApproveAsync(row.ApplicationId,row.Revision,no,pass,lifetime.Token);
            searchId.Text=res.ApplicationId;
            status.Text=$"已建立核准 Grant；rev {res.Revision}，尚未配號或發送。";
            queue.Items.Clear();
        });
    }
    private async Task ReserveAsync()
    {
        var row=Selected();if(row is null)return;
        if(MessageBox.Show(this,"僅預留號碼，不會發送 g0401。任何逾時需以原申請 ID 查回。確定？",
           "預留 Cloud 折讓單號",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;
        await RunAsync(async(no,pass)=>{
            var res=await client!.ReserveAsync(row.ApplicationId,row.Revision,no,pass,lifetime.Token);
            searchId.Text=res.ApplicationId;
            status.Text=$"已預留 {res.AllowanceNumber}；不代表光貿已收到折讓，不能自行送出。";
            queue.Items.Clear();
        });
    }
    private async Task InspectAttemptAsync()
    {
        var row=Selected();if(row is null)return;
        await RunAsync(async(no,pass)=>{
            var evidence=await client!.InspectAttemptAsync(row.ApplicationId,
                row.Revision,no,pass,lifetime.Token);
            searchId.Text=evidence.ApplicationId;
            status.Text=$"折讓 {evidence.AllowanceNumber}／Ledger {evidence.LedgerStatus}；"+
                $"官方內容回查紀錄：{(evidence.RecordedExactReadback?"有（不可歸因）":"尚無")}。"+
                "本次送出未獲官方歸因，禁止以此宣稱成功或重送。";
        });
    }
    private async Task SendOnceAsync()
    {
        var row=Selected();if(row is null)return;
        await RunAsync(async(no,pass)=>{
            // Read ONLY before presenting the irreversible, one-shot action.
            // Missing or uncertain prior evidence never authorizes another POST.
            var evidence=await client!.InspectAttemptAsync(row.ApplicationId,
                row.Revision,no,pass,lifetime.Token);
            if(evidence.LedgerStatus!="reserved")
            {
                searchId.Text=evidence.ApplicationId;
                status.Text="原折讓單號 "+evidence.AllowanceNumber+
                    " 已經處於 "+evidence.LedgerStatus+
                    "；只能人工查回、不得自動或手動重送。";
                return;
            }
            var yes=MessageBox.Show(this,
                "即將以原申請 ID "+evidence.ApplicationId+
                "、原折讓號 "+evidence.AllowanceNumber+
                " 執行唯一一次光貿 g0401。\n"+
                "這不是預覽。即使回覆代碼為 0 或官方內容完全吻合，"+
                "系統仍將保留結果不明、須人工核對，並禁止再次發送／換號。\n\n"+
                "請確認已取得本次測試／業務操作授權，且原 Cloudflare 正式送出閘門已獨立核准。",
                "不可重複的折讓送出",
                MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if(yes!=DialogResult.Yes)return;
            // A single outbound mutation; neither UI nor HTTP client retries.
            var result=await client!.SendOnceAsync(row.ApplicationId,row.Revision,
                no,pass,lifetime.Token);
            searchId.Text=result.ApplicationId;
            status.Text="原號 "+result.AllowanceNumber+"／"+result.ManualDisposition+
                "；Ledger 仍 sending。必須人工歸因與官方查回，不能重送或視為已完成。";
            queue.Items.Clear();
        });
    }
    private async Task RecordManualReviewAsync()
    {
        var row=Selected();if(row is null)return;
        await RunAsync(async(no,pass)=>{
            var attempt=await client!.InspectAttemptAsync(row.ApplicationId,
                row.Revision,no,pass,lifetime.Token);
            searchId.Text=attempt.ApplicationId;
            if(attempt.LedgerStatus is not ("sending" or "occupied_external"))
            {
                status.Text="只有已執行的原號可加入人工覆核稽核；未送出的預留不能標記。";
                return;
            }
            using var dialog=new Form{
                Text="原號人工覆核（只記錄，不確認財務完成）",
                ClientSize=new Size(630,240),MinimumSize=new Size(590,230),
                StartPosition=FormStartPosition.CenterParent,
                FormBorderStyle=FormBorderStyle.FixedDialog,
                MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false,
                Font=new Font("Microsoft JhengHei UI",10F)
            };
            var root=new TableLayoutPanel{
                Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=1,RowCount=3
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
            var info=new Label{
                Dock=DockStyle.Fill,
                Text="申請："+attempt.ApplicationId+"\n原號："+attempt.AllowanceNumber+
                    "／"+attempt.LedgerStatus+
                    "\n本次僅記錄主管對既有證據的分類，不會變更 Ledger、" +
                    "確認光貿成功、重送 g0401 或改配新號。"
            };
            var choices=new ComboBox{
                Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList
            };
            var allowed=new List<(string Code,string Name)>{
                ("unknown_outcome","結果不明／待調查"),
                ("number_collision_investigation","外部占號／待查證"),
                ("provider_rejection_investigation","光貿拒絕／待查證")
            };
            if(attempt.RecordedExactReadback)
                allowed.Insert(0,("full_match_unattributed","全項吻合，但本次送出無法歸因"));
            choices.Items.AddRange(allowed.Select(x=>x.Name).ToArray());
            choices.SelectedIndex=0;
            var buttons=new FlowLayoutPanel{
                Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft
            };
            var record=UiControls.StandardButton("確認寫入不可變稽核");
            record.Width=190;record.DialogResult=DialogResult.OK;
            var cancel=UiControls.StandardButton("取消");
            cancel.DialogResult=DialogResult.Cancel;
            buttons.Controls.Add(cancel);buttons.Controls.Add(record);
            root.Controls.Add(info,0,0);root.Controls.Add(choices,0,1);
            root.Controls.Add(buttons,0,2);dialog.Controls.Add(root);
            dialog.CancelButton=cancel;
            if(dialog.ShowDialog(this)!=DialogResult.OK||
               choices.SelectedIndex<0)return;
            var code=allowed[choices.SelectedIndex].Code;
            // One original, non-reused ID for the user's single audit action.
            // A lost response is checked with manual-history, never resubmitted.
            var reviewId=Guid.NewGuid().ToString("N");
            var receipt=await client!.RecordManualReviewAsync(
                row.ApplicationId,row.Revision,reviewId,code,no,pass,lifetime.Token);
            status.Text="人工分類已留存："+receipt.ReviewId+"；原 Ledger "+
                receipt.LedgerStatus+" 不變。不代表本次交易成功。";
        });
    }
    private async Task ShowManualHistoryAsync()
    {
        var row=Selected();if(row is null)return;
        await RunAsync(async(no,pass)=>{
            var records=await client!.ManualHistoryAsync(row.ApplicationId,
                row.Revision,no,pass,lifetime.Token);
            searchId.Text=row.ApplicationId;
            MessageBox.Show(this,records.Count==0?"目前沒有人工覆核紀錄。":
                string.Join("\n",records.Select(r=>r.ReviewId+"／"+
                    r.LedgerStatus+"／"+r.Disposition)),
                "Cloud 人工覆核稽核（唯讀）",MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            status.Text="已依原申請 ID 查回 "+records.Count+
                " 筆人工覆核分類；所有紀錄都不代表光貿官方完成。";
        });
    }
    internal static void VerifySmokeLayout()
    {
        using var form=new CloudAllowanceManagerForm();
        if(form.Text!="CYID 跨機折讓（送出須獨立授權）"||
           form.ClientSize.Width<790||form.queue.Columns.Count!=6||
           !form.password.UseSystemPasswordChar||
           form.queue.MultiSelect||form.reserve.Enabled||form.approve.Enabled||
           form.sendOnce.Enabled||form.inspect.Enabled||
           form.manualReview.Enabled||form.manualHistory.Enabled)
            throw new InvalidOperationException("Cloud allowance manager layout/authority gate invalid.");
    }
}
