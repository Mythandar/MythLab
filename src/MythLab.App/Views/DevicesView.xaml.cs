using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace MythLab.App.Views;
public partial class DevicesView : UserControl
{
    public DevicesView() => InitializeComponent();
    private static void ShowMenu(Button button)
    {
        if (button.ContextMenu is not { } menu) return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
    private void OpenCardMenu(object sender, RoutedEventArgs e) => ShowMenu((Button)sender);
    private void CardMenuKeyDown(object sender, KeyEventArgs e)
    {
        // Enter/Space already activate the button. Down and the menu key also open it.
        if (e.Key is not (Key.Down or Key.Apps)) return;
        ShowMenu((Button)sender);
        e.Handled = true;
    }
    private void CardMenuOpened(object sender, RoutedEventArgs e) =>
        ((ContextMenu)sender).MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
}
