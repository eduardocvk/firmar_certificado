using System.Windows;
using System.Windows.Controls;

namespace FirmaVisible.App;

public sealed class SigningNotice : Window
{
    private readonly CheckBox remember;
    public bool DoNotShowAgain => remember.IsChecked == true;
    public SigningNotice(Window owner, string title, string message, bool confirmation)
    {
        Owner = owner; Title = title; Width = 540; SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height * .85; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new Grid { Margin = new Thickness(22) };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scroll = new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(100, SystemParameters.WorkArea.Height * .55) };
        panel.Children.Add(scroll);
        remember = new CheckBox { Content = confirmation ? "No volver a pedir esta confirmación" : "No volver a mostrar este aviso de éxito", Margin = new Thickness(0, 18, 0, 14) };
        Grid.SetRow(remember, 1); panel.Children.Add(remember);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (confirmation) actions.Children.Add(new Button { Content = "Cancelar", IsCancel = true, Margin = new Thickness(0, 0, 10, 0), MinWidth = 100 });
        var accept = new Button { Content = confirmation ? "Firmar" : "Aceptar", IsDefault = true, MinWidth = 100 };
        accept.Click += (_, _) => DialogResult = true; actions.Children.Add(accept);
        Grid.SetRow(actions, 2); panel.Children.Add(actions); Content = panel;
    }
}
