using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

// Human-only exception queue. It never changes a provider's invoice state.
internal sealed class CloudWorkQueueForm : Form
{
    private readonly LocalRepository repository;
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly CloudWorkItemClient client;
    private readonly ListView items = new() {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        GridLines = true, MultiSelect = false, HideSelection = false
    };
    private readonly Label summary = new() {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.DimGray, AutoEllipsis = false
    };
    private readonly Button refresh = UiControls.StandardButton("重新整理");
    private readonly Button next = UiControls.StandardButton("下一頁");
    private readonly Button review = UiControls.StandardButton("開始覆核");
    private readonly Button closeWork = UiControls.StandardButton("人工封存");
    private readonly Button done = UiControls.StandardButton("關閉");
    private CloudWorkItemCursor? nextCursor;
    private bool busy;

    public CloudWorkQueueForm(LocalRepository repository,string endpoint,
        string deviceToken,string workspaceId)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        client = new CloudWorkItemClient(http,new Uri(endpoint,UriKind.Absolute),
            deviceToken,workspaceId);
        Text = "CYID 跨機待辦";
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft JhengHei UI",10F);
        ClientSize = new Size(820,485);
        MinimumSize = new Size(700,410);
        ShowIcon = false;
        ShowInTaskbar = false;
        BuildLayout();
        Shown += async (_,_) => await LoadAsync(true);
    }

    private void BuildLayout()
    {
        items.Columns.Add("類別",118);
        items.Columns.Add("狀態",90);
        items.Columns.Add("事件類型",155);
        items.Columns.Add("建立時間",150);
        items.Columns.Add("工作項目 ID",260);
        items.SelectedIndexChanged += (_,_) => SetActions();
        items.DoubleClick += async (_,_) => await ChangeAsync();
        var layout = new TableLayoutPanel {
            Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,
            Padding=new Padding(12),Margin=Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,49));
        summary.Text="工作項目只供跨機人工處理，不代表光貿已開立、作廢或確認上傳。";
        layout.Controls.Add(summary,0,0);
        layout.Controls.Add(items,0,1);
        var actions = new FlowLayoutPanel {
            Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,
            WrapContents=false,Margin=Padding.Empty,Padding=new Padding(0,7,0,0)
        };
        foreach(var button in new[]{done,closeWork,review,next,refresh})
        {
            button.Width = button==closeWork||button==review ? 110 : 95;
            actions.Controls.Add(button);
        }
        done.DialogResult=DialogResult.Cancel;
        done.Click += (_,_) => Close();
        refresh.Click += async (_,_) => await LoadAsync(true);
        next.Click += async (_,_) => await LoadAsync(false);
        review.Click += async (_,_) => await ChangeAsync();
        closeWork.Click += async (_,_) => await ChangeAsync();
        layout.Controls.Add(actions,0,2);
        Controls.Add(layout);
        CancelButton=done;
        SetActions();
    }

    private CloudWorkItemSummary? Selected() => items.SelectedItems.Count==1
        ? items.SelectedItems[0].Tag as CloudWorkItemSummary : null;

    private void SetActions()
    {
        var selected=Selected();
        refresh.Enabled=!busy;
        next.Enabled=!busy && nextCursor is not null;
        review.Enabled=!busy && selected is { Status:"open" };
        closeWork.Enabled=!busy && selected is { Status:"review" };
        done.Enabled=!busy;
    }

    private (string EmployeeNo,string Password)? Credentials()
    {
        using var form=new EmployeeAdminLoginForm(repository,
            "跨機待辦－CYID 當次驗證",collectCredentialsOnly:true);
        return form.ShowDialog(this)==DialogResult.OK
            ? (form.EnteredEmployeeNo,form.AuthenticatedPassword) : null;
    }

    private async Task LoadAsync(bool first)
    {
        if(busy)return;
        var credentials=Credentials();
        if(credentials is null)return;
        busy=true;SetActions();
        try
        {
            var result=await client.ListAsync(credentials.Value.EmployeeNo,
                credentials.Value.Password,limit:50,cursor:first?null:nextCursor);
            items.BeginUpdate();
            try
            {
                items.Items.Clear();
                foreach(var work in result.Items)
                {
                    var row=new ListViewItem(work.Kind=="VOID"?"紙本作廢覆核":"上傳問題");
                    row.SubItems.Add(work.Status=="open"?"待覆核":
                        work.Status=="review"?"覆核中":"已封存");
                    row.SubItems.Add(work.IssueCode??"—");
                    row.SubItems.Add(work.CreatedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
                    row.SubItems.Add(work.ItemId);
                    row.Tag=work;
                    items.Items.Add(row);
                }
            }
            finally{items.EndUpdate();}
            nextCursor=result.NextCursor;
            summary.Text=result.Items.Count==0?"目前沒有待處理項目。":
                "本頁 "+result.Items.Count+" 筆；人工封存不代表官方處理完成。";
        }
        catch(CloudApiException error)
        {
            summary.Text=error.Code=="WORK_ITEMS_NOT_READY"
                ?"3.1 跨機待辦尚未啟用，本機發票資料不受影響。"
                :"無法確認管理員權限或 Device 狀態，請稍後重新整理。";
        }
        catch {summary.Text="雲端查詢結果不明，請重新整理；系統沒有修改任何發票。";}
        finally{busy=false;SetActions();}
    }

    private async Task ChangeAsync()
    {
        if(busy)return;
        var selected=Selected();
        if(selected is null || selected.Status is not ("open" or "review"))return;
        var credentials=Credentials();
        if(credentials is null)return;
        busy=true;SetActions();
        try
        {
            var current=await client.GetAsync(selected.ItemId,
                credentials.Value.EmployeeNo,credentials.Value.Password);
            if(current is null||current.Status!=selected.Status||
               current.Revision!=selected.Revision)
            {
                MessageBox.Show(this,"此項版次已改變，請重新整理後再操作。",
                    "版次衝突",MessageBoxButtons.OK,MessageBoxIcon.Information);
                return;
            }
            var to=current.Status=="open"?"review":"closed";
            string? reason=null;
            if(to=="closed"&&current.Kind=="UPLOAD_ISSUE")
            {
                reason=ChooseReason();
                if(reason is null)return;
            }
            if(MessageBox.Show(this,
                "確定對 "+current.ItemId+" 進行"+(to=="review"?"開始覆核":"人工封存")+"？\n\n"+
                "只更新例外工作項目，不會向光貿送出開票、作廢或任何重送。",
                "確認人工操作",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            var saved=await client.TransitionManualAsync(current,to,
                credentials.Value.EmployeeNo,credentials.Value.Password,reason);
            var verified=await client.GetAsync(saved.ItemId,
                credentials.Value.EmployeeNo,credentials.Value.Password);
            if(verified is null||verified.Status!=to||
               verified.Revision!=current.Revision+1)
                throw new InvalidDataException("Work Item confirmation is incomplete.");
            foreach(ListViewItem row in items.SelectedItems)row.Selected=false;
            summary.Text="人工覆核版次 "+verified.Revision+" 已確認；官方發票狀態未變更。"+
                "請重新整理清單。";
        }
        catch
        {
            MessageBox.Show(this,
                "處理結果可能已寫入。請先重新整理並查回最新狀態，"+
                "不要盲目重試或重送光貿作業。",
                "人工操作待確認",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        finally{busy=false;SetActions();}
    }

    private string? ChooseReason()
    {
        using var form=new Form {
            Text="上傳問題－人工封存原因",ClientSize=new Size(385,142),
            StartPosition=FormStartPosition.CenterParent,
            FormBorderStyle=FormBorderStyle.FixedDialog,
            MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false,ShowIcon=false,
            Font=new Font("Microsoft JhengHei UI",10F)
        };
        var names=new[]{"本機已處理","已查詢官方網站","重複通報",
            "不需處理","需後續追蹤"};
        var codes=new[]{"resolved_locally","checked_official","duplicate_report",
            "not_actionable","needs_followup"};
        var combo=new ComboBox {
            Left=12,Top=18,Width=356,DropDownStyle=ComboBoxStyle.DropDownList
        };
        combo.Items.AddRange(names.Cast<object>().ToArray());
        combo.SelectedIndex=4;
        var accept=UiControls.StandardButton("確認封存");
        accept.SetBounds(157,82,100,32);
        accept.DialogResult=DialogResult.OK;
        var cancel=UiControls.StandardButton("取消");
        cancel.SetBounds(269,82,100,32);
        cancel.DialogResult=DialogResult.Cancel;
        form.Controls.AddRange([combo,accept,cancel]);
        form.AcceptButton=accept;form.CancelButton=cancel;
        return form.ShowDialog(this)==DialogResult.OK?codes[combo.SelectedIndex]:null;
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)http.Dispose();
        base.Dispose(disposing);
    }
}
