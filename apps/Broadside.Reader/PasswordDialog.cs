using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Broadside.Security;

namespace Broadside.Reader;

internal sealed class PasswordDialog : Window
{
    private readonly PasswordBox _password = new() { MinWidth = 280, Margin = new Thickness(0, 12, 0, 12) };

    public PasswordDialog(Window owner, bool incorrect)
    {
        Owner = owner;
        Title = "Open encrypted PDF";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, "PasswordDialog");
        AutomationProperties.SetAutomationId(_password, "DocumentPassword");
        AutomationProperties.SetName(_password, "Document password");
        var open = new Button { Content = "Open", IsDefault = true, MinWidth = 80, Margin = new Thickness(4) };
        AutomationProperties.SetAutomationId(open, "SubmitPassword");
        open.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(4) };
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = incorrect ? "Incorrect password. Try again." : "Enter the document password." },
                _password,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { open, cancel } },
            },
        };
        Loaded += (_, _) => _password.Focus();
    }

    public PdfPassword TakePassword()
    {
        var password = new PdfPassword(_password.Password);
        _password.Clear();
        return password;
    }
}
