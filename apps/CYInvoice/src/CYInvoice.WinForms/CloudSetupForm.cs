using CYInvoice.Core.Cloud;
using CYInvoice.Core.Storage;

namespace CYInvoice.WinForms;

internal sealed class CloudSetupForm : Form
{
    private const string SupportedApiVersion = "1";
    private const string SupportedSchemaVersion = "2";

    private readonly LocalRepository repository;
    private readonly HttpClient httpClient = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly RadioButton localMode = new() { Text = "單機模式", AutoSize = true };
    private readonly RadioButton cloudMode = new() { Text = "雲端模式", AutoSize = true };
    private readonly TextBox baseUrl = UiControls.TextBox(240);
    private readonly Label status = UiControls.Label(string.Empty);
    private readonly Label modeHint = UiControls.Label(string.Empty);
    private readonly Button check = UiControls.StandardButton("測試連線");
    private readonly Button save = UiControls.StandardButton("儲存");
    private readonly Button cancel = UiControls.StandardButton("取消");
    private GroupBox cloudGroup = null!;
    private bool busy;
    private bool stateError;
    private bool resourcesDisposed;
    private string confirmedBaseUrl = string.Empty;

    public CloudSetupForm(LocalRepository repository)
    {
        this.repository = repository;
        Text = "資料模式設定";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 322);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        Font = new Font("Microsoft JhengHei UI", 10F);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();

        baseUrl.PlaceholderText = "https://cloud.example.com/";
        modeHint.ForeColor = Color.DimGray;

        BuildLayout();
        LoadValues();
        localMode.CheckedChanged += ModeChanged;
        cloudMode.CheckedChanged += ModeChanged;
        baseUrl.TextChanged += (_, _) =>
        {
            confirmedBaseUrl = string.Empty;
            if (cloudMode.Checked) UpdateState();
        };
        UpdateModeFields();
        UpdateState();
    }

    private void BuildLayout()
    {
        var root = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16),
            Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var modeGroup = new GroupBox { Text = "運作模式", Dock = DockStyle.Fill };
        var modeLayout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10, 6, 10, 6),
            Margin = Padding.Empty,
        };
        modeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        modeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var modeChoices = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        localMode.Margin = new Padding(0, 4, 28, 0);
        cloudMode.Margin = new Padding(0, 4, 0, 0);
        modeChoices.Controls.Add(localMode);
        modeChoices.Controls.Add(cloudMode);
        modeLayout.Controls.Add(modeChoices, 0, 0);
        modeLayout.Controls.Add(modeHint, 0, 1);
        modeGroup.Controls.Add(modeLayout);

        cloudGroup = new GroupBox { Text = "CYInvoice Cloud API", Dock = DockStyle.Fill };
        var cloud = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(10, 8, 10, 8),
            Margin = Padding.Empty,
        };
        cloud.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        cloud.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cloud.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        cloud.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        cloud.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cloud.Controls.Add(UiControls.Label("API 網址"), 0, 0);
        cloud.Controls.Add(baseUrl, 1, 0);
        cloud.SetColumnSpan(baseUrl, 2);
        cloud.Controls.Add(UiControls.Label("狀態"), 0, 1);
        cloud.Controls.Add(status, 1, 1);
        cloud.Controls.Add(check, 2, 1);
        check.Click += async (_, _) => await CheckHealthAsync();
        cloudGroup.Controls.Add(cloud);

        cancel.DialogResult = DialogResult.Cancel;
        save.Width = 112;
        cancel.Width = 112;
        save.Click += async (_, _) => await SaveAsync();
        var actions = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0),
            Margin = Padding.Empty,
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(save);

        root.Controls.Add(modeGroup, 0, 0);
        root.Controls.Add(cloudGroup, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
        AcceptButton = null;
        CancelButton = cancel;
    }

    private void LoadValues()
    {
        var settings = repository.Settings.LoadOrCreate();
        localMode.Checked = settings.CloudMode == CloudModes.LocalOnly;
        cloudMode.Checked = settings.CloudMode == CloudModes.CloudPreferred;
        baseUrl.Text = settings.CloudBaseUrl;
    }

    private void ModeChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is RadioButton radio && !radio.Checked) return;
        confirmedBaseUrl = string.Empty;
        UpdateModeFields();
        UpdateState();
    }

    private void UpdateModeFields()
    {
        var enabled = cloudMode.Checked && !busy;
        cloudGroup.Enabled = cloudMode.Checked;
        baseUrl.Enabled = enabled;
        check.Enabled = enabled;
        modeHint.Text = cloudMode.Checked
            ? "雲端模式會連線到你自行設定、符合 CYInvoice Cloud API 的 HTTPS 服務；CYInvoice 不直接連資料庫。"
            : "單機模式只使用本機資料與既有 AMEGO 流程，不會呼叫任何 CYInvoice Cloud API。";
    }

    private void UpdateState(string? message = null, bool error = false)
    {
        stateError = error;
        var settings = repository.Settings.LoadOrCreate();
        var registeredHere = HasCloudIdentity(settings) && SameEndpoint(settings.CloudBaseUrl, baseUrl.Text);

        if (!cloudMode.Checked)
        {
            status.Text = "單機模式";
            status.ForeColor = SystemColors.ControlText;
            return;
        }

        status.Text = message ?? (baseUrl.Text.Trim().Length == 0
            ? "請輸入相容的 HTTPS API 網址。"
            : registeredHere
                ? "雲端模式｜裝置已註冊"
                : "雲端模式｜尚未完成裝置驗證");
        status.ForeColor = error
            ? Color.FromArgb(180, 0, 0)
            : registeredHere ? Color.FromArgb(0, 120, 60) : SystemColors.ControlText;
    }

    private async Task CheckHealthAsync()
    {
        await RunBusyAsync(async () =>
        {
            var normalized = await CheckHealthCoreAsync();
            confirmedBaseUrl = normalized;
            UpdateState($"連線正常｜API {SupportedApiVersion}｜Schema {SupportedSchemaVersion}");
        });
    }

    private async Task<string> CheckHealthCoreAsync()
    {
        var normalized = NormalizeBaseUrl(baseUrl.Text);
        var client = new CloudClient(httpClient, new Uri(normalized, UriKind.Absolute));
        var health = await client.CheckHealthAsync(lifetime.Token);
        if (!health.Reachable)
            throw new InvalidOperationException("無法連線到 CYInvoice Cloud API，請確認網址與網路連線。");
        if (!health.DatabaseAvailable)
            throw new InvalidOperationException($"Cloud API 可連線，但後端儲存服務尚未就緒（{health.ErrorCode}）。");
        if (health.ApiVersion != SupportedApiVersion)
            throw new InvalidOperationException($"Cloud API 版本不相容，目前為 {health.ApiVersion}，需要 {SupportedApiVersion}。");
        if (health.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidOperationException($"Cloud schema 版本不相容，目前為 {health.SchemaVersion}，需要 {SupportedSchemaVersion}。");
        return normalized;
    }

    private async Task SaveAsync()
    {
        await RunBusyAsync(async () =>
        {
            var settings = repository.Settings.LoadOrCreate();
            if (localMode.Checked)
            {
                settings.CloudMode = CloudModes.LocalOnly;
                repository.Settings.Save(settings);
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            if (!cloudMode.Checked)
                throw new InvalidOperationException("請選擇單機模式或雲端模式。");

            var normalized = NormalizeBaseUrl(baseUrl.Text);
            if (!SameEndpoint(confirmedBaseUrl, normalized))
            {
                normalized = await CheckHealthCoreAsync();
                confirmedBaseUrl = normalized;
            }

            if (settings.CloudBaseUrl.Length != 0 && !SameEndpoint(settings.CloudBaseUrl, normalized) && HasCloudIdentity(settings))
                repository.Settings.ClearCloudIdentity(settings);

            settings.CloudBaseUrl = normalized;
            settings.CloudMode = CloudModes.CloudPreferred;
            repository.Settings.Save(settings);
            DialogResult = DialogResult.OK;
            Close();
        });
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        UseWaitCursor = true;
        save.Enabled = false;
        localMode.Enabled = false;
        cloudMode.Enabled = false;
        UpdateModeFields();
        if (cloudMode.Checked) UpdateState("處理中…");
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (CloudApiException error)
        {
            if (!IsDisposed)
            {
                UpdateState($"Cloud API 回應失敗：{error.Code}", error: true);
                MessageBox.Show(this, $"Cloud API 錯誤：{error.Code}\n{error.Message}", "Cloud 操作失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                UpdateState(error.Message, error: true);
                MessageBox.Show(this, error.Message, "無法儲存資料模式", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            busy = false;
            if (!IsDisposed && !Disposing)
            {
                UseWaitCursor = false;
                save.Enabled = true;
                localMode.Enabled = true;
                cloudMode.Enabled = true;
                UpdateModeFields();
                if (cloudMode.Checked) UpdateState(status.Text, stateError);
            }
        }
    }

    private static string NormalizeBaseUrl(string value)
    {
        value = value.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Cloud API 網址必須是有效的 HTTPS 網址，且不可包含帳密、Query 或 Fragment。");

        var normalized = uri.AbsoluteUri;
        if (!normalized.EndsWith("/", StringComparison.Ordinal)) normalized += "/";
        return normalized;
    }

    private static bool HasCloudIdentity(Settings settings) =>
        settings.CloudWorkspaceId.Length != 0
        && settings.CloudDeviceId.Length != 0
        && settings.CloudDeviceTokenEncrypted.Length != 0;

    private static bool SameEndpoint(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return false;
        try
        {
            return string.Equals(NormalizeBaseUrl(left), NormalizeBaseUrl(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal void VerifySmokeLayout()
    {
        var settings = repository.Settings.LoadOrCreate();
        if (Text != "資料模式設定" || ShowIcon || AcceptButton is not null)
            throw new InvalidOperationException("資料模式設定視窗基本屬性不正確");
        if (save.Text != "儲存" || cancel.DialogResult != DialogResult.Cancel || check.Text != "測試連線")
            throw new InvalidOperationException("資料模式設定動作按鈕不正確");
        if (settings.CloudMode == CloudModes.LocalOnly && (!localMode.Checked || cloudMode.Checked || cloudGroup.Enabled))
            throw new InvalidOperationException("單機模式預設狀態不正確");
        if (settings.CloudBaseUrl.Length == 0 && baseUrl.Text.Length != 0)
            throw new InvalidOperationException("Public client 不得內建任何 Cloud API endpoint");
        if (baseUrl.PlaceholderText.Contains("workers.dev", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cloud API 輸入不得綁定特定雲端供應商");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            httpClient.Dispose();
        }
        base.Dispose(disposing);
    }
}
