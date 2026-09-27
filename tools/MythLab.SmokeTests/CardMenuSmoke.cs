using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using MythLab.App.ViewModels;
using MythLab.App.Views;
using MythLab.Infrastructure.Storage;
namespace MythLab.SmokeTests;

internal static class CardMenuSmoke
{
    public static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var found in Visuals<T>(VisualTreeHelper.GetChild(root, i))) yield return found;
    }
    private static Button CardButton(Window main, DeviceCardViewModel card, string id) =>
        Visuals<Button>(main).Single(b => ReferenceEquals(b.DataContext, card) && AutomationProperties.GetAutomationId(b) == id);
    private static MenuItem Item(ContextMenu menu, string id) =>
        menu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == id);
    private static async Task<ContextMenu> OpenAsync(Window main, DeviceCardViewModel card, bool keyboard = false)
    {
        var button = CardButton(main, card, "DeviceMoreActions");
        Require(button.IsVisible && button.Focusable && button.IsTabStop, "More must be visible and keyboard reachable.");
        Require(AutomationProperties.GetName(button).Contains(card.DisplayName), "More needs a device-specific accessible name.");
        if (keyboard)
        {
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button), 0, Key.Down)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            button.RaiseEvent(key);
            Require(key.Handled, "Down must open the menu from its button.");
        }
        else ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var menu = button.ContextMenu!;
        Require(menu.IsOpen && ReferenceEquals(menu.DataContext, card), "More must open the selected device's menu.");
        menu.UpdateLayout();
        return menu;
    }
    public static async Task CheckAsync(Window main, DeviceCardViewModel card, bool hasSsh)
    {
        var ssh = CardButton(main, card, "DeviceSsh");
        Require(ssh.IsVisible && ssh.Command is not null && ReferenceEquals(ssh.CommandParameter, card), "SSH must remain directly accessible and device-bound.");
        Require(!Visuals<Button>(main).Where(b => ReferenceEquals(b.DataContext, card)).Any(b =>
            b.IsVisible && new[] { "Edit SSH", "_Edit", "_Delete", "Test Wake" }.Contains(b.Content?.ToString())), "Secondary actions must not remain visible buttons.");
        var menu = await OpenAsync(main, card, true);
        try
        {
            Require(Item(menu, "EditSsh").Visibility == (hasSsh ? Visibility.Visible : Visibility.Collapsed), "Edit SSH visibility must track existing profiles.");
            Require(Item(menu, "TestWake").Visibility == (card.WakeEnabled ? Visibility.Visible : Visibility.Collapsed), "Test Wake must track WoL configuration.");
            foreach (var id in new[] { "EditSsh", "EditDevice", "TestWake", "DeleteDevice" })
            {
                var item = Item(menu, id);
                Require(item.Command is not null && ReferenceEquals(item.CommandParameter, card), id + " must keep the device-bound command.");
                Require(!string.IsNullOrEmpty(AutomationProperties.GetName(item)), id + " needs an accessible name.");
                if (item.Visibility == Visibility.Visible) Require(item.IsEnabled && item.Focusable && item.ActualHeight > 0, id + " must remain reachable and laid out.");
            }
            if (hasSsh && card.WakeEnabled)
            {
                // The Windows menu theme slides content during opening; capture only after it settles.
                await Task.Delay(300);
                var first = Item(menu, "EditSsh").TransformToAncestor(menu).Transform(new Point());
                Require(first.Y >= 0, "Edit SSH must be visible at the top of the opened menu.");
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),
                    (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(menu);
                System.IO.Directory.CreateDirectory("artifacts/ui-smoke");
                using var output = System.IO.File.Create("artifacts/ui-smoke/card-overflow.png");
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                encoder.Save(output);
            }
            menu.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(menu), 0, Key.Escape)
                { RoutedEvent = Keyboard.KeyDownEvent });
            Require(!menu.IsOpen, "Escape must close the menu.");
        }
        finally { menu.IsOpen = false; }
    }
    public static async Task InvokePrimaryAsync(Window main, DeviceCardViewModel card)
    {
        var button = CardButton(main, card, "DeviceSsh");
        var command = (IAsyncRelayCommand)button.Command;
        var previous = command.ExecutionTask;
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        await AwaitInvocationAsync(command, previous);
    }
    public static async Task InvokeMenuAsync(Window main, DeviceCardViewModel card, string id)
    {
        var menu = await OpenAsync(main, card);
        var item = Item(menu, id);
        try
        {
            Require(item.Visibility == Visibility.Visible && item.IsEnabled, id + " must be available.");
            var command = (IAsyncRelayCommand)item.Command;
            var previous = command.ExecutionTask;
            ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
            await AwaitInvocationAsync(command, previous);
        }
        finally { menu.IsOpen = false; }
    }
    private static async Task AwaitInvocationAsync(IAsyncRelayCommand command, Task? previous)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (command.ExecutionTask is null || ReferenceEquals(command.ExecutionTask, previous))
            await Task.Delay(20, timeout.Token);
        await command.ExecutionTask;
    }
    public static async Task CheckAdministrativeAsync(Window main, DeviceCardViewModel card, SqliteDeviceRepository repository)
    {
        var editOpened = false;
        var intercept = true;
        EventManager.RegisterClassHandler(typeof(DeviceEditorWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (!intercept) return;
            var editor = (DeviceEditorWindow)sender;
            editOpened = editor.DataContext is DeviceEditorViewModel vm && vm.DisplayName == card.DisplayName;
            editor.Close();
        }));
        try { await InvokeMenuAsync(main, card, "EditDevice"); }
        finally { intercept = false; }
        Require(editOpened, "Edit device must open the existing device editor.");

        var confirmed = false;
        var defaultNo = false;
        var uiThread = GetCurrentThreadId();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) =>
        {
            EnumThreadWindows(uiThread, (window, _) =>
            {
                var title = new StringBuilder(100);
                GetWindowText(window, title, title.Capacity);
                if (title.ToString() != "Delete device") return true;
                if (!IsWindowVisible(window)) return false;
                confirmed = true;
                defaultNo = (SendMessage(window, 0x400, 0, 0).ToInt64() & 0xffff) == 7; // DM_GETDEFID / IDNO
                PostMessage(window, 0x111, 7, 0); // WM_COMMAND / No: exercise the actual confirmation
                return false;
            }, 0);
            if (timeout.IsCancellationRequested && !confirmed) main.Close();
        };
        timer.Start();
        try { await InvokeMenuAsync(main, card, "DeleteDevice"); }
        finally { timer.Stop(); }
        Require(confirmed && defaultNo, $"Delete must show the existing confirmation with No selected by default (shown={confirmed}, defaultNo={defaultNo}).");
        Require((await repository.ListAsync()).Any(d => d.Id == card.Id), "Cancelling deletion must keep the device.");
    }
    public static void CheckMinimumWidth(Window main)
    {
        var panel = Visuals<ResponsiveCardsPanel>(main).Single();
        panel.Measure(new Size(340, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 340, panel.DesiredSize.Height));
        foreach (var button in Visuals<Button>(panel).Where(b => AutomationProperties.GetAutomationId(b) is "DeviceSsh" or "DeviceMoreActions"))
        {
            var bounds = button.TransformToAncestor(panel).TransformBounds(new Rect(button.RenderSize));
            Require(button.IsVisible && bounds.Left >= 0 && bounds.Right <= 340, "Primary and More controls must fit at minimum card width.");
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
