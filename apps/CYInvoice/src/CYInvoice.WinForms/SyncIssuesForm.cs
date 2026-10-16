using CYInvoice.Core;
using CYInvoice.Core.Amego;
using CYInvoice.Core.Cloud;
using System.Security.Cryptography;
using System.Text;
using CYInvoice.Core.Invoicing;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class SyncIssuesForm : Form
{
    internal const string ReadStateScope = "upload-issues-read";
    private static readonly int[] DefaultWidths = [145, 130, 110, 150, 0, 90];

    private readonly bool processing;
    private readonly LocalRepository repository;
    private readonly InvoiceSyncIssueStore issueStore;
    private readonly InvoiceSyncStateStore stateStore;
    private readonly FailedInvoiceRecordStore failedStore;
    private readonly EmployeeVoidWorkflowService voidWorkflow;
    private readonly EmployeeAllowanceWorkflowService allowanceWorkflow;
    private readonly EmployeeAllowanceVoidWorkflowService allowanceVoidWorkflow;
    private readonly InvoiceAdministrativeClosureService administrativeClosure;
    private readonly string accountKey;
    private DateTimeOffset? lastRead;
    private bool manualReviewBusy;
    private bool settingColumnWidths;
    private readonly ToolTip contentToolTip = new();
    private string visibleToolTip = string.Empty;

    private readonly bool startupSmokeTest;
    private const int MaximumVisibleRows = 12;
    private readonly NativeListViewHost listHost = new(fontSize: 10F, rowHeight: 22);
    private ListView list => listHost.List;
    private readonly Panel body = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label empty = new()
    {
        Text = "無資料", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.DimGray, BorderStyle = BorderStyle.FixedSingle, Margin = Padding.Empty,
    };
    private TableLayoutPanel root = null!;
    private readonly Label summary = new()
    {
        AutoSize = true,
        ForeColor = Color.DimGray,
        Anchor = AnchorStyles.Left,
    };
    private readonly Button archiveFailed = UiControls.StandardButton("封存勾選");
    private readonly Button archiveAllFailed = UiControls.StandardButton("封存全部失敗紀錄");

    public SyncIssuesForm(LocalRepository repository, bool processing = false, bool startupSmokeTest = false)
    {
        this.processing = processing;
        this.startupSmokeTest = startupSmokeTest;
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        issueStore = new InvoiceSyncIssueStore(repository.DataDirectory);
        stateStore = new InvoiceSyncStateStore(repository.DataDirectory);
        failedStore = new FailedInvoiceRecordStore(repository.DataDirectory);
        voidWorkflow = new EmployeeVoidWorkflowService(repository);
        allowanceWorkflow = new EmployeeAllowanceWorkflowService(repository);
        allowanceVoidWorkflow = new EmployeeAllowanceVoidWorkflowService(repository);
        administrativeClosure = new InvoiceAdministrativeClosureService(repository);
        accountKey = CurrentAccountKey();
        lastRead = stateStore.LastSuccess(accountKey, ReadStateScope);

        Text = processing ? "處理中" : "上傳問題";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 0);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(960, 520);
        Font = new Font("Microsoft JhengHei UI", 10F);
        BackColor = Color.White;
        ShowIcon = false;

        BuildLayout();
        if (!processing) MarkVisibleIssuesRead();
        ReloadAll();
        Shown += (_, _) => FitToRows();
    }

    private void BuildLayout()
    {
        list.CheckBoxes = !processing;
        list.MultiSelect = true;
        list.HideSelection = false;
        list.GridLines = true;
        list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        ConfigureList();
        var reload = UiControls.StandardButton("重新整理");
        reload.Click += (_, _) => ReloadAll();
        list.DoubleClick += async (_, _) => await ShowSelectedIssueDetailsAsync();
        list.ItemCheck += (_, eventArgs) =>
        {
            if (eventArgs.Index < 0 || eventArgs.Index >= list.Items.Count) return;
            if (eventArgs.NewValue == CheckState.Checked && (processing || list.Items[eventArgs.Index].Tag is not InvoiceRecord))
                eventArgs.NewValue = CheckState.Unchecked;
        };
        list.ItemChecked += (_, _) => BeginInvoke((Action)UpdateFailedButtons);
        list.ClientSizeChanged += (_, _) => LayoutColumns();
        list.ColumnWidthChanging += (_, eventArgs) =>
        {
            if (settingColumnWidths) return;
            eventArgs.Cancel = true;
            eventArgs.NewWidth = list.Columns[eventArgs.ColumnIndex].Width;
        };
        list.MouseMove += ShowClippedContentToolTip;
        list.MouseLeave += (_, _) => ClearContentToolTip();

        archiveFailed.Enabled = false;
        archiveFailed.Click += (_, _) => ArchiveCheckedFailed();
        archiveAllFailed.Enabled = false;
        archiveAllFailed.Click += (_, _) => ArchiveAllFailed();

        var close = UiControls.StandardButton("關閉");
        close.Click += (_, _) => Close();

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(summary, 0, 0);
        reload.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        header.Controls.Add(reload, 1, 0);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var failedActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        if (!processing)
        {
            failedActions.Controls.Add(archiveFailed);
            failedActions.Controls.Add(archiveAllFailed);
            var history=UiControls.StandardButton("檢視封存紀錄");
            history.Click+=(_,_)=>ShowArchivedFailed();
            failedActions.Controls.Add(history);
            var cfg=repository.Settings.LoadOrCreate();
            if(cfg.CloudMode==CloudModes.CloudPreferred &&
               cfg.CloudIdentityProvider=="CYID" &&
               cfg.CloudEmployeeAuthorityReady &&
               !string.IsNullOrWhiteSpace(cfg.CloudWorkspaceId) &&
               !string.IsNullOrWhiteSpace(cfg.CloudDeviceId) &&
               !string.IsNullOrWhiteSpace(cfg.CloudBaseUrl) &&
               !string.IsNullOrWhiteSpace(repository.Settings.CloudDeviceToken(cfg)))
            {
                var report=UiControls.StandardButton("回報選取至 CYID");
                report.Click+=async(_,_)=>await ReportSelectedUploadIssuesAsync();
                failedActions.Controls.Add(report);
            }
        }
        else
        {
            // V3: a dedicated CYID / multi-device queue. Local manual review
            // remains independent and unchanged; no Built-in Cloud fallback.
            var cfg = repository.Settings.LoadOrCreate();
            if (cfg.CloudMode == CloudModes.CloudPreferred
                && cfg.CloudIdentityProvider == "CYID"
                && cfg.CloudEmployeeAuthorityReady
                && cfg.CloudWorkspaceId.Length != 0
                && cfg.CloudDeviceId.Length != 0
                && !string.IsNullOrWhiteSpace(cfg.CloudBaseUrl)
                && !string.IsNullOrWhiteSpace(repository.Settings.CloudDeviceToken(cfg)))
            {
                var cloudQueue = UiControls.StandardButton("CYID 跨機折讓");
                cloudQueue.Click += (_, _) =>
                {
                    try
                    {
                        var current = repository.Settings.LoadOrCreate();
                        var token = repository.Settings.CloudDeviceToken(current);
                        using var manager = new CloudAllowanceManagerForm(
                            repository, current.CloudBaseUrl, token);
                        manager.ShowDialog(this);
                        ReloadAll();
                    }
                    catch (Exception error)
                    {
                        MessageBox.Show(this, "無法開啟 CYID 跨機折讓：" + error.Message,
                            "雲端覆核", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };
                failedActions.Controls.Add(cloudQueue);
                // V3 g0501 is a separate queue from g0401. A regular CYID
                // employee can create/read back an application; manager-only
                // actions are authorized again on the Cloud Worker.
                var allowanceVoidQueue=UiControls.StandardButton("CYID 折讓作廢");
                allowanceVoidQueue.Click+=(_,_)=>
                {
                    try
                    {
                        var current=repository.Settings.LoadOrCreate();
                        if(current.CloudMode!=CloudModes.CloudPreferred||
                           current.CloudIdentityProvider!="CYID"||
                           !current.CloudEmployeeAuthorityReady||
                           string.IsNullOrWhiteSpace(current.CloudWorkspaceId))
                        {
                            MessageBox.Show(this,"CYID 裝置尚未就緒，無法開啟跨機折讓作廢。",
                                "CYID 作廢",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                            return;
                        }
                        var token=repository.Settings.CloudDeviceToken(current);
                        using var manager=new CloudAllowanceVoidManagerForm(
                            repository,current.CloudBaseUrl,token,current.CloudWorkspaceId);
                        manager.ShowDialog(this);
                        ReloadAll();
                    }
                    catch(Exception error)
                    {
                        MessageBox.Show(this,"無法開啟 CYID 折讓作廢："+error.Message,
                            "雲端作廢",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                    }
                };
                failedActions.Controls.Add(allowanceVoidQueue);
                var cloudExceptions = UiControls.StandardButton("CYID 例外清單");
                cloudExceptions.Click += async (_, _) => await ShowCloudExceptionsAsync();
                failedActions.Controls.Add(cloudExceptions);
            }
        }
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Margin = new Padding(0, 4, 0, 0);
        bottom.Controls.Add(failedActions, 0, 0);
        bottom.Controls.Add(close, 1, 0);

        root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
            Tag = "upload-issues-root",
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.Controls.Add(header, 0, 0);
        body.Controls.Add(listHost);
        body.Controls.Add(empty);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(bottom, 0, 2);
        Controls.Add(root);
        Resize += (_, _) => LayoutColumns();
    }

    private async Task ReportSelectedUploadIssuesAsync()
    {
        if(processing||manualReviewBusy)return;
        var cfg=repository.Settings.LoadOrCreate();
        var deviceToken=repository.Settings.CloudDeviceToken(cfg);
        if(cfg.CloudMode!=CloudModes.CloudPreferred||
           cfg.CloudIdentityProvider!="CYID"||
           !cfg.CloudEmployeeAuthorityReady||
           string.IsNullOrWhiteSpace(cfg.CloudWorkspaceId)||
           string.IsNullOrWhiteSpace(cfg.CloudDeviceId)||
           string.IsNullOrWhiteSpace(cfg.CloudBaseUrl)||
           string.IsNullOrWhiteSpace(deviceToken))
        {
            MessageBox.Show(this,"尚未完成 CYID Cloud 裝置驗證。",
                "異常回報",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return;
        }
        var selected=list.SelectedItems.Cast<ListViewItem>()
            .Select(row=>row.Tag)
            .Select(tag=>tag switch{
                InvoiceSyncIssue issue when issue.ResolvedUtc is null &&
                    issue.IssueType is (
                        InvoiceSyncIssueTypes.InvoiceListFailed or
                        InvoiceSyncIssueTypes.QueryFailed or
                        InvoiceSyncIssueTypes.RemoteNotFound or
                        InvoiceSyncIssueTypes.UnknownRemoteNotFound or
                        InvoiceSyncIssueTypes.ReconciliationFailed or
                        InvoiceSyncIssueTypes.AmbiguousMatch or
                        InvoiceSyncIssueTypes.LocalWriteFailed)
                    =>(Key:"sync:"+issue.Id,Code:issue.IssueType),
                InvoiceRecord record when record.InvoiceState==InvoiceStates.Failed
                    =>(Key:"failed:"+record.Id,Code:"upload_failed"),
                _=>(Key:string.Empty,Code:string.Empty)
            })
            .Where(x=>x.Key.Length>0)
            .Distinct()
            .Take(11).ToArray();
        if(selected.Length==0){
            MessageBox.Show(this,"請先選取需要回報的上傳問題或開立失敗紀錄。",
                "異常回報",MessageBoxButtons.OK,MessageBoxIcon.Information);
            return;
        }
        if(selected.Length>10){
            MessageBox.Show(this,"一次最多選取 10 筆，請減少選取項目。",
                "異常回報",MessageBoxButtons.OK,MessageBoxIcon.Information);
            return;
        }
        if(MessageBox.Show(this,
            $"確認將 {selected.Length} 筆本機異常回報至 CYID 管理員清單？\n"+
            "只會傳送異常種類與不可逆來源識別，不傳買受人、訂單或發票內容。\n"+
            "回報不代表光貿已驗證，也不會重送發票。",
            "回報 CYID 上傳問題",MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)
            return;
        using var login=new EmployeeAdminLoginForm(repository,
            "CYID 上傳問題－員工驗證",administratorRequired:false,
            collectCredentialsOnly:true);
        if(login.ShowDialog(this)!=DialogResult.OK)return;
        var saved=0;var already=0;var unknown=0;
        SetManualReviewBusy(true);
        try{
            using var http=new HttpClient();
            var client=new CloudWorkItemClient(http,
                new Uri(cfg.CloudBaseUrl,UriKind.Absolute),deviceToken,cfg.CloudWorkspaceId);
            foreach(var issue in selected)
            {
                // Hash a Device-scoped durable local issue identity, not its
                // order/invoice/buyer/message. Re-selecting the same item
                // produces the exact same D1 Work Item ID and dedupe hash.
                var identity=$"upload-report|{cfg.CloudWorkspaceId}|"+
                    $"{cfg.CloudDeviceId}|{cfg.Environment}|{issue.Key}|{issue.Code}";
                var hash=Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
                var draft=new CloudUploadIssueReport(
                    "wi_issue_"+hash[..40],cfg.Environment,hash,issue.Code);
                try{
                    await client.ReportUploadIssueAsync(draft,
                        login.EnteredEmployeeNo,login.AuthenticatedPassword);
                    saved++;
                }catch(CloudApiException e)when(e.Code=="WORK_ITEM_CONFLICT"){
                    already++;
                }catch{
                    unknown++;
                    // A timeout may mean D1 committed; no automatic retry.
                    break;
                }
            }
            MessageBox.Show(this,
                $"已收到確認：{saved} 筆；已存在或衝突：{already} 筆；結果不明：{unknown} 筆。\n"+
                "結果不明時請由管理員在 CYID 例外清單核對，勿盲目重送。\n"+
                "本機異常未被刪除或標記已解決。",
                "異常回報結果",MessageBoxButtons.OK,
                unknown>0?MessageBoxIcon.Warning:MessageBoxIcon.Information);
        }catch(Exception error){
            MessageBox.Show(this,"無法送出 CYID 異常回報："+error.Message+
                "\n本機異常紀錄均未變更。","回報未完成",
                MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }finally{SetManualReviewBusy(false);}
    }

    // The reason is deliberately an enumerated administrative outcome.
    // It never asserts an official AMEGO invoice status.
    private static string? PromptUploadIssueArchiveReason(Form owner)
    {
        using var form=new Form {
            Text="CYID 上傳問題－人工封存原因",
            StartPosition=FormStartPosition.CenterParent,
            ClientSize=new Size(400,150),ShowIcon=false,
            FormBorderStyle=FormBorderStyle.FixedDialog,
            MinimizeBox=false,MaximizeBox=false
        };
        var label=new Label {
            Location=new Point(12,10),Size=new Size(375,40),
            Text="請選擇明確的人工處理原因（不等於官方結果）。"
        };
        var choices=new ComboBox {
            Location=new Point(12,53),Width=375,
            DropDownStyle=ComboBoxStyle.DropDownList
        };
        choices.Items.AddRange(new object[]{
            "本機問題已處理","已人工查核光貿",
            "重複回報","確認無需處理","仍需後續追蹤"
        });
        var accept=UiControls.StandardButton("確定");
        accept.Location=new Point(218,102);
        accept.Click+=(_,_)=>{
            if(choices.SelectedIndex<0){
                MessageBox.Show(form,"必須先選取原因。",
                    "缺少結案原因",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                return;
            }
            form.DialogResult=DialogResult.OK;
        };
        var cancel=UiControls.StandardButton("取消");
        cancel.Location=new Point(307,102);
        cancel.DialogResult=DialogResult.Cancel;
        form.Controls.AddRange(new Control[]{label,choices,accept,cancel});
        form.CancelButton=cancel;
        if(form.ShowDialog(owner)!=DialogResult.OK)return null;
        return choices.SelectedIndex switch {
            0=>"resolved_locally",
            1=>"checked_official",
            2=>"duplicate_report",
            3=>"not_actionable",
            4=>"needs_followup",
            _=>null
        };
    }

    // Read-only window for the already-existing manager Work Item list. Local
    // processing and upload failures remain untouched; no Cloud state change.
    private async Task ShowCloudExceptionsAsync()
    {
        var cfg=repository.Settings.LoadOrCreate();
        var token=repository.Settings.CloudDeviceToken(cfg);
        if(cfg.CloudMode!=CloudModes.CloudPreferred ||
           cfg.CloudIdentityProvider!="CYID" ||
           !cfg.CloudEmployeeAuthorityReady ||
           string.IsNullOrWhiteSpace(cfg.CloudWorkspaceId) ||
           string.IsNullOrWhiteSpace(cfg.CloudBaseUrl) ||
           string.IsNullOrWhiteSpace(token))
        {
            MessageBox.Show(this,"CYID 雲端裝置尚未完成驗證。",
                "例外清單",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return;
        }
        using var login=new EmployeeAdminLoginForm(repository,
            "CYID 例外清單－管理員驗證",collectCredentialsOnly:true);
        if(login.ShowDialog(this)!=DialogResult.OK)return;
        using var http=new HttpClient();
        var client=new CloudWorkItemClient(http,
            new Uri(cfg.CloudBaseUrl,UriKind.Absolute),token,cfg.CloudWorkspaceId);
        using var dialog=new Form{
            Text="CYID 跨裝置例外工作單（唯讀）",
            StartPosition=FormStartPosition.CenterParent,
            ClientSize=new Size(810,410),MinimumSize=new Size(760,340),
            ShowIcon=false,Font=new Font("Microsoft JhengHei UI",10F)
        };
        var grid=new ListView{
            Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,
            HideSelection=false,GridLines=true,MultiSelect=false
        };
        grid.Columns.Add("更新時間",153);
        grid.Columns.Add("類別",115);
        grid.Columns.Add("環境",80);
        grid.Columns.Add("狀態",100);
        grid.Columns.Add("工作單 ID",315);
        var caption=new Label{
            Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
            Text="僅顯示 CYID 管理員可見的例外工作單，不包含發票明細或密鑰。"
        };
        var previous=UiControls.StandardButton("上一頁");
        var next=UiControls.StandardButton("下一頁");
        var copy=UiControls.StandardButton("複製工作單 ID");
        var review=UiControls.StandardButton("開始覆核");
        var archive=UiControls.StandardButton("人工封存");
        review.Enabled=false;
        archive.Enabled=false;
        var close=UiControls.StandardButton("關閉");
        close.Click+=(_,_)=>dialog.Close();
        copy.Click+=(_,_)=>{
            if(grid.SelectedItems.Count!=1)return;
            if(grid.SelectedItems[0].Tag is CloudWorkItemSummary entry)
                Clipboard.SetText(entry.ItemId);
        };
        var bottom=new FlowLayoutPanel{
            Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,
            WrapContents=false
        };
        foreach(var button in new[]{close,copy,archive,review,next,previous})
            bottom.Controls.Add(button);
        var layout=new TableLayoutPanel{
            Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,35));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
        layout.Controls.Add(caption,0,0);
        layout.Controls.Add(grid,0,1);
        layout.Controls.Add(bottom,0,2);
        dialog.Controls.Add(layout);
        var history=new Stack<CloudWorkItemCursor?>();
        CloudWorkItemCursor? cursor=null;
        CloudWorkItemPage? page=null;
        bool busy=false;
        async Task ReloadAsync(CloudWorkItemCursor? target,bool back)
        {
            if(busy||dialog.IsDisposed)return;
            busy=true;
            foreach(var button in new[]{previous,next,copy,review,archive})
                button.Enabled=false;
            try
            {
                var result=await client.ListAsync(login.EnteredEmployeeNo,
                    login.AuthenticatedPassword,20,cursor:target);
                if(dialog.IsDisposed)return;
                if(back && history.Count>0)history.Pop();
                if(!back && page is not null)history.Push(cursor);
                cursor=target;
                page=result;
                grid.BeginUpdate();
                try
                {
                    grid.Items.Clear();
                    foreach(var item in result.Items)
                    {
                        var kind=item.Kind switch{
                            "VOID"=>"作廢",
                            "ALLOWANCE"=>"折讓",
                            "ALLOWANCE_VOID"=>"折讓作廢",
                            "UPLOAD_ISSUE"=>item.IssueCode switch{
                                "invoice_list_failed"=>"官方清單查詢失敗",
                                "query_failed"=>"發票查回失敗",
                                "remote_not_found"=>"光貿查無發票",
                                "unknown_remote_not_found"=>"結果不明／光貿查無",
                                "reconciliation_failed"=>"發票同步核對失敗",
                                "ambiguous_match"=>"發票比對無法唯一對應",
                                "local_write_failed"=>"本機資料寫入失敗",
                                "duplicate_order_id"=>"重複訂單編號",
                                _=>"開立上傳失敗"
                            },
                            _=>"其他例外"
                        };
                        var state=item.Status switch{
                            "open"=>"待覆核",
                            "review"=>"覆核中",
                            "closed"=>"已結案",
                            _=>"待確認"
                        };
                        var row=new ListViewItem(item.UpdatedAtUtc.ToLocalTime()
                            .ToString("yyyy/MM/dd HH:mm"));
                        row.SubItems.Add(kind);
                        row.SubItems.Add(item.Environment=="production"?"正式":"測試");
                        row.SubItems.Add(state);
                        row.SubItems.Add(item.ItemId);
                        row.Tag=item;
                        grid.Items.Add(row);
                    }
                }
                finally{grid.EndUpdate();}
                caption.Text=$"CYID 例外工作單｜本頁 {result.Items.Count} 筆"+
                    "｜清單僅供查看，不會結案或重送光貿。";
            }
            catch(Exception error)
            {
                if(!dialog.IsDisposed)
                    MessageBox.Show(dialog,"CYID 工作單查詢失敗："+error.Message+
                        "\n既有工作單與本機資料均未變更。",
                        "例外清單",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
            finally
            {
                busy=false;
                if(!dialog.IsDisposed){
                    previous.Enabled=history.Count>0;
                    next.Enabled=page?.NextCursor is not null;
                    var entry=grid.SelectedItems.Count==1?
                        grid.SelectedItems[0].Tag as CloudWorkItemSummary:null;
                    copy.Enabled=entry is not null;
                    review.Enabled=entry?.Kind=="UPLOAD_ISSUE"&&entry.Status=="open";
                    archive.Enabled=entry?.Kind=="UPLOAD_ISSUE"&&entry.Status=="review";
                }
            }
        }
        async Task ApplyManagerActionAsync(bool closeIssue)
        {
            if(busy || grid.SelectedItems.Count!=1 ||
               grid.SelectedItems[0].Tag is not CloudWorkItemSummary chosen ||
               chosen.Kind!="UPLOAD_ISSUE" ||
               chosen.Status!=(closeIssue?"review":"open"))return;
            var reason=closeIssue?PromptUploadIssueArchiveReason(dialog):null;
            if(closeIssue && reason is null)return;
            if(MessageBox.Show(dialog,closeIssue
                ?"將這筆裝置回報標記為已人工處理，並留下原因？不會修改任何發票。"
                :"開始覆核此筆裝置回報？不會呼叫光貿。",
                "CYID 例外工作單",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            busy=true;
            review.Enabled=archive.Enabled=copy.Enabled=previous.Enabled=next.Enabled=false;
            try
            {
                var current=await client.GetAsync(chosen.ItemId,
                    login.EnteredEmployeeNo,login.AuthenticatedPassword);
                if(current is null||current.Kind!="UPLOAD_ISSUE"||
                   current.Status!=chosen.Status||current.Revision!=chosen.Revision)
                    throw new InvalidOperationException("工作單已改變，需重新查詢最新狀態。");
                var updated=await client.TransitionAsync(current,
                    closeIssue?"closed":"review",login.EnteredEmployeeNo,
                    login.AuthenticatedPassword,resolutionCode:reason);
                MessageBox.Show(dialog,closeIssue
                    ?$"工作單 {updated.ItemId} 已人工封存。這不代表光貿確認成功。"
                    :$"工作單 {updated.ItemId} 已開始覆核。尚未影響任何發票。",
                    "CYID 工作單已更新",MessageBoxButtons.OK,MessageBoxIcon.Information);
                dialog.Close();
            }
            catch(Exception error)
            {
                if(!dialog.IsDisposed)MessageBox.Show(dialog,
                    "處理結果未確認："+error.Message+
                    "\n請重新查詢原工作單，不得自動重送。",
                    "處理待查回",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
            finally
            {
                busy=false;
                if(!dialog.IsDisposed){
                    review.Enabled=chosen.Status=="open";
                    archive.Enabled=chosen.Status=="review";
                    copy.Enabled=true;
                    previous.Enabled=history.Count>0;
                    next.Enabled=page?.NextCursor is not null;
                }
            }
        }
        review.Click+=async(_,_)=>await ApplyManagerActionAsync(false);
        archive.Click+=async(_,_)=>await ApplyManagerActionAsync(true);
        next.Click+=async(_,_)=>{
            if(page?.NextCursor is { } more)await ReloadAsync(more,false);
        };
        previous.Click+=async(_,_)=>{
            if(history.TryPeek(out var earlier))await ReloadAsync(earlier,true);
        };
        grid.SelectedIndexChanged+=(_,_)=>{
            var entry=grid.SelectedItems.Count==1?
                grid.SelectedItems[0].Tag as CloudWorkItemSummary:null;
            copy.Enabled=!busy&&entry is not null;
            review.Enabled=!busy&&entry?.Kind=="UPLOAD_ISSUE"&&entry.Status=="open";
            archive.Enabled=!busy&&entry?.Kind=="UPLOAD_ISSUE"&&entry.Status=="review";
        };
        dialog.Shown+=async(_,_)=>await ReloadAsync(null,false);
        await Task.CompletedTask;
        dialog.ShowDialog(this);
    }

    private void ConfigureList()
    {
        list.Columns.Add("日期/時間", DefaultWidths[0], HorizontalAlignment.Left);
        list.Columns.Add("類型", DefaultWidths[1], HorizontalAlignment.Left);
        list.Columns.Add("發票號碼", DefaultWidths[2], HorizontalAlignment.Left);
        list.Columns.Add("訂單編號", DefaultWidths[3], HorizontalAlignment.Left);
        list.Columns.Add("內容", DefaultWidths[4], HorizontalAlignment.Left);
        list.Columns.Add("狀態", DefaultWidths[5], HorizontalAlignment.Center);
    }

    private void ReloadAll()
    {
        try
        {
            lastRead = stateStore.LastSuccess(accountKey, ReadStateScope);
            var queue = new InvoiceWorkQueue(repository).Load();
            var issues = processing ? queue.ProcessingIssues : queue.UploadIssues;
            var failed = processing ? Array.Empty<InvoiceRecord>() : CurrentFailedRecords();
            var rows = new List<(string SortKey, ListViewItem Row)>();

            foreach (var issue in issues)
            {
                var state = IssueState(issue);
                var time = issue.CreatedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
                var row = new ListViewItem(ShortIssueTime(time));
                row.SubItems.Add(processing && IsAllowanceManualReview(issue) ? "折讓處理中" : DisplayType(issue.IssueType));
                row.SubItems.Add(issue.InvoiceNumber);
                row.SubItems.Add(issue.OrderId);
                row.SubItems.Add(issue.Message);
                row.SubItems.Add(state);
                row.Tag = issue;
                var stateCell = row.SubItems[5];
                stateCell.ForeColor = state switch
                {
                    "未讀" => Color.Firebrick,
                    "人工確認" or "等待確認" or "可結案" => Color.FromArgb(190, 120, 0),
                    "已解決" or "已人工處理" => Color.FromArgb(0, 132, 72),
                    _ => Color.DimGray,
                };
                if (state is "人工確認" or "等待確認" or "可結案")
                    stateCell.Font = new Font(list.Font, FontStyle.Bold);
                rows.Add((time, row));
            }

            if (processing)
            {
                foreach (var item in queue.ProcessingItems)
                {
                    var time = FullIssueTime(item.Record);
                    var row = new ListViewItem(ShortIssueTime(time));
                    row.SubItems.Add(item.Kind == InvoiceProcessingKind.Void ? "作廢處理中" : "發票上傳處理中");
                    row.SubItems.Add(item.Record.InvoiceNumber);
                    row.SubItems.Add(item.Record.OrderId);
                    row.SubItems.Add(item.Message);
                    row.SubItems.Add(item.Kind == InvoiceProcessingKind.Void && administrativeClosure.CanClose(item.Record) ? "可結案" : "處理中");
                    row.Tag = item;
                    row.SubItems[5].ForeColor = Color.FromArgb(190, 120, 0);
                    rows.Add((time, row));
                }
            }

            foreach (var record in failed)
            {
                var time = FullIssueTime(record);
                var content = FriendlyFailureReason(record.ErrorMessage);
                if (record.BuyerName.Trim().Length != 0) content = record.BuyerName.Trim() + "｜" + content;
                var row = new ListViewItem(ShortIssueTime(time));
                row.SubItems.Add("開立失敗");
                row.SubItems.Add(record.InvoiceNumber);
                row.SubItems.Add(record.OrderId);
                row.SubItems.Add(content);
                row.SubItems.Add("開立失敗");
                row.Tag = record;
                row.SubItems[5].ForeColor = Color.Firebrick;
                rows.Add((time, row));
            }

            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                var index = 0;
                foreach (var row in rows.OrderByDescending(item => item.SortKey, StringComparer.Ordinal))
                {
                    ApplyZebra(row.Row, index++);
                    list.Items.Add(row.Row);
                }
            }
            finally
            {
                list.EndUpdate();
            }

            var unresolved = issues.Count(issue => issue.ResolvedUtc is null);
            summary.Text = processing ? $"處理中：{queue.ProcessingCount} 項" : $"尚未解決：{unresolved} 項｜開立失敗：{failed.Count} 筆";
            UpdateFailedButtons();
            FitToRows();
            LayoutColumns();
        }
        catch (Exception error)
        {
            if (startupSmokeTest) throw;
            MessageBox.Show(this, "讀取" + Text + "失敗：" + error.Message, "讀取失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void FitToRows()
    {
        var hasRows = list.Items.Count > 0;
        empty.Visible = !hasRows;
        listHost.Visible = hasRows;
        var chrome = root.Padding.Vertical + (int)Math.Ceiling(root.RowStyles[0].Height + root.RowStyles[2].Height);
        var workArea = Screen.FromControl(this).WorkingArea;
        var maximumBodyHeight = Math.Max(listHost.HeightForRows(1), workArea.Height - (Height - ClientSize.Height) - chrome - 40);
        var visibleRows = Math.Min(MaximumVisibleRows, list.Items.Count);
        while (visibleRows > 1 && listHost.HeightForRows(visibleRows) > maximumBodyHeight) visibleRows--;
        var bodyHeight = hasRows ? listHost.HeightForRows(visibleRows) : Math.Max(40, empty.Font.Height + 16);
        ClientSize = new Size(ClientSize.Width, chrome + bodyHeight);
        root.PerformLayout();
        if (Visible && Bottom > workArea.Bottom) Top = Math.Max(workArea.Top, workArea.Bottom - Height);
    }

    internal void VerifySmokeRowSizing()
    {
        var original = list.Items.Cast<ListViewItem>().ToArray();
        try
        {
            foreach (var count in new[] { 0, 1, 4, 20, 1, 0 })
            {
                list.Items.Clear();
                for (var index = 0; index < count; index++)
                    list.Items.Add(new ListViewItem(["2026/10/09", "合成測試", "AA00000000", "synthetic", "測試資料", "處理中"]));
                FitToRows();
                Application.DoEvents();
                if (count == 0)
                {
                    if (!empty.Visible || listHost.Visible || empty.Text != "無資料")
                        throw new InvalidOperationException("空待辦清單必須只顯示無資料");
                }
                else if (listHost.VisibleRowCapacity() != Math.Min(count, MaximumVisibleRows))
                    throw new InvalidOperationException($"Issue viewport: items={count}, capacity={listHost.VisibleRowCapacity()}, height={listHost.Height}; expected actual rows capped at {MaximumVisibleRows}.");
                if (body.Bottom > root.ClientSize.Height || body.Height <= 0)
                    throw new InvalidOperationException("待辦動態高度超出視窗");
            }
        }
        finally
        {
            list.Items.Clear();
            list.Items.AddRange(original);
            FitToRows();
        }
    }

    private void MarkVisibleIssuesRead()
    {
        try
        {
            var openedAt = DateTimeOffset.Now;
            stateStore.SetLastSuccess(accountKey, ReadStateScope, openedAt);
            lastRead = openedAt;
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "更新上傳問題已讀狀態失敗：" + error.Message, "狀態更新失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ShowSelectedIssueDetailsAsync()
    {
        if (manualReviewBusy) return;
        if (list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is InvoiceProcessingItem item)
        {
            ShowProcessingDetails(item);
            return;
        }
        if (SelectedIssue() is not { } issue) return;
        try
        {
            var manual = IsManualReview(issue);
            var rows = IsAllowanceVoidManualReview(issue)
                ? AllowanceVoidDetailRows(issue)
                : IsAllowanceManualReview(issue)
                    ? AllowanceDetailRows(issue)
                    : IsVoidManualReview(issue)
                        ? VoidDetailRows(issue)
                        : CommonDetailRows(issue).Append(new("處理狀態", issue.ResolvedUtc is null ? IssueState(issue) : "已解決")).ToArray();

            var primaryText = string.Empty;
            var allowCancel = false;
            var allowAdministrativeClose = false;
            var primaryDanger = false;

            if (issue.ResolvedUtc is null)
            {
                if (administrativeClosure.IsActiveWorkIssue(issue))
                {
                    try { allowAdministrativeClose = administrativeClosure.CanClose(issue); }
                    catch { allowAdministrativeClose = false; }
                }

                if (!allowAdministrativeClose && IsVoidManualReview(issue))
                {
                    primaryText = "確認送出作廢";
                    primaryDanger = true;
                    allowCancel = true;
                }
                else if (!allowAdministrativeClose && IsAllowanceVoidManualReview(issue))
                {
                    primaryText = "已人工處理";
                    allowCancel = true;
                }
                else if (!allowAdministrativeClose && IsAllowanceManualReview(issue))
                {
                    var record = FindIssueRecord(issue);
                    var review = allowanceWorkflow.ManualReviewFor(record);
                    if (review is not null)
                    {
                        if (!review.AwaitingConfirmation)
                        {
                            primaryText = "已人工處理";
                            allowCancel = true;
                        }
                        else
                        {
                            var candidates = NewAllowanceCandidates(record, review);
                            if (candidates.Count > 1 && candidates.All(item => !IsPendingAllowanceStatus(item.InvoiceStatus)))
                                primaryText = "確認折讓單號";
                        }
                    }
                }
                else if (!allowAdministrativeClose && !manual && !administrativeClosure.IsActiveWorkIssue(issue))
                {
                    primaryText = "標記已解決";
                }
            }

            using var detail = new ManualReviewDetailForm(
                DisplayType(issue.IssueType),
                rows,
                primaryText,
                allowCancel,
                allowAdministrativeClose,
                primaryDanger);
            if (detail.ShowDialog(this) != DialogResult.OK) return;

            switch (detail.SelectedAction)
            {
                case ManualReviewDetailAction.AdministrativeClose:
                    CloseAdministrativeWork(issue);
                    break;
                case ManualReviewDetailAction.CancelReturn:
                    CancelManualReview(issue);
                    break;
                case ManualReviewDetailAction.Primary:
                    if (manual) await ProcessManualReviewAsync(issue);
                    else ResolveIssue(issue);
                    break;
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "讀取待辦明細失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowProcessingDetails(InvoiceProcessingItem item)
    {
        try
        {
            var canClose = item.Kind == InvoiceProcessingKind.Void && administrativeClosure.CanClose(item.Record);
            using var detail = new ManualReviewDetailForm("處理中", [
                new("發票號碼", item.Record.InvoiceNumber), new("訂單編號", item.Record.OrderId),
                new("開立時間", FullIssueTime(item.Record)), new("目前訊息", item.Message),
                new("官方上傳狀態", item.Record.UploadStatusText), new("最後回查", item.Record.LastChecked),
            ], string.Empty, false, canClose);
            if (detail.ShowDialog(this) == DialogResult.OK && detail.SelectedAction == ManualReviewDetailAction.AdministrativeClose)
                CloseAdministrativeWork("作廢", (employee, password) => administrativeClosure.Close(item.Record, employee, password));
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "讀取處理中明細失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResolveIssue(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null || IsManualReview(issue) || administrativeClosure.IsActiveWorkIssue(issue)) return;
        try
        {
            issueStore.Resolve(issue.Id, DateTimeOffset.Now);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "標記上傳問題失敗：" + error.Message, "操作失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CloseAdministrativeWork(InvoiceSyncIssue issue)
    {
        if (manualReviewBusy || !administrativeClosure.CanClose(issue)) return;
        var operation = IsAllowanceManualReview(issue) ? "折讓" : "作廢";
        CloseAdministrativeWork(operation, (employee, password) => administrativeClosure.Close(issue, employee, password));
    }

    private void CloseAdministrativeWork(string operation, Action<string, string> closeWork)
    {
        if (MessageBox.Show(
                this,
                $"這筆{operation}待處理作業已超過系統保留的兩期。\n\n管理員結案後，CYInvoice 會停止追蹤這筆作業、清除本機等待標記，之後可依舊資料保留規則清除。\n\n此操作不會呼叫光貿 API，也不代表光貿已完成{operation}。確定結案？",
                "管理員結案",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        using var login = new EmployeeAdminLoginForm(repository, "管理員結案－驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            closeWork(login.EnteredEmployeeNo, login.AuthenticatedPassword);
            MessageBox.Show(this, "已由管理員手動結案。CYInvoice 不會再自動追蹤這筆待處理作業。", "結案完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "管理員結案失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetManualReviewBusy(false); }
    }

    private async Task ProcessManualReviewAsync(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null || manualReviewBusy) return;
        if (IsVoidManualReview(issue))
        {
            await ApproveVoidManualReviewAsync(issue);
            return;
        }
        if (IsAllowanceVoidManualReview(issue))
        {
            CompleteAllowanceVoid(issue);
            return;
        }

        InvoiceRecord record;
        InvoiceAllowanceManualReview review;
        try
        {
            record = FindIssueRecord(issue);
            review = allowanceWorkflow.ManualReviewFor(record)
                ?? throw new InvalidOperationException("這筆折讓人工處理已沒有申請資料");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "折讓人工處理無法繼續", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!review.AwaitingConfirmation) await CompleteAllowanceAsync(issue);
        else await ConfirmAllowanceCandidateAsync(issue, record, review);
    }

    private void CompleteAllowanceVoid(InvoiceSyncIssue issue)
    {
        if (MessageBox.Show(
                this,
                "請先確認已在光貿網站完成這張折讓單的作廢。\n\n按下「是」後，CYInvoice 只會結束這筆人工待辦，不會呼叫折讓作廢 API。",
                "折讓作廢已人工處理",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var login = new EmployeeAdminLoginForm(repository, "折讓作廢人工處理－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            allowanceVoidWorkflow.MarkManualCompleted(issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
            MessageBox.Show(this, "折讓作廢人工待辦已標記完成。", "處理完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "折讓作廢人工處理未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetManualReviewBusy(false); }
    }

    private async Task ApproveVoidManualReviewAsync(InvoiceSyncIssue issue)
    {
        if (MessageBox.Show(this,
                "這筆作廢申請的紙本電子發票證明聯先前尚未收回。\n\n確認後，CYInvoice 會重新查詢光貿最新狀態，再送出作廢。請確認已完成主管核准並可進行作廢。",
                "確認送出作廢", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var login = new EmployeeAdminLoginForm(repository, "人工確認－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            var result = await voidWorkflow.ApproveManualReviewAsync(issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
            switch (result.Outcome)
            {
                case InvoiceVoidOutcome.Confirmed:
                case InvoiceVoidOutcome.AlreadyVoided:
                    MessageBox.Show(this, "光貿已確認發票作廢完成。", "作廢完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case InvoiceVoidOutcome.PendingConfirmation:
                    MessageBox.Show(this, "作廢已送出，但光貿尚未確認最終結果。\n\n發票目前為「已開立 (等待作廢)」；後續每次正常同步都會再查詢官方結果。",
                        "作廢結果待確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                case InvoiceVoidOutcome.Rejected:
                    MessageBox.Show(this, result.Message + "\n\n人工確認仍保留，尚未標記完成。", "作廢未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                case InvoiceVoidOutcome.RetryReady:
                    MessageBox.Show(this, "先前等待作廢狀態已解除，本次沒有自動重送。\n人工確認仍保留，可重新確認後再次送出。", "請重新確認",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
            }
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "人工確認未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetManualReviewBusy(false); }
    }

    private async Task CompleteAllowanceAsync(InvoiceSyncIssue issue)
    {
        if (MessageBox.Show(this,
                "請先確認已在光貿網站完成這筆人工折讓。\n\n按下「是」後，CYInvoice 會進入等待確認並用 invoice_query 自動比對新的折讓資料。",
                "折讓已人工處理", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var login = new EmployeeAdminLoginForm(repository, "折讓人工處理－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            var result = await allowanceWorkflow.MarkManualCompletedAsync(issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
            ShowAllowanceResult(result);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "折讓人工處理未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetManualReviewBusy(false); }
    }

    private async Task ConfirmAllowanceCandidateAsync(InvoiceSyncIssue issue, InvoiceRecord record, InvoiceAllowanceManualReview review)
    {
        var candidates = NewAllowanceCandidates(record, review);
        if (candidates.Count <= 1)
        {
            MessageBox.Show(this, "目前沒有多筆可供人工選擇的折讓候選。系統會在每次正常同步時繼續查詢。", "等待折讓確認",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var login = new EmployeeAdminLoginForm(repository, "折讓單號確認－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        using var selector = new AllowanceCandidateForm(candidates);
        if (selector.ShowDialog(this) != DialogResult.OK || selector.SelectedAllowanceNumber.Length == 0) return;
        SetManualReviewBusy(true);
        try
        {
            var result = await allowanceWorkflow.ConfirmCandidateAsync(issue, selector.SelectedAllowanceNumber,
                login.EnteredEmployeeNo, login.AuthenticatedPassword);
            ShowAllowanceResult(result);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "折讓單號確認未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetManualReviewBusy(false); }
    }

    private void ShowAllowanceResult(InvoiceAllowanceReconcileResult result)
    {
        switch (result.Outcome)
        {
            case InvoiceAllowanceReconcileOutcome.Confirmed:
                MessageBox.Show(this, result.Message, "折讓確認完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
            case InvoiceAllowanceReconcileOutcome.PendingConfirmation:
                MessageBox.Show(this, result.Message + "\n\n系統會在之後每一次正常同步繼續查詢，不需要重複按「已人工處理」。",
                    "折讓等待確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
            case InvoiceAllowanceReconcileOutcome.Problem:
                MessageBox.Show(this, result.Message + "\n\n這筆申請仍保留在「上傳問題」，不會自動誤結案。",
                    "折讓資料需要確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
        }
    }

    private void CancelManualReview(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null || manualReviewBusy || !IsManualReview(issue)) return;
        var allowance = IsAllowanceManualReview(issue);
        var allowanceVoid = IsAllowanceVoidManualReview(issue);
        var prompt = allowanceVoid
            ? "確定取消並退回這筆折讓作廢申請？\n\n不會對光貿執行折讓作廢。"
            : allowance
                ? "確定取消並退回這筆折讓申請？\n\n不會對光貿執行任何折讓操作。"
                : "確定取消並退回這筆作廢申請？\n\n不會向光貿送出作廢，發票會維持「已開立」。";
        if (MessageBox.Show(this, prompt, "取消退回", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var login = new EmployeeAdminLoginForm(repository, "人工確認－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        SetManualReviewBusy(true);
        try
        {
            if (allowanceVoid)
            {
                allowanceVoidWorkflow.CancelManualReview(issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
                MessageBox.Show(this, "已取消退回；未對光貿執行折讓作廢。", "已取消", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (allowance)
            {
                allowanceWorkflow.CancelManualReview(issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
                MessageBox.Show(this, "已取消退回；未對光貿執行折讓。", "已取消", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                voidWorkflow.CancelManualReview(issue, login.EnteredEmployeeNo, login.AuthenticatedPassword);
                MessageBox.Show(this, "已取消退回；未向光貿送出作廢。", "已取消", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "取消退回失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetManualReviewBusy(false); }
    }

    private InvoiceSyncIssue? SelectedIssue() =>
        list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as InvoiceSyncIssue : null;

    private void SetManualReviewBusy(bool busy)
    {
        manualReviewBusy = busy;
        list.Enabled = !busy;
    }

    private IReadOnlyList<KeyValuePair<string, string>> VoidDetailRows(InvoiceSyncIssue issue)
    {
        var rows = CommonDetailRows(issue).ToList();
        var record = TryFindIssueRecord(issue);
        var review = record is null ? null : voidWorkflow.ManualReviewFor(record);
        var handled = record is null ? null : voidWorkflow.HandledReviewFor(record);
        var parsed = VoidOperationSessionCache.Parse(VoidOperationSessionCache.ReasonFor(issue.InvoiceNumber));
        var requester = review?.RequesterEmployeeNo ?? handled?.RequesterEmployeeNo ?? parsed?.UserEmployeeNo ?? "";
        var requested = review?.RequestedUtc ?? handled?.RequestedUtc;
        rows.Add(new("申請員工", EmployeeDisplay(requester)));
        rows.Add(new("申請時間", requested is null ? "－" : requested.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")));
        rows.Add(new("作廢原因", review?.Reason ?? handled?.Reason ?? parsed?.Reason ?? "－"));
        rows.Add(new("處理管理員", EmployeeDisplay(handled?.ReviewerEmployeeNo ?? parsed?.ReviewerEmployeeNo ?? "")));
        rows.Add(new("處理狀態", issue.ResolvedUtc is not null ? "已解決" : "等待管理員人工確認"));
        return rows;
    }

    private IReadOnlyList<KeyValuePair<string, string>> AllowanceDetailRows(InvoiceSyncIssue issue)
    {
        var rows = CommonDetailRows(issue).ToList();
        var record = TryFindIssueRecord(issue);
        var review = record is null ? null :
            allowanceWorkflow.ManualReviewFor(record) ??
            (issue.ResolvedUtc is null ? null : allowanceWorkflow.HandledReviewFor(record));
        if (review is not null && Math.Abs((review.RequestedUtc - issue.CreatedUtc).TotalMinutes) > 5)
            review = null;
        rows.Add(new("申請員工", EmployeeDisplay(review?.RequesterEmployeeNo ?? "")));
        rows.Add(new("申請時間", review is null ? "－" : review.RequestedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")));
        rows.Add(new("折讓原因", review?.Reason ?? "－"));
        rows.Add(new("處理管理員", EmployeeDisplay(review?.HandlerEmployeeNo ?? "")));
        rows.Add(new("人工處理時間", review?.ManualCompletedUtc is { } completed
            ? completed.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss") : "－"));
        if (review is not null)
        {
            var taxInclusive = review.TaxInclusiveAmount;
            var tax = taxInclusive - (long)Math.Round(taxInclusive / 1.05D, MidpointRounding.AwayFromZero);
            var untaxed = taxInclusive - tax;
            rows.Add(new("未稅折讓金額", MoneyFormatter.Integer(untaxed)));
            rows.Add(new("稅額", MoneyFormatter.Integer(tax)));
            rows.Add(new("含稅折讓總額", MoneyFormatter.Integer(taxInclusive)));
        }
        else
        {
            rows.Add(new("未稅折讓金額", "－"));
            rows.Add(new("含稅折讓總額", "－"));
        }
        rows.Add(new("處理狀態", issue.ResolvedUtc is not null
            ? "已人工處理"
            : review?.AwaitingConfirmation == true ? "等待光貿確認" : "等待管理員人工處理"));
        if (record is not null)
        {
            rows.Add(new("官方折讓資料", FormatAllowances(InvoiceAllowanceMetadata.ReadOfficial(record))));
            if (review is not null && review.AwaitingConfirmation)
                rows.Add(new("本次新增候選", FormatAllowances(NewAllowanceCandidates(record, review))));
        }
        return rows;
    }

    private IReadOnlyList<KeyValuePair<string, string>> AllowanceVoidDetailRows(InvoiceSyncIssue issue)
    {
        var rows = CommonDetailRows(issue).ToList();
        var record = TryFindIssueRecord(issue);
        var review = record is null ? null : allowanceVoidWorkflow.ManualReviewFor(record);
        rows.Add(new("折讓單號", review?.AllowanceNumber ?? "－"));
        rows.Add(new("申請員工", review?.RequesterEmployeeNo ?? "－"));
        rows.Add(new("申請時間", review is null ? "－" : review.RequestedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss")));
        rows.Add(new("折讓作廢原因", review?.Reason ?? "－"));
        rows.Add(new("處理狀態", issue.ResolvedUtc is not null ? "已解決" : "等待管理員人工處理"));
        return rows;
    }

    private IEnumerable<KeyValuePair<string, string>> CommonDetailRows(InvoiceSyncIssue issue)
    {
        yield return new("類型", DisplayType(issue.IssueType));
        yield return new("發票號碼", issue.InvoiceNumber);
        yield return new("訂單編號", issue.OrderId);
        yield return new("待辦建立時間", issue.CreatedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss"));
        yield return new("目前訊息", issue.Message);
    }

    private string EmployeeDisplay(string employeeNo)
    {
        employeeNo = employeeNo.Trim();
        if (employeeNo.Length == 0) return "－";
        try
        {
            var account = repository.LoadAuthorityEmployees()
                .FirstOrDefault(employee => string.Equals(employee.EmployeeNo, employeeNo, StringComparison.Ordinal));
            return account is null || string.IsNullOrWhiteSpace(account.Name)
                ? employeeNo : employeeNo + " " + account.Name.Trim();
        }
        catch
        {
            return employeeNo;
        }
    }

    private InvoiceRecord FindIssueRecord(InvoiceSyncIssue issue) =>
        TryFindIssueRecord(issue) ?? throw new InvalidOperationException("本機找不到這筆人工確認對應的發票");

    private InvoiceRecord? TryFindIssueRecord(InvoiceSyncIssue issue)
    {
        var settings = repository.Settings.LoadOrCreate();
        var sellerInvoice = settings.Environment == Environments.Test ? AmegoDefaults.TestInvoice : settings.ProductionInvoice.Trim();
        var matches = repository.Invoices.LoadOrCreate()
            .Where(record => string.Equals(record.Environment, settings.Environment, StringComparison.Ordinal))
            .Where(record => record.SellerInvoice.Trim().Length == 0 || string.Equals(record.SellerInvoice.Trim(), sellerInvoice, StringComparison.Ordinal))
            .Where(record =>
                (issue.InvoiceNumber.Trim().Length != 0 && string.Equals(record.InvoiceNumber.Trim(), issue.InvoiceNumber.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                (issue.OrderId.Trim().Length != 0 && string.Equals(EffectiveOrderId(record), issue.OrderId.Trim(), StringComparison.Ordinal)))
            .ToArray();
        if (matches.Length > 1) throw new InvalidDataException("人工確認對到多筆本機發票，已停止處理");
        return matches.SingleOrDefault();
    }

    private static IReadOnlyList<InvoiceAllowanceResult> NewAllowanceCandidates(InvoiceRecord record, InvoiceAllowanceManualReview review)
    {
        var baseline = new HashSet<string>(review.BaselineAllowanceNumbers ?? [], StringComparer.OrdinalIgnoreCase);
        return InvoiceAllowanceMetadata.ReadOfficial(record)
            .Where(item => item.AllowanceNumber.Trim().Length != 0)
            .Where(item => !baseline.Contains(item.AllowanceNumber.Trim()))
            .ToArray();
    }

    private static string FormatAllowances(IReadOnlyList<InvoiceAllowanceResult> values)
    {
        if (values.Count == 0) return "無";
        return string.Join("\r\n", values.Select(item =>
        {
            var amount = "?";
            try { amount = FixedDecimal.Add(FixedDecimal.Parse(item.TotalAmount), FixedDecimal.Parse(item.TaxAmount)).ToString(); }
            catch (Exception error) when (error is FormatException or OverflowException) { }
            return $"{item.AllowanceNumber}｜{item.AllowanceDate}｜{item.InvoiceType}｜狀態 {item.InvoiceStatus}｜含稅 {amount}";
        }));
    }

    private static string EffectiveOrderId(InvoiceRecord record) =>
        record.ApiOrderId.Trim().Length != 0 ? record.ApiOrderId.Trim() :
        record.OrderId.Trim().Length != 0 ? record.OrderId.Trim() : record.OriginalOrderId.Trim();

    private void ArchiveCheckedFailed()
    {
        var records = list.CheckedItems.Cast<ListViewItem>().Select(item => item.Tag).OfType<InvoiceRecord>().ToArray();
        if (records.Length == 0 || !ConfirmFailedArchive(records.Length, clearAll: false)) return;
        ArchiveFailedRecords(records);
    }

    private void ArchiveAllFailed()
    {
        var records = CurrentFailedRecords().ToArray();
        if (records.Length == 0 || !ConfirmFailedArchive(records.Length, clearAll: true)) return;
        ArchiveFailedRecords(records);
    }

    private bool ConfirmFailedArchive(int count, bool clearAll) => MessageBox.Show(
        this,
        clearAll
            ? $"確定封存全部 {count} 筆開立失敗紀錄？\n\n保留在原 CYInvoice.db，從一般清單移出，不影響光貿。"
            : $"確定封存勾選的 {count} 筆開立失敗紀錄？\n\n保留完整明細，可在封存紀錄查閱。",
        clearAll ? "封存全部失敗紀錄" : "封存勾選",
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private void ArchiveFailedRecords(IReadOnlyCollection<InvoiceRecord> records)
    {
        try
        {
            failedStore.Archive(records);
            ReloadAll();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "封存開立失敗紀錄失敗：" + error.Message, "封存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowArchivedFailed()
    {
        try
        {
            var settings=repository.Settings.LoadOrCreate();
            var seller=settings.Environment==Environments.Test?
                AmegoDefaults.TestInvoice:settings.ProductionInvoice.Trim();
            if(string.IsNullOrWhiteSpace(seller)){
                MessageBox.Show(this,"請先設定公司統編。","封存紀錄",
                    MessageBoxButtons.OK,MessageBoxIcon.Information);
                return;
            }
            var all=failedStore.AllArchived(settings.Environment,seller);
            var rows=all.Take(15).Select(x=>
                x.ArchivedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm")+
                "｜"+x.Record.Id+"｜"+FriendlyFailureReason(x.Record.ErrorMessage));
            var body=all.Count==0?"目前沒有封存紀錄。":
                $"共 {all.Count} 筆（顯示最新 15 筆）：\n"+
                string.Join("\n",rows)+
                "\n\n完整發票內容與商品明細仍保留於本機 CYInvoice.db。";
            MessageBox.Show(this,body,"開立失敗封存紀錄（唯讀）",
                MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        catch(Exception error)
        {
            MessageBox.Show(this,"無法讀取封存紀錄："+error.Message,
                "封存查詢失敗",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }

    private IReadOnlyList<InvoiceRecord> CurrentFailedRecords()
    {
        var settings = repository.Settings.LoadOrCreate();
        var sellerInvoice = settings.Environment == Environments.Test ? AmegoDefaults.TestInvoice : settings.ProductionInvoice.Trim();
        return repository.Invoices.LoadOrCreate()
            .Where(record => string.Equals(record.Environment, settings.Environment, StringComparison.Ordinal))
            .Where(record => record.SellerInvoice.Trim().Length == 0 || string.Equals(record.SellerInvoice.Trim(), sellerInvoice, StringComparison.Ordinal))
            .Where(record => string.Equals(record.InvoiceState, InvoiceStates.Failed, StringComparison.Ordinal))
            .OrderByDescending(record => FullIssueTime(record), StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsPendingAllowanceStatus(int status) => status is
        UploadStatuses.Pending or UploadStatuses.Uploading or UploadStatuses.Uploaded or UploadStatuses.Processing or UploadStatuses.Confirming;

    private void UpdateFailedButtons()
    {
        if (IsDisposed) return;
        var count = list.CheckedItems.Cast<ListViewItem>().Count(item => item.Tag is InvoiceRecord);
        archiveFailed.Text = count == 0 ? "封存勾選" : $"封存勾選 ({count})";
        archiveFailed.Enabled = count > 0;
        archiveAllFailed.Enabled = list.Items.Cast<ListViewItem>().Any(item => item.Tag is InvoiceRecord);
    }

    private string IssueState(InvoiceSyncIssue issue)
    {
        if (issue.ResolvedUtc is not null)
            return IsAllowanceManualReview(issue) || IsAllowanceVoidManualReview(issue) ? "已人工處理" : "已解決";
        if (administrativeClosure.IsActiveWorkIssue(issue))
        {
            try { if (administrativeClosure.CanClose(issue)) return "可結案"; }
            catch { }
        }
        if (IsAllowanceManualReview(issue))
        {
            try
            {
                var record = FindIssueRecord(issue);
                return allowanceWorkflow.ManualReviewFor(record)?.AwaitingConfirmation == true ? "等待確認" : "人工確認";
            }
            catch { return "人工確認"; }
        }
        if (IsVoidManualReview(issue) || IsAllowanceVoidManualReview(issue)) return "人工確認";
        return lastRead is not null && issue.CreatedUtc <= lastRead.Value ? "已讀" : "未讀";
    }

    private static bool IsManualReview(InvoiceSyncIssue issue) =>
        IsVoidManualReview(issue) || IsAllowanceManualReview(issue) || IsAllowanceVoidManualReview(issue);

    private static bool IsVoidManualReview(InvoiceSyncIssue issue) =>
        string.Equals(issue.IssueType, InvoiceVoidIssueTypes.ManualReview, StringComparison.Ordinal);

    private static bool IsAllowanceManualReview(InvoiceSyncIssue issue) =>
        string.Equals(issue.IssueType, InvoiceAllowanceIssueTypes.ManualReview, StringComparison.Ordinal);

    private static bool IsAllowanceVoidManualReview(InvoiceSyncIssue issue) =>
        string.Equals(issue.IssueType, InvoiceAllowanceVoidIssueTypes.ManualReview, StringComparison.Ordinal);

    private string CurrentAccountKey()
    {
        var settings = repository.Settings.LoadOrCreate();
        var sellerInvoice = settings.Environment == Environments.Test ? AmegoDefaults.TestInvoice : settings.ProductionInvoice.Trim();
        if (sellerInvoice.Length == 0) throw new InvalidOperationException("目前環境缺少可識別的公司統編");
        return settings.Environment + "|" + sellerInvoice;
    }

    private void LayoutColumns()
    {
        if (list.Columns.Count != DefaultWidths.Length || list.ClientSize.Width <= 0) return;
        var widths = DefaultWidths.ToArray();
        var available = Math.Max(1, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        widths[4] = Math.Max(60, available - widths.Where((_, index) => index != 4).Sum());
        settingColumnWidths = true;
        try
        {
            for (var index = 0; index < widths.Length; index++)
                if (list.Columns[index].Width != widths[index]) list.Columns[index].Width = widths[index];
        }
        finally { settingColumnWidths = false; }
    }

    private void ShowClippedContentToolTip(object? sender, MouseEventArgs eventArgs)
    {
        var hit = list.HitTest(eventArgs.Location);
        var content = hit.Item is { } item && item.SubItems.Count > 4 &&
            ReferenceEquals(hit.SubItem, item.SubItems[4])
            ? item.SubItems[4].Text : string.Empty;
        var available = list.Columns[4].Width - 12;
        if (content.Length == 0 || TextRenderer.MeasureText(content, list.Font).Width <= available)
            content = string.Empty;
        if (content == visibleToolTip) return;
        visibleToolTip = content;
        contentToolTip.SetToolTip(list, content);
    }

    private void ClearContentToolTip()
    {
        visibleToolTip = string.Empty;
        contentToolTip.SetToolTip(list, string.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            contentToolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private static void ApplyZebra(ListViewItem row, int index)
    {
        row.UseItemStyleForSubItems = false;
        var background = index % 2 == 0 ? Color.White : Color.FromArgb(238, 244, 250);
        foreach (ListViewItem.ListViewSubItem subItem in row.SubItems)
        {
            subItem.BackColor = background;
            if (subItem.ForeColor == Color.Empty) subItem.ForeColor = SystemColors.ControlText;
        }
    }

    private static string FullIssueTime(InvoiceRecord record) =>
        record.InvoiceDate.Trim().Length != 0 ? (record.InvoiceDate.Trim() + " " + record.InvoiceTime.Trim()).Trim() : record.SentAt.Trim();

    private static string ShortIssueTime(string value) => value.Length >= 16 ? value[..16] : value;

    private static string FriendlyFailureReason(string raw)
    {
        var message = raw.Trim();
        if (message.Length == 0) return "光貿未提供失敗原因";
        if (message.StartsWith("API code ", StringComparison.OrdinalIgnoreCase))
        {
            var colon = message.IndexOf(':');
            if (colon >= 0 && colon + 1 < message.Length) message = message[(colon + 1)..].Trim();
        }
        var field = FailureField(message);
        if (field.Length != 0)
        {
            if (ContainsAny(message, "required", "empty", "blank", "不可空", "不得空", "不能空")) return field + "不可空白";
            if (ContainsAny(message, "length", "長度")) return field + "長度不正確";
            if (ContainsAny(message, "duplicate", "already exists", "重複", "已存在")) return field + "重複";
            return field + "資料格式不正確";
        }
        if (message.Any(IsCjk)) return message;
        return "光貿拒絕此筆開立資料，請檢查發票內容";
    }

    private static string FailureField(string message)
    {
        var fields = new (string Technical, string Display)[]
        {
            ("BuyerIdentifier", "買受人統編"), ("BuyerName", "買受人名稱"), ("BuyerAddress", "買受人地址"),
            ("BuyerTelephoneNumber", "買受人電話"), ("BuyerEmailAddress", "買受人電子郵件"),
            ("OrderId", "訂單編號"), ("OrderID", "訂單編號"), ("SalesAmount", "銷售額"),
            ("TaxAmount", "稅額"), ("TotalAmount", "發票總額"), ("ProductItem", "商品明細"),
            ("Description", "商品名稱"), ("Quantity", "商品數量"), ("UnitPrice", "商品單價"),
            ("CarrierType", "載具類型"), ("CarrierId1", "載具號碼"), ("CarrierId2", "載具號碼"),
            ("NPOBAN", "捐贈碼"), ("MainRemark", "備註"), ("DetailVat", "明細含稅設定"),
        };
        foreach (var field in fields)
            if (message.Contains(field.Technical, StringComparison.OrdinalIgnoreCase)) return field.Display;
        return string.Empty;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsCjk(char value) => value is >= '\u3400' and <= '\u9fff';

    internal void VerifySmokeFailedRecord(string recordId)
    {
        if (!list.Items.Cast<ListViewItem>().Any(row => row.Tag is InvoiceRecord record && record.Id == recordId))
            throw new InvalidOperationException("開立失敗紀錄未保留在上傳問題清單");
    }

    internal void VerifySmokeLayout()
    {
        if (Text != (processing ? "處理中" : "上傳問題") || ShowIcon || Math.Abs(Font.SizeInPoints - 10F) > 0.1F)
            throw new InvalidOperationException("上傳問題視窗標題、圖示或字級不正確");
        if (!listHost.UsesOnlyNativeScrollBar || list.CheckBoxes == processing || list.Columns.Count != 6 || !list.GridLines || !list.Scrollable)
            throw new InvalidOperationException("上傳問題單一清單結構不正確");
        if (list.Columns[5].Text != "狀態" || archiveAllFailed.Text != "封存全部失敗紀錄")
            throw new InvalidOperationException("上傳問題欄位或開立失敗清除按鈕不正確");
        if (Descendants(this).OfType<Button>().Any(button => button.Text is "標記已解決" or "查看明細" or "確認送出作廢" or "取消退回"))
            throw new InvalidOperationException("上傳問題主視窗仍保留待辦操作按鈕，操作應位於雙擊後的詳細視窗");
        if (FriendlyFailureReason("API code 3040122: BuyerIdentifier invalid") != "買受人統編資料格式不正確")
            throw new InvalidOperationException("開立失敗原因未轉為可理解中文");
        if (DisplayType(InvoiceVoidIssueTypes.ManualReview) != "紙本作廢確認" ||
            DisplayType(InvoiceVoidSyncIssueTypes.PendingConfirmation) != "作廢結果待確認" ||
            DisplayType(InvoiceAllowanceIssueTypes.ManualReview) != "折讓人工處理" ||
            DisplayType(InvoiceAllowanceVoidIssueTypes.ManualReview) != "折讓作廢人工處理")
            throw new InvalidOperationException("人工確認問題類型顯示不正確");
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static string DisplayType(string type) => type switch
    {
        InvoiceSyncIssueTypes.InvoiceListFailed => "發票清單查詢失敗",
        InvoiceSyncIssueTypes.QueryFailed => "發票回查失敗",
        InvoiceSyncIssueTypes.RemoteNotFound => "光貿查無發票",
        InvoiceSyncIssueTypes.UnknownRemoteNotFound => "結果不明且光貿查無",
        InvoiceSyncIssueTypes.ReconciliationFailed => "同步比對失敗",
        InvoiceSyncIssueTypes.AmbiguousMatch => "本機資料無法唯一對應",
        InvoiceSyncIssueTypes.LocalWriteFailed => "本機資料庫寫入失敗",
        InvoiceVoidIssueTypes.ManualReview => "紙本作廢確認",
        InvoiceVoidSyncIssueTypes.PendingConfirmation => "作廢結果待確認",
        InvoiceAllowanceIssueTypes.ManualReview => "折讓人工處理",
        InvoiceAllowanceVoidIssueTypes.ManualReview => "折讓作廢人工處理",
        _ => type,
    };

}
