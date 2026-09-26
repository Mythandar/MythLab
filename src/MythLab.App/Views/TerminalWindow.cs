using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MythLab.Core.Connections;
using MythLab.Infrastructure.Storage;
namespace MythLab.App.Views;

/// <summary>Only terminal view glue lives here. Transport is an ITerminalSession; secrets never enter WebView2.</summary>
public sealed class TerminalWindow : Window
{
    private const string Origin = "https://terminal.mythlab.invalid/";
    private readonly WebView2 browser = new();
    private readonly TextBlock status = new() { Text = "Opening terminal…", Margin = new(10), TextWrapping = TextWrapping.Wrap };
    private readonly AppPaths paths;
    private readonly Func<CancellationToken, Task<ITerminalSession>> connect;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? running, initialization, closeTask;
    private CancellationTokenSource? cancellation;
    private ITerminalSession? session;
    private Channel<Input>? input;
    private TaskCompletionSource? acknowledgement;
    private int outputId, columns = 100, rows = 30;
    private bool drained, closing, restarting;
    private sealed record Input(byte[]? Bytes = null, int Columns = 0, int Rows = 0);
    public TerminalWindow(AppPaths paths, string title, Func<CancellationToken, Task<ITerminalSession>> connect)
    {
        this.paths = paths; this.connect = connect;
        Title = title; Width = 1050; Height = 720; MinWidth = 600; MinHeight = 420;
        var root = new DockPanel();
        var toolbar = new WrapPanel { Margin = new(8) };
        void Button(string label, RoutedEventHandler handler)
        {
            var button = new Button { Content = label }; button.Click += handler; toolbar.Children.Add(button);
        }
        Button("Reconnect", async (_, _) => await ReconnectAsync());
        Button("Disconnect", async (_, _) => await StopAsync());
        Button("Copy selection", async (_, _) => { if (browser.CoreWebView2 is not null) await browser.ExecuteScriptAsync("requestCopy()"); });
        Button("Paste", (_, _) => Paste());
        Button("WebView2 runtime", (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true }));
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        root.Children.Add(browser); Content = root;
        Loaded += async (_, _) => await ReconnectAsync();
        Closing += OnClosing;
    }
    private async Task InitializeBrowserAsync()
    {
        // No fixed runtime; inventory startup never calls this method.
        try { _ = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException) { throw new InvalidOperationException("Evergreen WebView2 Runtime is missing. Use the WebView2 runtime link to install it; other MythLab features remain available."); }
        Directory.CreateDirectory(paths.WebView2UserData);
        var probe = Path.Combine(paths.WebView2UserData, ".write-" + Guid.NewGuid().ToString("N"));
        using (var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) file.WriteByte(0);
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: paths.WebView2UserData);
        await browser.EnsureCoreWebView2Async(environment);
        var core = browser.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.NavigationStarting += (_, e) => e.Cancel = e.Uri != Origin + "index.html";
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            var uri = new Uri(e.Request.Uri);
            var name = uri.AbsoluteUri.StartsWith(Origin, StringComparison.Ordinal) ? uri.AbsolutePath.TrimStart('/') : "";
            string[] allowed = ["index.html", "terminal.css", "terminal.js", "xterm.js", "xterm.css", "addon-fit.js"];
            var stream = allowed.Contains(name) ? Assembly.GetExecutingAssembly().GetManifestResourceStream("MythLab.App.TerminalAssets." + name) : null;
            var contentType = name.EndsWith(".js", StringComparison.Ordinal) ? "text/javascript" : name.EndsWith(".css", StringComparison.Ordinal) ? "text/css" : "text/html";
            e.Response = environment.CreateWebResourceResponse(stream ?? new MemoryStream(), stream is null ? 403 : 200,
                stream is null ? "Forbidden" : "OK", $"Content-Type: {contentType}; charset=utf-8\r\nCache-Control: no-store");
        };
        core.WebMessageReceived += OnMessage;
        core.ProcessFailed += (_, _) => { status.Text = "The terminal renderer stopped. Close and reopen this terminal."; cancellation?.Cancel(); };
        core.Navigate(Origin + "index.html");
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (e.Source != Origin + "index.html" || e.WebMessageAsJson.Length > 65536) return;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var message = document.RootElement;
            switch (message.GetProperty("type").GetString())
            {
                case "ready":
                    SetSize(message); ready.TrySetResult(); break;
                case "resize":
                    SetSize(message);
                    Queue(new(null, columns, rows)); break;
                case "input":
                    var text = message.GetProperty("data").GetString() ?? "";
                    if (text.Length <= 16384) Queue(new(Encoding.UTF8.GetBytes(text)));
                    break;
                case "ack":
                    if (message.GetProperty("id").GetInt32() == outputId) acknowledgement?.TrySetResult();
                    break;
                case "copy":
                    var selection = message.GetProperty("data").GetString() ?? "";
                    if (selection.Length is > 0 and <= 32768) Clipboard.SetText(selection);
                    break;
                case "paste": Paste(); break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or System.Runtime.InteropServices.COMException)
        { /* Invalid browser messages cannot invoke native actions or leak data. */ }
    }
    private void SetSize(JsonElement message)
    {
        var cols = message.GetProperty("cols").GetInt32();
        var lines = message.GetProperty("rows").GetInt32();
        columns = Math.Clamp(cols, 2, 500); rows = Math.Clamp(lines, 1, 300);
    }
    private void Queue(Input value)
    {
        if (session is null || cancellation?.IsCancellationRequested != false) return;
        if (input?.Writer.TryWrite(value) == false) { status.Text = "Terminal input queue filled. Disconnecting."; cancellation.Cancel(); }
    }
    private void Paste()
    {
        if (session is null || !Clipboard.ContainsText()) return;
        var text = Clipboard.GetText();
        if (text.Length > 32768) { status.Text = "Paste is limited to 32,768 characters."; return; }
        if ((text.Contains('\n') || text.Contains('\r')) &&
            MessageBox.Show(this, "Paste multiple lines? Newlines may execute commands on the remote computer.", "Paste",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Queue(new(Encoding.UTF8.GetBytes(text)));
    }
    private async Task ReconnectAsync()
    {
        if (closing || restarting) return;
        restarting = true;
        try
        {
            await StopAsync();
            if (closing) return;
            cancellation = new();
            input = Channel.CreateBounded<Input>(64);
            running = RunAsync(cancellation.Token);
        }
        finally { restarting = false; }
        await running;
    }
    private async Task RunAsync(CancellationToken token)
    {
        Task? writer = null;
        try
        {
            status.Text = "Preparing terminal…";
            initialization ??= InitializeBrowserAsync();
            await initialization.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"reset\"}");
            status.Text = "Connecting to SSH…";
            session = await connect(token);
            status.Text = "Connected • Ctrl+Shift+C copies; Ctrl+Shift+V pastes.";
            await session.ResizeAsync(columns, rows, token);
            writer = WriteLoopAsync(session, token);
            var buffer = new byte[8192];
            while (true)
            {
                var count = await session.ReadAsync(buffer, token);
                if (count == 0) { status.Text = "Remote session closed. Reconnect to open a new session."; break; }
                acknowledgement = new(TaskCreationOptions.RunContinuationsAsynchronously);
                browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "output", id = ++outputId,
                    data = Convert.ToBase64String(buffer, 0, count) }));
                await acknowledgement.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            }
        }
        catch (OperationCanceledException) { status.Text = "Disconnected."; }
        catch (Exception ex)
        {
            status.Text = ex is InvalidOperationException or TimeoutException or Infrastructure.Ssh.HostIdentityException or Core.Credentials.CredentialStoreException
                ? ex.Message : $"Terminal could not continue ({ex.GetType().Name}). Check credentials, key path, network and Data-folder permissions.";
        }
        finally
        {
            cancellation?.Cancel();
            if (session is not null) { try { await session.DisposeAsync(); } catch { status.Text = "Session closed with a transport cleanup error."; } finally { session = null; } }
            if (writer is not null) { try { await writer; } catch (Exception) { /* No raw transport detail or input is logged. */ } }
        }
    }
    private async Task WriteLoopAsync(ITerminalSession target, CancellationToken token)
    {
        try
        {
            await foreach (var message in input!.Reader.ReadAllAsync(token))
            {
                if (message.Bytes is { } bytes) await target.WriteAsync(bytes, token);
                else await target.ResizeAsync(message.Columns, message.Rows, token);
            }
        }
        catch { cancellation?.Cancel(); throw; }
    }
    public async Task StopAsync()
    {
        var stopping = cancellation;
        var task = running;
        stopping?.Cancel();
        if (task is not null) await task;
        if (ReferenceEquals(cancellation, stopping)) { stopping?.Dispose(); cancellation = null; }
    }
    public Task CloseSessionAsync() => closeTask ??= CloseCoreAsync();
    private async Task CloseCoreAsync()
    {
        if (closing) return;
        closing = true;
        await StopAsync();
        if (initialization is not null) { try { await initialization; } catch { } }
        browser.Dispose(); drained = true; Close();
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (drained) return;
        e.Cancel = true;
        await CloseSessionAsync();
    }
}
