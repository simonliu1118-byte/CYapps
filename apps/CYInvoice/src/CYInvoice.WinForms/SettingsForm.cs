using CYInvoice.Core;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class SettingsForm : Form
{
    private readonly LocalRepository repository;
    private Settings settings;
    private readonly RadioButton test = new();
    private readonly RadioButton production = new();
    private readonly Label environmentTitle = new() { Text = "選擇使用環境", AutoSize = true };
    private readonly Label connectionTitle = new() { Text = "連線與裝置", AutoSize = true };
    private readonly Label identityStatus = new() { AutoSize = true, ForeColor = Color.FromArgb(102,112,133) };
    private readonly Label cardStatus = new() { AutoSize = true, MaximumSize = new Size(530,0), ForeColor = Color.FromArgb(102,112,133) };
    private readonly Button cloudSettings = UiControls.StandardButton("雲端連線設定");
    private readonly Button deviceManagement = UiControls.StandardButton("裝置管理");
    private readonly Button companyCard = UiControls.StandardButton("編輯公司卡");
    private readonly Button localReset = UiControls.StandardButton("重設本機資料");
    private readonly Button save = UiControls.StandardButton("儲存選擇");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private readonly TableLayoutPanel choices = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
    private bool resetFlowBusy;

    public SettingsForm(LocalRepository repository, bool startupSmokeTest = false)
    {
        this.repository = repository;
        settings = repository.Settings.LoadOrCreate();
        Text = "設定選單";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 430);
        MinimumSize = new Size(570, 450);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI",10F);
        BackColor = Color.FromArgb(248,250,252);
        BuildLayout();
        LoadValues();
        Shown += async (_,_) => { if (!startupSmokeTest) await RefreshCardAsync(); };
    }

    private void ConfigureCard(RadioButton choice)
    {
        // Native button appearance provides a whole-card hit area without a
        // radio glyph, extra checked label, custom drawing or parallel state.
        choice.Appearance = Appearance.Button;
        choice.AutoSize = false;
        choice.Dock = DockStyle.Fill;
        choice.MinimumSize = new Size(240,112);
        choice.Padding = new Padding(16);
        choice.Font = new Font(Font.FontFamily,12F,FontStyle.Bold);
        choice.TextAlign = ContentAlignment.MiddleLeft;
        choice.FlatStyle = FlatStyle.Flat;
        choice.FlatAppearance.BorderSize = 1;
        choice.FlatAppearance.CheckedBackColor = Color.FromArgb(219,234,254);
        choice.UseVisualStyleBackColor = false;
        choice.CheckedChanged += (_,_) => UpdateSelection();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
            ColumnCount = 1, RowCount = 6, Padding = new Padding(24) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        environmentTitle.Font = new Font(Font.FontFamily,12F,FontStyle.Bold);
        connectionTitle.Font = new Font(Font.FontFamily,12F,FontStyle.Bold);
        var environmentHeader = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
            WrapContents = false, Margin = new Padding(0,0,0,12) };
        environmentTitle.Margin = new Padding(0,7,16,0);
        companyCard.AutoSize = true;
        companyCard.Margin = Padding.Empty;
        companyCard.Click += async (_,_) => await EditCompanyCardAsync();
        environmentHeader.Controls.AddRange([environmentTitle,companyCard]);
        root.Controls.Add(environmentHeader,0,1);
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        choices.RowStyles.Add(new RowStyle(SizeType.Absolute,112));
        ConfigureCard(test); ConfigureCard(production);
        test.Margin = new Padding(0,0,6,0);
        production.Margin = new Padding(6,0,0,0);
        test.TabIndex = 0; production.TabIndex = 1;
        choices.Controls.Add(test,0,0); choices.Controls.Add(production,1,0);
        root.Controls.Add(choices,0,2);
        cardStatus.Margin = new Padding(0,12,0,0);
        root.Controls.Add(cardStatus,0,3);
        var connection = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
            ColumnCount = 1, Margin = new Padding(0,0,0,24) };
        var connectionHeader = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
            WrapContents = false, Margin = new Padding(0,0,0,8) };
        connectionTitle.Margin = new Padding(0,0,16,0);
        identityStatus.Margin = new Padding(0,2,0,0);
        connectionHeader.Controls.AddRange([connectionTitle,identityStatus]);
        var connectionActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
            WrapContents = false, Margin = Padding.Empty };
        cloudSettings.AutoSize = true; deviceManagement.AutoSize = true;
        cloudSettings.Margin = new Padding(0,0,12,0);
        deviceManagement.Margin = Padding.Empty;
        connectionActions.Controls.AddRange([cloudSettings,deviceManagement]);
        connection.Controls.Add(connectionHeader,0,0); connection.Controls.Add(connectionActions,0,1);
        cloudSettings.Click += (_,_) => OpenCloudSettings();
        deviceManagement.Click += async (_,_) => await OpenDeviceManagementAsync();
        root.Controls.Add(connection,0,0);
        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0,20,0,0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        localReset.AutoSize = true;
        localReset.Click += async (_,_) => await BeginLocalResetAsync();
        footer.Controls.Add(localReset,0,0);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty };
        save.AutoSize = true; cancel.AutoSize = true;
        save.BackColor = Color.FromArgb(37,99,235); save.ForeColor = Color.White;
        save.UseVisualStyleBackColor = false; save.FlatStyle = FlatStyle.Flat;
        save.Click += (_,_) => SaveSelection();
        cancel.DialogResult = DialogResult.Cancel;
        actions.Controls.AddRange([save,cancel]); footer.Controls.Add(actions,1,0);
        root.Controls.Add(footer,0,5); Controls.Add(root);
        CancelButton = cancel;
    }

    private void LoadValues()
    {
        test.Checked = settings.Environment == Environments.Test;
        production.Checked = settings.Environment == Environments.Production;
        UpdateCloudControls(); UpdateSelection();
    }

    private void UpdateSelection()
    {
        var name = string.IsNullOrWhiteSpace(settings.ProductionCompanyName) ? "正式公司" : settings.ProductionCompanyName;
        test.Text = "光貿測試環境\n12345678";
        production.Text = name + "\n" + (settings.ProductionInvoice.Length==0?"尚未設定統編":settings.ProductionInvoice);
        foreach (var choice in new[] {test,production})
        {
            choice.FlatAppearance.BorderColor = choice.Checked ? Color.FromArgb(37,99,235) : Color.FromArgb(209,213,219);
            choice.BackColor = choice.Checked ? Color.FromArgb(219,234,254) : Color.White;
            choice.ForeColor = Color.FromArgb(31,41,55);
        }
        cardStatus.Text = test.Checked ? "目前使用光貿公開測試環境。MO店+ 匯入沿用公司卡的檔案密碼。" :
            $"{name}｜光貿金鑰：{(settings.ProductionAppKeyEncrypted.Length>0?"已設定":"未設定")}｜MO店+ 密碼：{(settings.MoPasswordEncrypted.Length>0?"已設定":"未設定")}";
    }

    private void UpdateCloudControls()
    {
        var cloud = settings.CloudMode != CloudModes.LocalOnly;
        identityStatus.Text = cloud ? "CYID 雲端連線" : "本機使用";
        cloudSettings.Enabled = !resetFlowBusy;
        deviceManagement.Visible = cloud;
        deviceManagement.Enabled = !resetFlowBusy && settings.CloudEmployeeAuthorityReady && HasCloudIdentity(settings);
        localReset.Visible = !cloud;
        localReset.Enabled = !resetFlowBusy;
        companyCard.Enabled = save.Enabled = cancel.Enabled = !resetFlowBusy;
    }

    private async Task RefreshCardAsync()
    {
        if (resetFlowBusy) return;
        resetFlowBusy = true; UpdateCloudControls();
        try
        {
            var card = await repository.RefreshCompanyCardAsync();
            if (IsDisposed) return;
            settings = repository.Settings.LoadOrCreate();
            UpdateCloudControls(); UpdateSelection();
            if (card is not null) cardStatus.Text += $"\n雲端公司卡已同步（版次 {card.Revision}）。";
        }
        catch (Exception) { if (!IsDisposed) cardStatus.Text = "公司卡同步未完成；保留本機設定。請確認雲端連線後重新開啟設定。"; }
        finally { resetFlowBusy=false; if(!IsDisposed)UpdateCloudControls(); }
    }

    private void SaveSelection()
    {
        if (resetFlowBusy) return;
        try
        {
            var latest = repository.Settings.LoadOrCreate();
            latest.Environment = production.Checked ? Environments.Production : Environments.Test;
            repository.Settings.Save(latest);
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception error) { MessageBox.Show(this,error.Message,"無法儲存設定",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
    }

    private async Task EditCompanyCardAsync()
    {
        if (resetFlowBusy) return;
        var cloud = settings.CloudMode != CloudModes.LocalOnly;
        CloudCompanySettingsStatus? card = null;
        if (cloud)
        {
            try { card = await repository.RefreshCompanyCardAsync(); settings = repository.Settings.LoadOrCreate(); }
            catch { MessageBox.Show(this,"請先恢復公司卡同步再編輯，避免覆寫未知的雲端版次。","無法編輯",MessageBoxButtons.OK,MessageBoxIcon.Warning); return; }
        }
        using var editor = new Form { Text = "編輯公司卡", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(520,320), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowIcon = false, ShowInTaskbar = false,
            Font = Font, BackColor = BackColor };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 6 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var name = UiControls.TextBox(100); name.Text = card?.CompanyName ?? settings.ProductionCompanyName;
        var seller = UiControls.TextBox(8); seller.Text = card?.SellerInvoice ?? settings.ProductionInvoice;
        var key = UiControls.TextBox(512); key.UseSystemPasswordChar = true; key.PlaceholderText = "留白保留原金鑰";
        var mo = UiControls.TextBox(256); mo.UseSystemPasswordChar = true; mo.PlaceholderText = "留白保留原密碼";
        var fields = new[] {name,seller,key,mo};
        var labels = new[] {"公司名稱","公司統編","光貿 App Key","MO店+ 密碼"};
        for (var i=0;i<4;i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label {Text=labels[i],AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,8,16,8)},0,i);
            fields[i].Anchor = AnchorStyles.Left|AnchorStyles.Right; fields[i].Margin = new Padding(0,8,0,8);
            table.Controls.Add(fields[i],1,i);
        }
        var note = new Label { AutoSize=true, MaximumSize=new Size(465,0), ForeColor=Color.FromArgb(102,112,133),
            Text=cloud?"儲存後同步至此 Workspace；其他電腦在下次使用時自動更新。":"公司卡以 Windows 安全儲存保護；密碼留白保留原值。" };
        table.Controls.Add(note,0,4); table.SetColumnSpan(note,2);
        var actions = new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Margin=new Padding(0,16,0,0) };
        var abort=UiControls.StandardButton("取消"); abort.DialogResult=DialogResult.Cancel;
        var apply=UiControls.StandardButton(cloud?"儲存並同步":"儲存公司卡"); apply.AutoSize=true;
        actions.Controls.AddRange([abort,apply]); table.Controls.Add(actions,0,5); table.SetColumnSpan(actions,2);
        editor.Controls.Add(table); editor.CancelButton=abort;
        var saving=false;
        editor.FormClosing+=(_,e)=> { if(saving)e.Cancel=true; };
        apply.Click+=async(_,_)=>
        {
            if(saving)return;
            if(name.Text.Trim().Length==0 || seller.Text.Trim().Length!=8 || !seller.Text.Trim().All(char.IsAsciiDigit))
            { MessageBox.Show(editor,"請輸入公司名稱及八位數統編。","資料未完整",MessageBoxButtons.OK,MessageBoxIcon.Warning); return; }
            using var login=new EmployeeAdminLoginForm(repository,"編輯公司卡－超管驗證",collectCredentialsOnly:cloud);
            if(login.ShowDialog(editor)!=DialogResult.OK)return;
            if(!cloud && login.AuthenticatedPrincipal?.Role!=AppRole.SuperAdmin)
            { MessageBox.Show(editor,"只有超級管理員可修改公司卡。","權限不足",MessageBoxButtons.OK,MessageBoxIcon.Warning); return; }
            saving=true; apply.Enabled=abort.Enabled=false;
            try
            {
                if(cloud)
                {
                    using var http=new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                    var client=new CloudCompanySettingsClient(http,new Uri(settings.CloudBaseUrl),repository.Settings.CloudDeviceToken(settings));
                    var edit=new CloudCompanySettingsEdit(card?.Revision??0,seller.Text.Trim(),
                        key.Text.Length==0?CloudCompanySecretChange.Keep():CloudCompanySecretChange.Replace(key.Text.Trim()),
                        mo.Text.Length==0?CloudCompanySecretChange.Keep():CloudCompanySecretChange.Replace(mo.Text),name.Text.Trim());
                    if((card?.HasAmegoAppKey!=true&&key.Text.Length==0)||(card?.HasMoExcelPassword!=true&&mo.Text.Length==0))
                        throw new InvalidOperationException("請將金鑰與 MO店+ 密碼完整設定後儲存。");
                    if(card is null || card.SellerInvoice!=edit.SellerInvoice || card.CompanyName!=edit.CompanyName || key.Text.Length!=0 || mo.Text.Length!=0)
                        card=await client.UpdateAsync(edit,login.EnteredEmployeeNo,login.AuthenticatedPassword);
                    // UPDATE is never retried. A lost reply leaves the editor visible;
                    // reopening re-reads the current revision before another edit.
                    await client.SynchronizeAsync(repository.Settings);
                }
                else
                {
                    var latest=repository.Settings.LoadOrCreate();
                    latest.ProductionInvoice=seller.Text.Trim(); latest.ProductionCompanyName=name.Text.Trim();
                    repository.Settings.ClearCloudCompanyStamp(latest);
                    if(key.Text.Length!=0)repository.Settings.SetProductionAppKey(latest,key.Text);
                    if(mo.Text.Length!=0)repository.Settings.SetMoPassword(latest,mo.Text);
                    repository.Settings.Save(latest);
                }
                settings=repository.Settings.LoadOrCreate(); UpdateSelection();
                saving=false; editor.DialogResult=DialogResult.OK;
            }
            catch(Exception)
            {
                // Do not retry an uncertain mutation or retain an old actionable revision.
                apply.Enabled=false;
                MessageBox.Show(editor,cloud?"儲存或同步尚未確認。請關閉編輯並重新讀取公司卡後核對；程式沒有重送更新。":"無法儲存公司卡；原設定仍保留。",
                    "公司卡未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
            finally { saving=false; abort.Enabled=true; key.Clear(); mo.Clear(); }
        };
        editor.ShowDialog(this);
        key.Clear(); mo.Clear();
    }

    private void OpenCloudSettings()
    {
        settings = repository.Settings.LoadOrCreate();
        // V3 only supports Local and CYID Cloud. Do not expose the retired
        // Built-in onboarding flow from Settings for a new installation.
        if (!HasCloudIdentity(settings))
        {
            using var direct = new CloudDirectJoinForm(repository);
            if (direct.ShowDialog(this) == DialogResult.OK && direct.IdentityCompleted)
            {
                // Direct join saves the CYID binding itself. Close this older
                // settings snapshot so a subsequent Save cannot overwrite it.
                DialogResult = DialogResult.OK;
                Close();
            }
            return;
        }
        if (settings.CloudIdentityProvider != "CYID")
        {
            MessageBox.Show(this,
                "CYInvoice V3 已取消 Built-in Cloud。請使用 CYID 雲端加入流程；既有資料不會在此自動刪除。",
                "不支援的雲端帳號模式", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var originalEndpoint = settings.CloudBaseUrl;
        using var form = new CloudSetupForm(repository, originalEndpoint, settings);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        if (!SameCloudEndpoint(originalEndpoint,form.SelectedBaseUrl))
        {
            MessageBox.Show(this,"這台電腦已綁定 CYID Workspace。更換連線位置須完成原裝置移轉／加入流程；原綁定仍保留。",
                "需重新加入",MessageBoxButtons.OK,MessageBoxIcon.Information);
            return;
        }
        UpdateCloudControls();
    }

    private async Task OpenDeviceManagementAsync()
    {
        try
        {
            if (settings.CloudMode != CloudModes.CloudPreferred
                || !settings.CloudEmployeeAuthorityReady || !HasCloudIdentity(settings)
                || string.IsNullOrWhiteSpace(settings.CloudBaseUrl))
            {
                throw new InvalidOperationException("這台電腦尚未完成中央帳號轉換，暫時不能管理其他裝置。");
            }

            var token = repository.Settings.CloudDeviceToken(settings);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("目前無法讀取這台電腦的 Cloud Device Token。");

            using var form = new CloudDeviceManagementForm(settings.CloudBaseUrl, token);
            form.ShowDialog(this);
            if (IsDisposed || Disposing || LocalResetApplication.IsPending) return;

            var assessment = await LocalResetCoordinator.AssessBuiltInCloudAsync(repository);
            if (assessment.Disposition != CloudExitDisposition.DeviceAlreadyRevoked) return;

            MessageBox.Show(
                this,
                "目前這台電腦的 Device 已被撤銷。為避免留下無法再使用 Cloud 身分的半完成狀態，請完成本機重設；Cloud Workspace 與其他 Device 不會被刪除。",
                "目前裝置已撤銷",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            if (!ConfirmDestructiveReset(cloudExit: true) || !ConfirmFinalReset())
            {
                MessageBox.Show(
                    this,
                    "本機資料目前仍完整保留，但這台 Device 已失效。在完成本機重設或重新加入 Workspace 前，不應繼續執行 Cloud 操作。",
                    "雲端裝置已失效",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            LocalResetApplication.Schedule(new LocalResetExecutionRequest(LocalResetKind.BuiltInCloud));
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "裝置管理狀態確認失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task BeginLocalResetAsync()
    {
        if (repository.Settings.LoadOrCreate().CloudMode != CloudModes.LocalOnly) return;
        using var login = new EmployeeAdminLoginForm(repository,"重設本機資料－管理員驗證");
        if (login.ShowDialog(this) != DialogResult.OK) return;
        if (!ConfirmDestructiveReset(cloudExit: false)) return;
        if (!ConfirmFinalReset()) return;
        await Task.Yield();
        LocalResetApplication.Schedule(new LocalResetExecutionRequest(LocalResetKind.Local));
    }

    private bool ConfirmDestructiveReset(bool cloudExit)
    {
        var message = cloudExit
            ? "切回單機版不是單純切換模式。執行後會永久刪除這台電腦的所有 CYInvoice 本機資料，包括發票與明細、本機／雲端員工快取、程式設定、PDF 與預覽快取。\n\nCloud Workspace、其他 Device 與中央資料不會被刪除。\n\n是否繼續？"
            : "重設單機版會永久刪除這台電腦的所有 CYInvoice 本機資料，包括發票與明細、員工帳號、程式設定、PDF 與預覽快取。\n\n完成後會回到首次使用狀態。\n\n是否繼續？";
        return MessageBox.Show(
            this,
            message,
            cloudExit ? "確認切回單機版" : "確認重設本機資料",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private bool ConfirmFinalReset() =>
        MessageBox.Show(
            this,
            "最後確認：本機資料刪除後無法復原。CYInvoice 會先完整關閉，再執行雲端退出／本機清除；完成後重新開啟並回到首次使用。\n\n確定立即執行？",
            "最後確認",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private static bool HasCloudIdentity(Settings value) =>
        value.CloudWorkspaceId.Length != 0
        && value.CloudDeviceId.Length != 0
        && value.CloudDeviceTokenEncrypted.Length != 0;

    private static bool SameCloudEndpoint(string left, string right)
    {
        static string Normalize(string value)
        {
            value = value.Trim();
            if (value.Length == 0) return string.Empty;
            return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
        }

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    internal void VerifySmokeLayout()
    {
        if (Text!="設定選單" || ShowIcon || AcceptButton is not null || choices.Controls.Count!=2 ||
            test.Parent!=production.Parent ||
            test.Checked==production.Checked || test.Text!="光貿測試環境\n12345678" || !companyCard.Enabled ||
            !cloudSettings.Visible || Controls.OfType<GroupBox>().Any())
            throw new InvalidOperationException("設定公司卡、環境選取或連線入口配置不正確");
        if (Math.Abs(test.Width-production.Width)>1 || test.Height!=production.Height ||
            companyCard.Parent!=environmentTitle.Parent || companyCard.Left<=environmentTitle.Right ||
            environmentTitle.Font.SizeInPoints!=connectionTitle.Font.SizeInPoints ||
            environmentTitle.Text!="選擇使用環境" ||
            connectionTitle.PointToScreen(Point.Empty).Y>=environmentTitle.PointToScreen(Point.Empty).Y)
            throw new InvalidOperationException("公司卡尺寸、編輯按鈕與分組標題比例不一致");
        foreach(var card in new[]{test,production})
            if(card.Appearance!=Appearance.Button || card.Controls.Count!=0 ||
                card.Text.Contains("已選取") || card.Text.Contains('✓') ||
                Math.Abs(card.Font.SizeInPoints-12F)>0.1F ||
                card.Width<=0 || card.Height<card.PreferredSize.Height)
                throw new InvalidOperationException("公司卡文字遭裁切");
        var wasProduction=production.Checked;
        production.PerformClick();
        if(!production.Checked || test.Checked)throw new InvalidOperationException("正式公司卡未維持原生單選");
        test.PerformClick();
        if(!test.Checked || production.Checked)throw new InvalidOperationException("測試公司卡未維持原生單選");
        if(wasProduction)production.PerformClick();
    }
}
