using System.Net;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ForgeQuestDesktop;

// Hosts the game in a WebView2 control, served from a tiny local HTTP server
// (not file://) so localStorage gets a stable origin and saves persist
// reliably across runs — same reasoning as the dev-workflow throwaway
// PowerShell server used while editing index.html.
public class MainForm : Form
{
    private readonly WebView2 _webView = new();
    private HttpListener? _listener;

    public MainForm()
    {
        Text = "ForgeQuest";
        Width = 1440;
        Height = 900;
        MinimumSize = new Size(1000, 650);
        StartPosition = FormStartPosition.CenterScreen;

        // <ApplicationIcon> in the .csproj embeds app.ico as the exe's file
        // icon, but WinForms doesn't pick that up for the window/taskbar/
        // alt-tab on its own — pull it back out of our own exe at runtime.
        var exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (exeIcon is not null) Icon = exeIcon;

        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);

        Load += async (_, _) => await InitializeAsync();
        FormClosed += (_, _) => StopServer();
    }

    private async Task InitializeAsync()
    {
        var htmlPath = Path.Combine(AppContext.BaseDirectory, "www", "index.html");
        if (!File.Exists(htmlPath))
        {
            MessageBox.Show(this,
                $"Couldn't find the game file:\n{htmlPath}\n\n" +
                "Make sure a www\\index.html folder ships next to ForgeQuestDesktop.exe.",
                "ForgeQuest", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        int port;
        try
        {
            port = StartServer(Path.Combine(AppContext.BaseDirectory, "www"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Couldn't start ForgeQuest's local server:\n{ex.Message}",
                "ForgeQuest", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        // Fixed, writable per-user folder for the WebView2 profile (cookies,
        // localStorage, etc.) so saves survive even if the exe is launched
        // from a read-only location like Program Files or a fresh zip extract.
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForgeQuest", "WebView2");
        Directory.CreateDirectory(userDataFolder);

        try
        {
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "ForgeQuest needs the Microsoft Edge WebView2 Runtime, which isn't installed.\n\n" +
                "Get it from https://developer.microsoft.com/microsoft-edge/webview2/ " +
                "(the \"Evergreen Bootstrapper\" is the one you want), then relaunch ForgeQuest.",
                "ForgeQuest", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        _webView.CoreWebView2.Navigate($"http://127.0.0.1:{port}/");
    }

    // localStorage is scoped per-origin, which includes the port — so the
    // server MUST come back up on the same port every launch, or saves from
    // a previous run become invisible (different origin = empty storage,
    // even though the WebView2 profile folder itself is shared). A random
    // free port would silently reset progress on every single launch.
    private const int PreferredPort = 47811;
    private const int PortAttempts = 10;

    // Most of the game (CSS/JS/UI art/icons) is still inlined as data URIs in
    // index.html — but the background music tracks are a few MB each, way too
    // big for that, so they ship as real files under www/audio/ instead. That
    // means the server needs to actually route by request path now (index.html
    // at "/", anything else resolved as a real file under wwwRoot), not just
    // hand back one fixed set of bytes for every request like before.
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".mp3"] = "audio/mpeg",
        [".ogg"] = "audio/ogg",
        [".wav"] = "audio/wav",
        [".m4a"] = "audio/mp4",
    };

    private int StartServer(string wwwRoot)
    {
        // Trailing separator so the traversal check below (StartsWith) can't be
        // fooled by a sibling folder that merely shares wwwRoot as a prefix
        // (e.g. "...\www-evil" starting with "...\www").
        var normalizedRoot = Path.GetFullPath(wwwRoot) + Path.DirectorySeparatorChar;

        for (var port = PreferredPort; port < PreferredPort + PortAttempts; port++)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
            }
            catch (HttpListenerException) when (port < PreferredPort + PortAttempts - 1)
            {
                // Something else already owns this port — try the next one.
                // (Only matters if that something else is still running next
                // time too; otherwise we land back on PreferredPort as usual.)
                continue;
            }

            _listener = listener;
            Task.Run(() => ServeLoop(listener, normalizedRoot));
            return port;
        }

        throw new InvalidOperationException(
            $"Ports {PreferredPort}-{PreferredPort + PortAttempts - 1} are all in use.");
    }

    private static void ServeLoop(HttpListener listener, string wwwRoot)
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = listener.GetContext(); }
            catch (Exception) { return; } // listener was stopped — normal on app close

            // Handle each request on its own task instead of finishing one before
            // accepting the next. This used to serialize every request behind
            // whichever one arrived first — harmless for a single index.html
            // response, but once the page can request several audio files close
            // together (e.g. background music unlocking on the very first click,
            // the same click a player uses to land a hammer hit and trigger its
            // SFX) a big music track could sit in front of a tiny effect file in
            // the queue and delay it noticeably.
            _ = Task.Run(() => HandleRequest(ctx, wwwRoot));
        }
    }

    private static void HandleRequest(HttpListenerContext ctx, string wwwRoot)
    {
        try
        {
            var reqPath = ctx.Request.Url?.AbsolutePath ?? "/";
            if (reqPath == "/") reqPath = "/index.html";
            var relative = reqPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(wwwRoot, relative));

            if (!fullPath.StartsWith(wwwRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                ctx.Response.StatusCode = 404;
            }
            else
            {
                var ext = Path.GetExtension(fullPath);
                ctx.Response.ContentType = ContentTypes.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";
                var bytes = File.ReadAllBytes(fullPath);
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
        }
        catch { /* client went away mid-response — nothing to do */ }
        finally { ctx.Response.OutputStream.Close(); }
    }

    private void StopServer()
    {
        try { _listener?.Stop(); _listener?.Close(); }
        catch { /* already gone */ }
    }
}
