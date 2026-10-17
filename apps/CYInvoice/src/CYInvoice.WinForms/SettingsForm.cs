using CYInvoice.Core;
using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class SettingsForm : Form
{
    private readonly LocalRepository repository;
    private readonly Settings settings;
    private readonly ToolTip toolTip = new();
    private readonly RadioButton localMode = new() { Text = "單機版", AutoSize = true };
    private readonly RadioButton cloudMode = new() { Text = "雲端版", AutoSize = true };
    private readonly RadioButton test = new() { Text = "光貿測試環境", AutoSize = true };
    private readonly RadioButton production = new() { Text = "正式公司", AutoSize = true };
    private readonly TextBox invoice = UiControls.TextBox(8);
    private readonly TextBox appKey = UiControls.TextBox(200);
    private readonly TextBox moPassword = UiControls.TextBox(200);
    private readonly Button cloudSettings = UiControls.StandardButton("雲端連線設定");
    private readonly Button deviceManagement = UiControls.StandardButton("裝置管理");
    private readonly Button companyCard = UiControls.StandardButton("CYID 公司卡");
    private readonly Button localReset = UiControls.StandardButton("重設本機資料");
    private readonly Button diagnostics = UiControls.StandardButton("系統診斷");
    private readonly Button save = UiControls.StandardButton("儲存設定");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private GroupBox modeGroup = null!;
    private BufferedTableLayoutPanel environmentLayout = null!;
    private BufferedFlowLayoutPanel modeChoices = null!;
    private BufferedTableLayoutPanel platformLayout = null!;
    private Label invoiceLabel = null!;
    private Label appKeyLabel = null!;
    private Label moPasswordLabel = null!;
    private BufferedFlowLayoutPanel actionButtons = null!;
    private bool resetFlowBusy;

    public SettingsForm(LocalRepository repository)
    {
        this.repository = repository;
        settings = repository.Settings.LoadOrCreate();
        Text = "設定選單";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 380);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();

        SuspendLayout();
        BuildLayout();
        LoadValues();
        UpdateEnvironmentFields();
        UpdateCloudControls();
        test.CheckedChanged += EnvironmentChanged;
        production.CheckedChanged += EnvironmentChanged;
        localMode.CheckedChanged += CloudModeChanged;
        cloudMode.CheckedChanged += CloudModeChanged;
        ResumeLayout(false);
        PerformLayout();
    }

    private void BuildLayout()
    {
        appKey.UseSystemPasswordChar = true;
        moPassword.UseSystemPasswordChar = true;
        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10),
            Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

        modeGroup = new GroupBox { Text = "運作模式", Dock = DockStyle.Fill };
        modeChoices = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(6, 5, 0, 0),
            Margin = Padding.Empty,
        };
        localMode.Margin = new Padding(0, 5, 10, 0);
        cloudMode.Margin = new Padding(0, 5, 5, 0);
        cloudSettings.Width = 116;
        cloudSettings.Margin = new Padding(0, 0, 4, 0);
        deviceManagement.Width = 92;
        deviceManagement.Margin = Padding.Empty;
        localReset.Width = 112;
        localReset.Margin = new Padding(4, 0, 0, 0);
        cloudSettings.Click += (_, _) => OpenCloudSettings();
        deviceManagement.Click += async (_, _) => await OpenDeviceManagementAsync();
        localReset.Click += async (_, _) => await BeginLocalResetAsync();
        modeChoices.Controls.Add(localMode);
        modeChoices.Controls.Add(cloudMode);
        modeChoices.Controls.Add(cloudSettings);
        modeChoices.Controls.Add(deviceManagement);
        modeChoices.Controls.Add(localReset);
        modeGroup.Controls.Add(modeChoices);

        var environmentGroup = new GroupBox { Text = "使用環境", Dock = DockStyle.Fill };
        environmentLayout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(6, 3, 6, 3),
            Margin = Padding.Empty,
        };
        environmentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        environmentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        environmentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        environmentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        environmentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        environmentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        environmentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        environmentLayout.Controls.Add(test, 0, 0);
        environmentLayout.SetColumnSpan(test, 3);
        environmentLayout.Controls.Add(production, 0, 1);
        environmentLayout.SetColumnSpan(production, 2);
        companyCard.Width = 116;
        companyCard.Height = 24; // Preserve the existing compact layout.
        companyCard.Margin = Padding.Empty;
        companyCard.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        companyCard.Click += async (_, _) => await ShowCompanyCardAsync();
        environmentLayout.Controls.Add(companyCard, 2, 1);
        invoiceLabel = FieldLabel("統編");
        appKeyLabel = FieldLabel("App Key");
        environmentLayout.Controls.Add(invoiceLabel, 1, 2);
        environmentLayout.Controls.Add(invoice, 2, 2);
        environmentLayout.Controls.Add(appKeyLabel, 1, 3);
        environmentLayout.Controls.Add(appKey, 2, 3);
        environmentGroup.Controls.Add(environmentLayout);

        var platformGroup = new GroupBox { Text = "平台檔案密碼", Dock = DockStyle.Fill };
        platformLayout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(6, 5, 6, 5),
            Margin = Padding.Empty,
        };
        platformLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        platformLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        platformLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        moPasswordLabel = FieldLabel("MO店+");
        platformLayout.Controls.Add(moPasswordLabel, 1, 0);
        platformLayout.Controls.Add(moPassword, 2, 0);
        toolTip.SetToolTip(
            moPasswordLabel,
            "輸入 MO店+ 匯出 Excel 的保護密碼；留白會保留目前已儲存的密碼。\n未設定時只會停用 MO店+ 匯入，不影響其他功能。");
        platformGroup.Controls.Add(platformLayout);

        cancel.DialogResult = DialogResult.Cancel;
        diagnostics.Width = 112;
        save.Width = 112;
        cancel.Width = 112;
        diagnostics.Click += (_, _) =>
        {
            using var form = new SystemDiagnosticsForm(repository);
            form.ShowDialog(this);
        };
        save.Click += SaveClicked;
        actionButtons = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0),
            Margin = Padding.Empty,
        };
        actionButtons.SizeChanged += (_, _) => CenterButtons(actionButtons, 6);
        actionButtons.Controls.Add(diagnostics);
        actionButtons.Controls.Add(save);
        actionButtons.Controls.Add(cancel);

        root.Controls.Add(modeGroup, 0, 0);
        root.Controls.Add(environmentGroup, 0, 1);
        root.Controls.Add(platformGroup, 0, 2);
        root.Controls.Add(actionButtons, 0, 3);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void OpenCloudSettings()
    {
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

        var endpointChanged = originalEndpoint.Length != 0
            && !SameCloudEndpoint(originalEndpoint, form.SelectedBaseUrl);
        var identityAlreadyMovedToSelectedEndpoint = HasCloudIdentity(settings)
            && SameCloudEndpoint(settings.CloudBaseUrl, form.SelectedBaseUrl);
        if (endpointChanged && !identityAlreadyMovedToSelectedEndpoint)
        {
            if (HasPendingCloudOperation(settings))
            {
                MessageBox.Show(
                    this,
                    "目前仍有未完成的雲端初始化／裝置加入憑證。為避免斷線後遺失復原能力，必須先回原 Cloud 完成或確認結果，暫時不能切換 API 網址。",
                    "尚有待確認的雲端作業",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                settings.CloudBaseUrl = originalEndpoint;
                UpdateCloudControls();
                return;
            }

            if (HasCloudIdentity(settings))
                repository.Settings.ClearCloudIdentity(settings);
        }
        settings.CloudBaseUrl = form.SelectedBaseUrl;

        if (form.IdentityCompleted)
        {
            cloudMode.Checked = true;
            localMode.Checked = false;
        }
        UpdateCloudControls();
    }

    // The D1 company card is distinct from local DPAPI AMEGO/MO settings;
    // until secure per-Device provisioning is implemented, invoice operations
    // still use the original local credentials. Never conflate them.
    private async Task ShowCompanyCardAsync()
    {
        if (resetFlowBusy || !companyCard.Enabled) return;
        using var login = new EmployeeAdminLoginForm(repository,
            "CYID 公司卡－管理員驗證", collectCredentialsOnly: true);
        if (login.ShowDialog(this) != DialogResult.OK) return;
        companyCard.Enabled = false;
        try
        {
            var current = repository.Settings.LoadOrCreate();
            if (current.CloudMode != CloudModes.CloudPreferred ||
                current.CloudIdentityProvider != "CYID" ||
                !current.CloudEmployeeAuthorityReady ||
                !HasCloudIdentity(current) ||
                string.IsNullOrWhiteSpace(current.CloudBaseUrl))
                throw new InvalidOperationException("請先完成 CYID 雲端加入與驗證。");
            var token = repository.Settings.CloudDeviceToken(current);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("找不到有效的 CYID Cloud Device 身分。");
            // POST carries CYID credentials (and potentially App Key / MO password).
            // Do not forward it to any Location redirect, even another HTTPS host.
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            var client = new CloudCompanySettingsClient(http,
                new Uri(current.CloudBaseUrl, UriKind.Absolute), token);
            var card = await client.GetAsync(login.EnteredEmployeeNo,
                login.AuthenticatedPassword);
            var message = card is null
                ? "此 Workspace 尚未建立公司設定卡。"
                : $"公司統編：{card.SellerInvoice}\n設定版次：{card.Revision}\n"+
                  $"光貿 App Key：{(card.HasAmegoAppKey ? "已設定" : "尚未設定")}\n"+
                  $"MO店+ 密碼：{(card.HasMoExcelPassword ? "已設定" : "尚未設定")}";
            message += CompanyCardLocalUsageMessage(card, current);
            var action = MessageBox.Show(this,message+
                "\n\n【是】編輯雲端公司卡（超管）"+
                "\n【否】將雲端公司卡套用到此電腦（超管）"+
                "\n【取消】關閉；不修改本機憑證。",
                "CYID 公司卡",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button3);
            if(action==DialogResult.Cancel)return;
            if(action==DialogResult.No)
            {
                await InstallCompanyCardAsync(client,card,current,
                    login.EnteredEmployeeNo,login.AuthenticatedPassword);
                return;
            }
            var edit=PromptCompanyCardEdit(card);
            if(edit is null)return;
            if(MessageBox.Show(this,
                $"確定修改 CYID 公司卡（原版次 {edit.ExpectedRevision}）？\n"+
                $"公司統編：{edit.SellerInvoice}\n"+
                $"光貿 App Key：{SecretActionLabel(edit.AmegoAppKey.Action)}\n"+
                $"MO店+ 密碼：{SecretActionLabel(edit.MoExcelPassword.Action)}\n\n"+
                "僅超級管理員可修改，且雲端會記錄永久稽核。\n"+
                "系統只送出一次更新要求。",
                "確認雲端公司設定",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2)!=DialogResult.Yes)
                return;
            try
            {
                var updated=await client.UpdateAsync(edit,
                    login.EnteredEmployeeNo,login.AuthenticatedPassword);
                MessageBox.Show(this,
                    $"CYID 公司卡更新已確認。\n統編：{updated.SellerInvoice}\n"+
                    $"版次：{updated.Revision}\n"+
                    $"光貿 App Key：{(updated.HasAmegoAppKey?"已設定":"尚未設定")}\n"+
                    $"MO店+ 密碼：{(updated.HasMoExcelPassword?"已設定":"尚未設定")}\n\n"+
                    CompanyCardLocalUsageMessage(updated, current),"公司卡已更新",
                    MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
            catch(Exception error)
            {
                CloudCompanySettingsStatus? latest=null;
                try{latest=await client.GetAsync(login.EnteredEmployeeNo,
                    login.AuthenticatedPassword);}
                catch{/* The result remains ambiguous, never retry UPDATE. */}
                var observed=latest is null?"目前無法查回最新雲端狀態。":
                    $"雲端目前統編：{latest.SellerInvoice}，版次：{latest.Revision}。";
                var reason=error is CloudApiException ex&&
                    ex.Code=="COMPANY_AUTH_REQUIRED"
                    ?"只有 CYID 超級管理員可以修改公司卡。":
                    "更新結果未確認；可能已寫入，也可能遇到版次衝突或權限變動。";
                MessageBox.Show(this,reason+"\n"+observed+
                    "\n請核對雲端內容，再決定是否重新編輯。"+
                    "\n系統沒有自動重送，也未修改本機設定。",
                    "公司卡更新待確認",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(this,
                "目前無法確認 CYID 公司卡狀態：" + error.Message +
                "\n本機設定未變更。雲端若有未確認的更新，請重新查回公司卡。",
                "公司設定查詢未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed && !Disposing) UpdateCloudControls();
        }
    }

    // Explicit super-admin provisioning only; normal CYID status read never
    // receives secrets. Existing local AMEGO/MO credentials are untouched on
    // any failure, denial, timeout or stale cloud revision.
    private async Task InstallCompanyCardAsync(
        CloudCompanySettingsClient client,CloudCompanySettingsStatus? card,
        Settings original,string employeeNo,string password)
    {
        if(card is null||!card.HasAmegoAppKey||!card.HasMoExcelPassword)
        {
            MessageBox.Show(this,
                "雲端公司卡的統編、光貿 App Key 與 MO店+ 密碼尚未全部設定；請先由超級管理員補齊。",
                "公司卡尚未就緒",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return;
        }
        if(MessageBox.Show(this,
            $"確定以 CYID 公司卡（版次 {card.Revision}）覆寫這台電腦的正式開票與 MO店+ 憑證？"+
            $"\n雲端統編：{card.SellerInvoice}\n本機統編：{original.ProductionInvoice}"+
            "\n\n本機憑證將使用 Windows 原本的 DPAPI 加密。"+
            "\n其他電腦不受影響；雲端中斷時仍沿用本機既有憑證。",
            "確認套用本機",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2)!=DialogResult.Yes)
            return;
        try
        {
            var received=await client.ProvisionAsync(employeeNo,password);
            if(!received.Matches(card))
                throw new InvalidOperationException("公司卡版次或設定已改變；請重新讀取雲端狀態。");
            var latest=repository.Settings.LoadOrCreate();
            if(latest.CloudMode!=CloudModes.CloudPreferred||
               latest.CloudIdentityProvider!="CYID"||!latest.CloudEmployeeAuthorityReady||
               latest.CloudWorkspaceId!=original.CloudWorkspaceId||
               latest.CloudDeviceId!=original.CloudDeviceId||
               latest.CloudBaseUrl!=original.CloudBaseUrl||
               latest.CloudDeviceTokenEncrypted!=original.CloudDeviceTokenEncrypted)
                throw new InvalidOperationException("雲端裝置狀態已改變，沒有覆寫本機設定。");
            if(latest.ProductionInvoice!=original.ProductionInvoice||
               latest.ProductionAppKeyEncrypted!=original.ProductionAppKeyEncrypted||
               latest.MoPasswordEncrypted!=original.MoPasswordEncrypted)
                throw new InvalidOperationException("本機憑證在確認期間已變更，請重新開啟設定。");
            // Prepare a fresh detached settings object; Save is a single
            // atomic JSON replace. Failed DPAPI/Save leaves originals intact.
            repository.Settings.SetProductionAppKey(latest,received.AmegoAppKey);
            repository.Settings.SetMoPassword(latest,received.MoExcelPassword);
            latest.ProductionInvoice=received.SellerInvoice;
            repository.Settings.StampCloudCompanyProvision(latest,received.Revision);
            repository.Settings.Save(latest);
            settings.ProductionInvoice=latest.ProductionInvoice;
            settings.ProductionAppKeyEncrypted=latest.ProductionAppKeyEncrypted;
            settings.MoPasswordEncrypted=latest.MoPasswordEncrypted;
            settings.CloudCompanyAppliedRevision=latest.CloudCompanyAppliedRevision;
            settings.CloudCompanyAppliedWorkspaceId=latest.CloudCompanyAppliedWorkspaceId;
            settings.CloudCompanyAppliedDeviceId=latest.CloudCompanyAppliedDeviceId;
            settings.CloudCompanyAppliedSellerInvoice=latest.CloudCompanyAppliedSellerInvoice;
            LoadValues();
            MessageBox.Show(this,
                $"已將公司卡版次 {received.Revision} 套用這台電腦；"+
                "\n本機 App Key／MO店+ 密碼已使用 DPAPI 保存。"+
                "\n其他電腦需各自經超級管理員授權套用。",
                "公司卡已套用",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        catch
        {
            // No exception details / stack trace / server response may include
            // a secret; never format exception into a UI or telemetry log.
            MessageBox.Show(this,
                "未能確認超管授權、公司卡版次、裝置或本機儲存狀態。"+
                "\n本機原有憑證不會因未確認的同步而自動清除或重試。"+
                "\n請重新查閱公司卡狀態後再操作。",
                "公司卡套用未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }

    private static string CompanyCardLocalUsageMessage(
        CloudCompanySettingsStatus? card, Settings local)
    {
        var state=CloudCompanySettingsUsage.Inspect(card, local);
        var seller=state.HasLocalSellerInvoice?local.ProductionInvoice:"尚未設定";
        var message="\n\n目前 Windows 實際開票仍使用本機設定："+
            $"\n統編：{seller}"+
            $"\nApp Key：{(state.LocalAmegoKeyConfigured?"本機已有加密設定":"本機尚未設定")}"+
            $"\nMO店+ 密碼：{(state.LocalMoPasswordConfigured?"本機已有加密設定":"本機尚未設定")}";
        if (state.HasCloudCard && !state.SellerInvoiceMatches)
            message+="\n注意：雲端公司卡與本機開票統編不一致，請管理員核對。";
        if (card is not null && card.HasAmegoAppKey && !state.LocalAmegoKeyConfigured)
            message+="\n注意：雲端已有 App Key，但本機尚無可用金鑰。";
        if (card is not null && card.HasMoExcelPassword && !state.LocalMoPasswordConfigured)
            message+="\n注意：雲端已有 MO 密碼，但本機尚無可用密碼。";
        if(state.CompanyCardNeedsReview)
            message+="\n注意：本機套用的公司卡版次已與目前雲端狀態不同；請由超管確認後重新套用。";
        else if(state.CompanyCardAppliedToCurrentDevice)
            message+=$"\n本機已套用這台 Device 的雲端公司卡版次 {card!.Revision}。";
        else
            message+="\n本機設定並無與目前雲端公司卡相同版次的套用紀錄。";
        return message+"\n雲端「已設定」不代表兩邊密碼內容相同。"+
            "\n如需使用雲端公司卡，超管可在此明確確認後套用本機；不會自動覆寫。";
    }

    // Temporary editor under the existing Settings menu; no new settings
    // system. Empty secret fields always mean KEEP, never implicit deletion.
    private CloudCompanySettingsEdit? PromptCompanyCardEdit(
        CloudCompanySettingsStatus? current)
    {
        using var editor=new Form{
            Text="編輯 CYID 公司卡（超級管理員）",
            StartPosition=FormStartPosition.CenterParent,
            ClientSize=new Size(490,260),
            FormBorderStyle=FormBorderStyle.FixedDialog,
            MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false,ShowIcon=false,
            Font=new Font("Microsoft JhengHei UI",10F)
        };
        var table=new TableLayoutPanel{
            Dock=DockStyle.Fill,ColumnCount=3,RowCount=5,Padding=new Padding(12)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,65));
        foreach(var h in new[]{40,35,43,43,50})
            table.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        var note=new Label{
            Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
            Text="金鑰留白＝保留原值；只有明確勾選清除才會刪除。"
        };
        table.Controls.Add(note,0,0);
        table.SetColumnSpan(note,3);
        var seller=UiControls.TextBox(8);
        seller.Text=current?.SellerInvoice??string.Empty;
        var key=UiControls.TextBox(512);
        var mo=UiControls.TextBox(256);
        key.UseSystemPasswordChar=true;
        mo.UseSystemPasswordChar=true;
        key.PlaceholderText="留白保留雲端 App Key";
        mo.PlaceholderText="留白保留雲端密碼";
        var clearKey=new CheckBox{Text="清除",AutoSize=true,
            Enabled=current?.HasAmegoAppKey==true};
        var clearMo=new CheckBox{Text="清除",AutoSize=true,
            Enabled=current?.HasMoExcelPassword==true};
        clearKey.CheckedChanged+=(_,_)=>{key.Enabled=!clearKey.Checked;
            if(clearKey.Checked)key.Clear();};
        clearMo.CheckedChanged+=(_,_)=>{mo.Enabled=!clearMo.Checked;
            if(clearMo.Checked)mo.Clear();};
        table.Controls.Add(new Label{Text="公司統編",Dock=DockStyle.Fill,
            TextAlign=ContentAlignment.MiddleLeft},0,1);
        table.Controls.Add(seller,1,1);
        table.SetColumnSpan(seller,2);
        table.Controls.Add(new Label{Text="光貿 App Key",Dock=DockStyle.Fill,
            TextAlign=ContentAlignment.MiddleLeft},0,2);
        table.Controls.Add(key,1,2);
        table.Controls.Add(clearKey,2,2);
        table.Controls.Add(new Label{Text="MO店+ 密碼",Dock=DockStyle.Fill,
            TextAlign=ContentAlignment.MiddleLeft},0,3);
        table.Controls.Add(mo,1,3);
        table.Controls.Add(clearMo,2,3);
        var buttons=new FlowLayoutPanel{
            Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft
        };
        var apply=UiControls.StandardButton("確認修改");
        var cancel=UiControls.StandardButton("取消");
        cancel.DialogResult=DialogResult.Cancel;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(apply);
        table.Controls.Add(buttons,0,4);
        table.SetColumnSpan(buttons,3);
        editor.Controls.Add(table);
        editor.CancelButton=cancel;
        apply.Click+=(_,_)=>{
            var sellerId=seller.Text.Trim();
            if(sellerId.Length!=8||!sellerId.All(char.IsAsciiDigit)){
                MessageBox.Show(editor,"請輸入八位數公司統編。",
                    "統編不正確",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                return;
            }
            if(sellerId==(current?.SellerInvoice??string.Empty)&&
                key.Text.Length==0&&mo.Text.Length==0&&
                !clearKey.Checked&&!clearMo.Checked){
                MessageBox.Show(editor,"沒有資料變更，請取消。",
                    "沒有修改",MessageBoxButtons.OK,MessageBoxIcon.Information);
                return;
            }
            editor.DialogResult=DialogResult.OK;
        };
        if(editor.ShowDialog(this)!=DialogResult.OK)return null;
        var keyAction=clearKey.Checked?CloudCompanySecretChange.Clear():
            key.Text.Length!=0?CloudCompanySecretChange.Replace(key.Text):
                CloudCompanySecretChange.Keep();
        var moAction=clearMo.Checked?CloudCompanySecretChange.Clear():
            mo.Text.Length!=0?CloudCompanySecretChange.Replace(mo.Text):
                CloudCompanySecretChange.Keep();
        var change=new CloudCompanySettingsEdit(current?.Revision??0,
            seller.Text.Trim(),keyAction,moAction);
        key.Clear();
        mo.Clear();
        return change;
    }

    private static string SecretActionLabel(string action)=>action switch{
        "replace"=>"取代（不顯示內容）",
        "clear"=>"明確清除",
        _=>"保留原值"
    };

    private async Task OpenDeviceManagementAsync()
    {
        try
        {
            if (!cloudMode.Checked || settings.CloudMode != CloudModes.CloudPreferred
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

    private void EnvironmentChanged(object? sender, EventArgs eventArgs)
    {
        SuspendLayout();
        environmentLayout.SuspendLayout();
        try
        {
            UpdateEnvironmentFields();
        }
        finally
        {
            environmentLayout.ResumeLayout(false);
            ResumeLayout(false);
        }
    }

    private void CloudModeChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is RadioButton radio && !radio.Checked) return;
        UpdateCloudControls();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && production.Checked)
        {
            if (invoice.ContainsFocus)
            {
                appKey.Focus();
                appKey.SelectAll();
                return true;
            }

            if (appKey.ContainsFocus)
            {
                save.PerformClick();
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void LoadValues()
    {
        localMode.Checked = settings.CloudMode == CloudModes.LocalOnly;
        cloudMode.Checked = settings.CloudMode is CloudModes.CloudTransition or CloudModes.CloudPreferred;
        test.Checked = settings.Environment == Environments.Test;
        production.Checked = settings.Environment == Environments.Production;
        invoice.Text = settings.ProductionInvoice;
        appKey.PlaceholderText = settings.ProductionAppKeyEncrypted.Length == 0
            ? ""
            : "留白會保留目前已儲存的 App Key。";
        moPassword.PlaceholderText = settings.MoPasswordEncrypted.Length == 0
            ? "尚未設定"
            : "留白會保存目前已儲存的密碼";
    }

    private void UpdateCloudControls()
    {
        modeGroup.Text = settings.CloudIdentityProvider == "CYID" ? "運作模式（CYID）" : "運作模式";
        cloudSettings.Visible = cloudMode.Checked;
        deviceManagement.Visible = cloudMode.Checked;
        companyCard.Visible = cloudMode.Checked;
        localReset.Visible = localMode.Checked && settings.CloudMode == CloudModes.LocalOnly;
        cloudSettings.Enabled = cloudMode.Checked && !resetFlowBusy;
        deviceManagement.Enabled = cloudMode.Checked
            && !resetFlowBusy
            && settings.CloudMode == CloudModes.CloudPreferred
            && settings.CloudEmployeeAuthorityReady
            && HasCloudIdentity(settings)
            && !string.IsNullOrWhiteSpace(settings.CloudBaseUrl);
        companyCard.Enabled = cloudMode.Checked && !resetFlowBusy
            && settings.CloudMode == CloudModes.CloudPreferred
            && settings.CloudIdentityProvider == "CYID"
            && settings.CloudEmployeeAuthorityReady
            && HasCloudIdentity(settings)
            && !string.IsNullOrWhiteSpace(settings.CloudBaseUrl);
        localReset.Enabled = localReset.Visible && !resetFlowBusy;
        save.Enabled = !resetFlowBusy;
        cancel.Enabled = !resetFlowBusy;
    }

    private void UpdateEnvironmentFields()
    {
        var locked = !production.Checked;
        if (invoice.ReadOnly != locked) UiControls.SetTextBoxLocked(invoice, locked);
        if (appKey.ReadOnly != locked) UiControls.SetTextBoxLocked(appKey, locked);
    }

    private async void SaveClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (resetFlowBusy) return;
            if (!localMode.Checked && !cloudMode.Checked)
                throw new InvalidOperationException("請選擇單機版或雲端版。");

            if (localMode.Checked && settings.CloudMode != CloudModes.LocalOnly)
            {
                await BeginCloudToLocalResetAsync();
                return;
            }

            if (cloudMode.Checked && string.IsNullOrWhiteSpace(settings.CloudBaseUrl))
                throw new InvalidOperationException("請先按「雲端連線設定」完成 Cloud API 連線設定。");
            if (cloudMode.Checked && !HasCloudIdentity(settings))
                throw new InvalidOperationException("請先完成建立／加入雲端空間；只有 API 網址尚不能切換成雲端版。");

            // Opening the settings window does not authorize a later write.
            using var authorization = new EmployeeAdminLoginForm(repository, "儲存設定－管理員驗證");
            if (authorization.ShowDialog(this) != DialogResult.OK) return;
            var latest = repository.Settings.LoadOrCreate();
            if (latest.CloudIdentityProvider != settings.CloudIdentityProvider
                || latest.CyIdBindingEncrypted != settings.CyIdBindingEncrypted)
                throw new InvalidOperationException("雲端驗證設定已更新，請重新開啟設定視窗後儲存。");
            // The company card may have been applied from another window.
            // Never overwrite its credentials using this old Settings snapshot.
            if (latest.ProductionInvoice != settings.ProductionInvoice ||
                latest.ProductionAppKeyEncrypted != settings.ProductionAppKeyEncrypted ||
                latest.MoPasswordEncrypted != settings.MoPasswordEncrypted ||
                latest.CloudCompanyAppliedRevision != settings.CloudCompanyAppliedRevision ||
                latest.CloudCompanyAppliedDeviceId != settings.CloudCompanyAppliedDeviceId)
                throw new InvalidOperationException("本機公司憑證已在其他視窗更新，請重新開啟設定。");

            if (cloudMode.Checked)
            {
                settings.CloudMode = settings.CloudEmployeeAuthorityReady
                    ? CloudModes.CloudPreferred
                    : CloudModes.CloudTransition;
            }
            else
            {
                settings.CloudMode = CloudModes.LocalOnly;
            }

            settings.Environment = production.Checked ? Environments.Production : Environments.Test;
            var manuallyChangedSeller = settings.ProductionInvoice != invoice.Text.Trim();
            settings.ProductionInvoice = invoice.Text.Trim();
            if (manuallyChangedSeller) repository.Settings.ClearCloudCompanyStamp(settings);
            if (appKey.Text.Length != 0) repository.Settings.SetProductionAppKey(settings, appKey.Text);
            if (moPassword.Text.Length != 0) repository.Settings.SetMoPassword(settings, moPassword.Text);
            repository.Settings.Save(settings);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "無法儲存設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task BeginCloudToLocalResetAsync()
    {
        if (!ConfirmDestructiveReset(cloudExit: true)) return;
        await RunResetPreflightAsync(async () =>
        {
            var assessment = await LocalResetCoordinator.AssessBuiltInCloudAsync(repository);
            if (assessment.Disposition == CloudExitDisposition.LastActiveDevice)
            {
                MessageBox.Show(
                    this,
                    "這是 Built-in Cloud Workspace 最後一台使用中的裝置，目前不能直接切回單機版。\n\n" +
                    "若要保留 Workspace：請先讓另一台電腦加入成為可信任 Device。\n\n" +
                    "若不再使用 Workspace：請到中央 Workspace 管理端手動停用 Workspace，再回來重試。\n\n" +
                    "CYInvoice 桌面程式不提供 Workspace 停用或刪除功能。",
                    "最後一台雲端裝置",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var employeeNo = string.Empty;
            var password = string.Empty;
            if (assessment.Disposition == CloudExitDisposition.ReadyForRevoke)
            {
                using var credentials = new CloudDeviceRevokeAuthenticationForm(
                    assessment.DeviceDisplayName.Length == 0 ? "目前這台電腦" : assessment.DeviceDisplayName,
                    currentDevice: true);
                if (credentials.ShowDialog(this) != DialogResult.OK) return;
                employeeNo = credentials.EmployeeNo;
                password = credentials.Password;
            }

            if (!ConfirmFinalReset()) return;
            LocalResetApplication.Schedule(new LocalResetExecutionRequest(
                LocalResetKind.BuiltInCloud,
                employeeNo,
                password));
        });
    }

    private async Task BeginLocalResetAsync()
    {
        if (settings.CloudMode != CloudModes.LocalOnly || !localMode.Checked) return;
        if (!ConfirmDestructiveReset(cloudExit: false)) return;
        if (!ConfirmFinalReset()) return;
        await Task.Yield();
        LocalResetApplication.Schedule(new LocalResetExecutionRequest(LocalResetKind.Local));
    }

    private async Task RunResetPreflightAsync(Func<Task> action)
    {
        if (resetFlowBusy) return;
        resetFlowBusy = true;
        UseWaitCursor = true;
        UpdateCloudControls();
        try
        {
            await action();
        }
        catch (CloudApiException error)
        {
            MessageBox.Show(
                this,
                $"目前無法向 Cloud 確認退出條件。\n\n{error.Message}\n\n本機資料沒有變更。",
                "無法切回單機版",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                $"目前無法安全確認雲端狀態。\n\n{error.Message}\n\n本機資料沒有變更。",
                "無法切回單機版",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            resetFlowBusy = false;
            if (!IsDisposed && !Disposing)
            {
                UseWaitCursor = false;
                UpdateCloudControls();
            }
        }
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

    private static bool HasPendingCloudOperation(Settings value) =>
        value.CloudPendingBootstrapTokenEncrypted.Length != 0
        || value.CloudPendingDeviceJoinTokenEncrypted.Length != 0;

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

    private static void CenterButtons(FlowLayoutPanel panel, int topPadding)
    {
        var contentWidth = panel.Controls.Cast<Control>().Sum(control => control.Width + control.Margin.Horizontal);
        var left = Math.Max(0, (panel.ClientSize.Width - contentWidth) / 2);
        if (panel.Padding.Left == left && panel.Padding.Top == topPadding) return;
        panel.Padding = new Padding(left, topPadding, 0, 0);
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = false,
        Margin = new Padding(3),
    };

    internal void VerifySmokeLayout()
    {
        if (Text != "設定選單" || ShowIcon || invoice.ReadOnly != !production.Checked || appKey.ReadOnly != !production.Checked ||
            !invoice.Enabled || !appKey.Enabled)
            throw new InvalidOperationException("設定視窗標題或測試與正式環境欄位鎖定狀態不一致");
        if (AcceptButton is not null)
            throw new InvalidOperationException("設定視窗不應使用表單預設 AcceptButton，Enter 必須依欄位明確處理");
        if (settings.ProductionAppKeyEncrypted.Length != 0 &&
            appKey.PlaceholderText != "留白會保留目前已儲存的 App Key。")
            throw new InvalidOperationException("App Key 保留提示未放在輸入欄位內");
        if (settings.MoPasswordEncrypted.Length != 0 &&
            moPassword.PlaceholderText != "留白會保存目前已儲存的密碼")
            throw new InvalidOperationException("MO店+ 密碼保留提示文字不正確");
        if (settings.MoPasswordEncrypted.Length == 0 && moPassword.PlaceholderText != "尚未設定")
            throw new InvalidOperationException("未設定 MO店+ 密碼提示文字不正確");
        if (companyCard.Visible &&
            (environmentLayout.GetPositionFromControl(companyCard).Row != 1 ||
             environmentLayout.GetPositionFromControl(companyCard).Column != 2))
            throw new InvalidOperationException("CYID 公司卡位置不正確");
        if (environmentLayout.GetPositionFromControl(production).Row != 1 ||
            environmentLayout.GetColumnSpan(production) != 2 ||
            environmentLayout.GetPositionFromControl(invoiceLabel).Column != 1 ||
            environmentLayout.GetPositionFromControl(appKeyLabel).Column != 1 ||
            invoiceLabel.AutoEllipsis || appKeyLabel.AutoEllipsis)
            throw new InvalidOperationException("正式公司、統編與 App Key 未依指定方式排列");
        var environmentX = invoiceLabel.PointToScreen(Point.Empty).X;
        var platformX = moPasswordLabel.PointToScreen(Point.Empty).X;
        var expectedDeviceManagementEnabled = cloudMode.Checked
            && settings.CloudMode == CloudModes.CloudPreferred
            && settings.CloudEmployeeAuthorityReady
            && HasCloudIdentity(settings)
            && !string.IsNullOrWhiteSpace(settings.CloudBaseUrl);
        var expectedCompanyCardEnabled = expectedDeviceManagementEnabled &&
            settings.CloudIdentityProvider == "CYID";
        var expectedLocalResetVisible = localMode.Checked && settings.CloudMode == CloudModes.LocalOnly;
        if (string.IsNullOrEmpty(toolTip.GetToolTip(moPasswordLabel)) ||
            appKeyLabel.PreferredWidth > appKeyLabel.Width ||
            cloudSettings.PreferredSize.Width > cloudSettings.Width ||
            deviceManagement.PreferredSize.Width > deviceManagement.Width ||
            localReset.PreferredSize.Width > localReset.Width ||
            diagnostics.Text != "系統診斷" || cloudSettings.Text != "雲端連線設定" ||
            deviceManagement.Text != "裝置管理" || companyCard.Text != "CYID 公司卡" ||
            localReset.Text != "重設本機資料" ||
            localMode.Text != "單機版" || cloudMode.Text != "雲端版" ||
            cloudSettings.Visible != cloudMode.Checked || deviceManagement.Visible != cloudMode.Checked ||
            companyCard.Visible != cloudMode.Checked ||
            companyCard.Enabled != expectedCompanyCardEnabled ||
            localReset.Visible != expectedLocalResetVisible ||
            cloudSettings.Enabled != cloudMode.Checked ||
            deviceManagement.Enabled != expectedDeviceManagementEnabled ||
            actionButtons.Controls.Count != 3 ||
            Math.Abs(environmentX - platformX) > 1 ||
            actionButtons.Controls.Cast<Control>().Any(control => control.Bottom > actionButtons.ClientSize.Height) ||
            appKeyLabel.Bottom > environmentLayout.ClientSize.Height - environmentLayout.Padding.Bottom ||
            appKey.Bottom > environmentLayout.ClientSize.Height - environmentLayout.Padding.Bottom ||
            moPassword.Bottom > platformLayout.ClientSize.Height - platformLayout.Padding.Bottom ||
            Math.Abs((actionButtons.Controls.Cast<Control>().Min(control => control.Left) +
                actionButtons.Controls.Cast<Control>().Max(control => control.Right)) / 2 - actionButtons.ClientSize.Width / 2) > 2)
            throw new InvalidOperationException("設定動作、雲端裝置管理、本機重設、運作模式、MO店+ 對齊或提示配置不正確");
        var logicalWidth = ClientSize.Width * 96D / DeviceDpi;
        var logicalHeight = ClientSize.Height * 96D / DeviceDpi;
        if (logicalWidth > 430 || logicalHeight > 390)
            throw new InvalidOperationException("設定視窗未維持精簡尺寸");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) toolTip.Dispose();
        base.Dispose(disposing);
    }
}
