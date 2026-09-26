from pathlib import Path

ROOT = Path("apps/CYERPAutoInput")


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one match in {path}: found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


# Build number / visible status.
(ROOT / "BUILD").write_text("24\n", encoding="utf-8")
replace_once(
    ROOT / "AppLogger.cs",
    "CYERPAutoInput V0.1.0 Build 23 starting",
    "CYERPAutoInput V0.1.0 Build 24 starting",
)
replace_once(
    ROOT / "Build10UiPatch.cs",
    "V0.1.0 Build 23 · Esc：緊急停止 · 不自動儲存 ERP",
    "V0.1.0 Build 24 · Esc：緊急停止 · 不自動儲存 ERP",
)

# Lookup fields in COPI08 can use a reused DevExpress/Delphi inner editor.  The
# inner editor may retain the previous control's display text even when the
# actual TDBEdit data-bound field is blank.  For Lookup + TDBEdit, make the
# real target control authoritative while preserving the occupied-field guard.
erp_old = '''        if (field.Key == "order_type")
        {
            InputSender.EndBackspace(4);
        }
        else
        {
            var before = NativeMethods.WindowText(focus);
            if (before == value)
            {
                InputSender.Press(NativeMethods.VK_TAB);
                await Delay(field.Kind == FieldKind.Lookup ? 420 : 180, cancellationToken);
                return;
            }
            if (!string.IsNullOrWhiteSpace(before))
                throw new InvalidOperationException($"ERP 欄位「{field.Label}」目前已有內容；為避免覆蓋既有值已停止。");
        }
'''
erp_new = '''        if (field.Key == "order_type")
        {
            InputSender.EndBackspace(4);
        }
        else
        {
            var editorBefore = NativeMethods.WindowText(focus).Trim();
            var targetBefore = NativeMethods.WindowText(target.Handle).Trim();
            var targetIsLookupDbEdit = field.Kind == FieldKind.Lookup &&
                                       target.ClassName.Equals("TDBEdit", StringComparison.OrdinalIgnoreCase);
            var before = targetIsLookupDbEdit ? targetBefore : editorBefore;

            if (before == value)
            {
                InputSender.Press(NativeMethods.VK_TAB);
                await Delay(field.Kind == FieldKind.Lookup ? 420 : 180, cancellationToken);
                return;
            }

            if (targetIsLookupDbEdit && targetBefore.Length == 0 && editorBefore.Length > 0)
                _log.Info("field", $"lookup target is blank; ignored retained inner-editor text key={field.Key} editor_class={NativeMethods.ClassName(focus)} editor_len={editorBefore.Length}");

            if (!string.IsNullOrWhiteSpace(before))
                throw new InvalidOperationException($"ERP 欄位「{field.Label}」目前已有內容；為避免覆蓋既有值已停止。");
        }
'''
replace_once(ROOT / "ErpAutomationService.cs", erp_old, erp_new)

# Keep the canonical icon in the PE for Explorer/shortcuts and also embed it as
# a managed resource so WinForms can assign the exact icon independent of
# ExtractAssociatedIcon behavior in a single-file publish.
csproj_old = '''  <ItemGroup>
    <PackageReference Include="RapidOCRSharpOnnx" Version="1.3.1" />
    <PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.29.0" />
    <PackageReference Include="OpenCvSharp4.runtime.win" Version="4.13.0.20260627" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="runtime\\ocr\\*.onnx">
'''
csproj_new = '''  <ItemGroup>
    <PackageReference Include="RapidOCRSharpOnnx" Version="1.3.1" />
    <PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.29.0" />
    <PackageReference Include="OpenCvSharp4.runtime.win" Version="4.13.0.20260627" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="assets\\Auto.ico" LogicalName="CYERPAutoInput.Auto.ico" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="runtime\\ocr\\*.onnx">
'''
replace_once(ROOT / "CYERPAutoInput.csproj", csproj_old, csproj_new)

program_old_using = 'using System.Runtime.Versioning;\n'
program_new_using = 'using System.Reflection;\nusing System.Runtime.Versioning;\n'
replace_once(ROOT / "Program.cs", program_old_using, program_new_using)

program_old_appid = '''        if (args.Any(a => a.Equals("--vision-self-test", StringComparison.OrdinalIgnoreCase)))
            return VisionSelfTest.RunAsync(logger).GetAwaiter().GetResult();

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
'''
program_new_appid = '''        if (args.Any(a => a.Equals("--vision-self-test", StringComparison.OrdinalIgnoreCase)))
            return VisionSelfTest.RunAsync(logger).GetAwaiter().GetResult();

        var appIdHr = NativeMethods.SetCurrentProcessExplicitAppUserModelID("Chihyuan.CYERPAutoInput");
        if (appIdHr != 0)
            logger.Warn("app", $"SetCurrentProcessExplicitAppUserModelID failed HRESULT=0x{appIdHr:X8}");

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
'''
replace_once(ROOT / "Program.cs", program_old_appid, program_new_appid)

program_old_icon = '''        var form = new MainForm(logger);
        try
        {
            form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (Exception ex)
        {
            logger.Warn("app", $"window icon load skipped: {ex.Message}");
        }
'''
program_new_icon = '''        var form = new MainForm(logger);
        try
        {
            using var iconStream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("CYERPAutoInput.Auto.ico");
            if (iconStream is not null)
            {
                using var embeddedIcon = new Icon(iconStream);
                form.Icon = (Icon)embeddedIcon.Clone();
                logger.Info("app", "window/taskbar icon loaded from embedded canonical Auto.ico");
            }
            else
            {
                form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                logger.Warn("app", "embedded canonical icon missing; used executable associated icon fallback");
            }
        }
        catch (Exception ex)
        {
            logger.Warn("app", $"window icon load skipped: {ex.Message}");
        }
'''
replace_once(ROOT / "Program.cs", program_old_icon, program_new_icon)

native_old = '''    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    internal static string WindowText(nint hwnd)
'''
native_new = '''    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    internal static string WindowText(nint hwnd)
'''
replace_once(ROOT / "NativeMethods.cs", native_old, native_new)

readme_old = '''- Auto.ico SHA-256：`b35e87231fcd3238a4e7d73a687225d282bd1d60fe9de937f23de59393cc8e11`

## 安全設計
'''
readme_new = '''- Auto.ico SHA-256：`b35e87231fcd3238a4e7d73a687225d282bd1d60fe9de937f23de59393cc8e11`
- Windows 執行時同時以 managed embedded resource 指派同一份 `Auto.ico`，並設定固定 AppUserModelID，避免 single-file 執行時工作列退回通用圖示。

## 安全設計
'''
replace_once(ROOT / "README.md", readme_old, readme_new)

print("Build 24 patch applied")
