using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;
using MythLab.App.Views;
using MythLab.Core.Connections;
using MythLab.Infrastructure.Storage;
namespace MythLab.SmokeTests;

internal static class TerminalSmoke
{
    public static async Task RunAsync()
    {
        var folder = Path.GetFullPath("artifacts/terminal-spike/" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths("MythLab.Spike", folder);
        FakeTerminal? current = null;
        var connects = 0;
        var window = new TerminalWindow(paths, "Terminal spike", _ =>
        {
            current = new FakeTerminal(); connects++;
            return Task.FromResult<ITerminalSession>(current);
        }) { ShowInTaskbar = false, ShowActivated = false, Left = -30000, Top = -30000 };
        var browser = (WebView2)typeof(TerminalWindow).GetField("browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        window.Show();
        try
        {
            await UntilAsync(() => Task.FromResult(current is not null), "Terminal never connected. Check Evergreen runtime and local asset loading.");
            await UntilAsync(async () => (await browser.ExecuteScriptAsync("terminal.buffer.active.getLine(0).translateToString(true)")).Contains("RED"), "ANSI output did not render.");
            var screen = await browser.ExecuteScriptAsync("Array.from({length:terminal.rows},(_,i)=>terminal.buffer.active.getLine(i)?.translateToString(true)).join(' ')");
            if (!screen.Contains("\u03bb", StringComparison.OrdinalIgnoreCase) && !screen.Contains("λ")) throw new InvalidOperationException("Split UTF-8 output was lost.");
            await browser.ExecuteScriptAsync("terminal.input('typed', true)");
            await UntilAsync(() => Task.FromResult(current!.Input.ToString().Contains("typed")), "Terminal keyboard input did not reach transport.");
            var oldWidth = current!.Columns;
            window.Width += 220;
            await UntilAsync(() => Task.FromResult(current!.Columns != oldWidth), "Resize did not reach transport.");
            await browser.ExecuteScriptAsync("terminal.selectAll()");
            if ((await browser.ExecuteScriptAsync("terminal.getSelection()")).Length < 4) throw new InvalidOperationException("Terminal selection failed.");
            await browser.ExecuteScriptAsync("window.externalBlocked=false; fetch('https://example.com/').then(()=>window.externalBlocked=false,()=>window.externalBlocked=true)");
            await UntilAsync(async () => await browser.ExecuteScriptAsync("window.externalBlocked") == "true", "CSP did not block external network requests.");
            var imagePath = Path.GetFullPath("artifacts/ui-smoke/terminal-spike.png");
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
            await using (var stream = File.Create(imagePath))
                await browser.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, stream);
            _ = (Task)typeof(TerminalWindow).GetMethod("ReconnectAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            await UntilAsync(() => Task.FromResult(connects == 2), "Reconnect did not create a fresh session.");
            if (!Directory.Exists(paths.WebView2UserData)) throw new InvalidOperationException("WebView2 cache is not under portable Data.");
            Console.WriteLine("PASS: Evergreen/local assets, ANSI, split UTF-8, input, resize, selection, CSP, reconnect and portable browser data.");
        }
        finally { await window.CloseSessionAsync(); }
    }
    private static async Task UntilAsync(Func<Task<bool>> predicate, string error)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await predicate())
        {
            if (timeout.IsCancellationRequested) throw new InvalidOperationException(error);
            await Task.Delay(100);
        }
    }
    private sealed class FakeTerminal : ITerminalSession
    {
        private readonly Channel<byte[]> output = Channel.CreateUnbounded<byte[]>();
        public StringBuilder Input { get; } = new();
        public int Columns { get; private set; }
        public FakeTerminal()
        {
            var bytes = Encoding.UTF8.GetBytes("\x1b[2J\x1b[H\x1b[31mRED\x1b[0m λ 世界\r\nInteractive terminal fixture\r\n");
            for (var i = 0; i < bytes.Length; i += 2) output.Writer.TryWrite(bytes.Skip(i).Take(2).ToArray());
        }
        public async Task<int> ReadAsync(byte[] buffer, CancellationToken token)
        {
            var data = await output.Reader.ReadAsync(token);
            data.CopyTo(buffer, 0); return data.Length;
        }
        public Task WriteAsync(byte[] data, CancellationToken token) { Input.Append(Encoding.UTF8.GetString(data)); output.Writer.TryWrite(data); return Task.CompletedTask; }
        public Task ResizeAsync(int columns, int rows, CancellationToken token) { Columns = columns; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { output.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }
}
