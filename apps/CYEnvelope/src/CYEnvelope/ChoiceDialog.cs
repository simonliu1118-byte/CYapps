using System.Windows;
using System.Windows.Controls;

namespace CYEnvelope;

// "Add or overwrite?" needs labelled buttons; a native Yes/No/Cancel MessageBox cannot say which is which.
public sealed class ChoiceDialog : Window
{
    public SaveChoice Choice { get; private set; } = SaveChoice.Cancel;

    private ChoiceDialog(string title, string message, string existing, string addLabel, string overwriteLabel)
    {
        Title = title;
        Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        DesktopUi.Dialog(this);
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = existing, TextWrapping = TextWrapping.Wrap,
            Foreground = DesktopUi.Muted, Margin = new Thickness(0, 10, 0, 20) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(DesktopUi.Action("取消", (_, _) => Close(), false, cancel: true));
        actions.Children.Add(DesktopUi.Action(overwriteLabel, (_, _) => Finish(SaveChoice.Overwrite)));
        var add = DesktopUi.Action(addLabel, (_, _) => Finish(SaveChoice.Add), true);
        add.IsDefault = true; add.Margin = new Thickness(0);
        actions.Children.Add(add);
        panel.Children.Add(actions);
    }

    private void Finish(SaveChoice choice) { Choice = choice; DialogResult = true; }

    // Separate from Ask so tests can show and photograph the dialog without blocking on ShowDialog.
    public static ChoiceDialog Create(Window owner, SaveRequest request)
    {
        var kind = request.IsAddress ? "地址" : "電話";
        return new ChoiceDialog(
            $"新的{kind}",
            $"此客戶已有 {request.ExistingCount} 筆{kind}，這次的{kind}是新的：\n{request.NewValue}",
            $"要覆蓋的{kind}：{request.ExistingLabel}",
            $"新增為另一筆{kind}", $"覆蓋上面的{kind}") { Owner = owner };
    }

    public static SaveChoice Ask(Window owner, SaveRequest request)
    {
        var dialog = Create(owner, request);
        dialog.ShowDialog();
        return dialog.Choice;
    }
}
