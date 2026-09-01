using System.Net;
using System.Text;
using System.Text.Json;
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
        // Explicitly disposing the WebView2 control (not just relying on
        // WinForms' automatic Controls-disposal-on-Form-Dispose) matters
        // here: without an explicit, awaited-in-spirit shutdown handshake,
        // its underlying browser/renderer/GPU child processes have been
        // observed surviving a closed window as orphans — confirmed
        // tonight, not theoretical: an orphaned renderer kept running the
        // page's own once-a-second autosave in the background, racing a
        // later launch's writes to the same save file. A genuinely forceful
        // kill (taskkill /F, Process.Kill()) bypasses this entirely no
        // matter what — nothing here changes that — but a NORMAL window
        // close should tear the whole tree down cleanly.
        FormClosed += (_, _) => { StopServer(); _webView.Dispose(); };
    }

    private async Task InitializeAsync()
    {
        LogApiEvent($"STARTUP exe=[{Application.ExecutablePath}] baseDir=[{AppContext.BaseDirectory}] " +
            $"localAppData=[{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}] " +
            $"resolvedSaveFilePath=[{Path.GetFullPath(SaveFilePath)}] pid={Environment.ProcessId} " +
            $"is64=[{Environment.Is64BitProcess}] user=[{Environment.UserName}]");
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
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
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
            if (reqPath == "/api/save") { HandleSaveApi(ctx); return; }
            if (reqPath == "/api/friend-identity") { HandleFriendIdentityApi(ctx); return; }
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

    // Real save persistence, bypassing WebView2/Chromium's own localStorage
    // (LevelDB-backed) entirely — see SESSION_NOTES.md for the whole story:
    // a cold-storage read race at boot (now also mitigated client-side by a
    // retry) got permanently baked in by the once-a-second autosave, AND a
    // separate incident where forcefully killing stray WebView2 processes
    // appears to have caused that storage backend to roll back to an older
    // checkpoint on its own — real, observed data loss from TWO different
    // angles, both upstream of anything this app's own JS logic controls.
    // A plain JSON file, written by THIS process, sidesteps that whole
    // storage engine: GET returns whatever's on disk, POST replaces it, and
    // the replace is atomic (write to a temp file, then File.Replace/Move)
    // so a crash or forceful kill mid-write can only ever leave the
    // PREVIOUS save intact, never a half-written/corrupt one and never a
    // silent rollback to some unrelated older state. index.html feature-
    // detects this endpoint (a 200 here vs. a 404 on a plain static server)
    // and falls back to localStorage on platforms without it — Android/
    // Capacitor, or this file served by a plain dev static-file server.
    private static readonly string SaveFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ForgeQuest", "save.json");

    // TEMPORARY diagnostic (see SESSION_NOTES.md, the save-appears-reset
    // investigation) — logs every single GET/POST/DELETE this endpoint
    // ever handles, server-side, independent of any client-JS complexity
    // or the various competing test processes that turned out to be
    // muddying earlier diagnosis. Remove once root-caused for real.
    private static readonly string ApiLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ForgeQuest", "api_save_log.txt");
    private static void LogApiEvent(string line)
    {
        try { File.AppendAllText(ApiLogPath, $"{DateTime.Now:HH:mm:ss.fff} | {line}\n"); }
        catch { /* best-effort logging only */ }
    }
    private static string SummarizeSaveJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int gold = root.TryGetProperty("gold", out var g) ? g.GetInt32() : -1;
            int catalogCount = 0;
            if (root.TryGetProperty("catalog", out var cat))
                foreach (var mat in cat.EnumerateObject())
                    catalogCount += mat.Value.GetArrayLength();
            int friendsCount = root.TryGetProperty("friends", out var fr) ? fr.GetArrayLength() : -1;
            return $"gold={gold} catalogCount={catalogCount} friendsCount={friendsCount} len={json.Length}";
        }
        catch (Exception ex) { return $"UNPARSEABLE ({ex.Message}), len={json.Length}, first120={json.Substring(0, Math.Min(120, json.Length))}"; }
    }

    private static void HandleSaveApi(HttpListenerContext ctx)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SaveFilePath)!);
            ctx.Response.Headers.Add("Cache-Control", "no-store");
            switch (ctx.Request.HttpMethod)
            {
                case "GET":
                {
                    bool exists = File.Exists(SaveFilePath);
                    string data = exists ? File.ReadAllText(SaveFilePath) : "";
                    // Deep path/identity diagnostic — a running app's own read
                    // returning DIFFERENT content than an external tool sees for
                    // the "same" nominal path, confirmed happening tonight, means
                    // something about path resolution or file identity itself
                    // differs for this process specifically. Full canonicalized
                    // path (bracketed to catch invisible whitespace) and the
                    // actual on-disk write time as THIS PROCESS sees it settle
                    // whether it's really the same file or not.
                    string fullPath = Path.GetFullPath(SaveFilePath);
                    string writeTime = exists ? File.GetLastWriteTimeUtc(SaveFilePath).ToString("O") : "n/a";
                    LogApiEvent(exists
                        ? $"GET  exists=true  path=[{fullPath}] writeTimeUtc={writeTime} {SummarizeSaveJson(data)}"
                        : $"GET  exists=false path=[{fullPath}]");
                    string json = exists
                        ? JsonSerializer.Serialize(new { exists = true, data })
                        : JsonSerializer.Serialize(new { exists = false, data = (string?)null });
                    WriteJson(ctx, 200, json);
                    break;
                }
                case "POST":
                {
                    var contentLengthHeader = ctx.Request.Headers["Content-Length"];
                    using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
                    var bodyText = reader.ReadToEnd();
                    using var doc = JsonDocument.Parse(bodyText);
                    var data = doc.RootElement.GetProperty("data").GetString() ?? "";
                    LogApiEvent($"POST contentLengthHeader={contentLengthHeader} actualBodyLen={bodyText.Length} {SummarizeSaveJson(data)}");
                    // Atomic replace — see this method's own comment above for why
                    // this specific sequence (write to .tmp, then Replace/Move)
                    // is the whole point, not incidental. Per-call GUID suffix,
                    // not a fixed ".tmp" name — confirmed happening tonight: two
                    // overlapping POSTs (a retry racing the original, or the
                    // once-a-second autosave overlapping an explicit "Save now")
                    // both tried to open the SAME fixed temp filename at once,
                    // throwing IOException on whichever lost the race. A unique
                    // name per call means concurrent writers never collide, and
                    // whichever finishes its own Replace() last simply wins —
                    // still safe, still atomic, just no longer needlessly prone
                    // to failing outright under completely normal overlap.
                    var tmpPath = SaveFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(tmpPath, data);
                    if (File.Exists(SaveFilePath)) File.Replace(tmpPath, SaveFilePath, null);
                    else File.Move(tmpPath, SaveFilePath);
                    // Re-read immediately after our own write, logged, so this
                    // log is a direct witness to what's ACTUALLY on disk right
                    // after this specific write — not an assumption based on
                    // what we just asked for. If a later GET in this same log
                    // ever shows something else, that gap is the whole mystery.
                    var verifyData = File.ReadAllText(SaveFilePath);
                    LogApiEvent($"  -> post-write verify: {SummarizeSaveJson(verifyData)} writeTimeUtc={File.GetLastWriteTimeUtc(SaveFilePath):O}");
                    WriteJson(ctx, 200, "{\"ok\":true}");
                    break;
                }
                case "DELETE":
                {
                    LogApiEvent("DELETE");
                    if (File.Exists(SaveFilePath)) File.Delete(SaveFilePath);
                    WriteJson(ctx, 200, "{\"ok\":true}");
                    break;
                }
                default:
                    ctx.Response.StatusCode = 405;
                    break;
            }
        }
        catch (Exception ex)
        {
            LogApiEvent($"EXCEPTION method={ctx.Request.HttpMethod}: {ex}");
            WriteJson(ctx, 500, JsonSerializer.Serialize(new { error = ex.Message }));
        }
        // No finally-close here — HandleRequest's own finally closes the stream
        // once, for every route including this one; closing it a second time
        // here would be redundant (and, depending on the exact HttpListener
        // implementation, not guaranteed harmless).
    }

    // Friend identity persistence — playerId/friendCode/authSecret/username.
    // Same atomic-write mechanism and same reasoning as SaveFilePath/
    // HandleSaveApi above (see that method's comment for the full story of
    // why plain WebView2 localStorage can't be trusted). This data was
    // ORIGINALLY left in localStorage even after the main save moved to a
    // file, on the assumption it changes rarely enough not to matter —
    // confirmed wrong (see SESSION_NOTES.md): the same localStorage
    // unreliability that lost real save progress also silently mints a
    // BRAND NEW backend identity here every time it strikes, which is
    // worse than a stale save — existing friends can no longer find you,
    // and the one-time username prompt reappears every launch.
    private static readonly string FriendIdentityFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ForgeQuest", "friend_identity.json");

    private static void HandleFriendIdentityApi(HttpListenerContext ctx)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FriendIdentityFilePath)!);
            ctx.Response.Headers.Add("Cache-Control", "no-store");
            switch (ctx.Request.HttpMethod)
            {
                case "GET":
                {
                    bool exists = File.Exists(FriendIdentityFilePath);
                    string data = exists ? File.ReadAllText(FriendIdentityFilePath) : "";
                    LogApiEvent(exists
                        ? $"FRIEND-ID GET  exists=true  path=[{Path.GetFullPath(FriendIdentityFilePath)}] len={data.Length}"
                        : $"FRIEND-ID GET  exists=false path=[{Path.GetFullPath(FriendIdentityFilePath)}]");
                    string json = exists
                        ? JsonSerializer.Serialize(new { exists = true, data })
                        : JsonSerializer.Serialize(new { exists = false, data = (string?)null });
                    WriteJson(ctx, 200, json);
                    break;
                }
                case "POST":
                {
                    using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
                    var bodyText = reader.ReadToEnd();
                    using var doc = JsonDocument.Parse(bodyText);
                    var data = doc.RootElement.GetProperty("data").GetString() ?? "";
                    LogApiEvent($"FRIEND-ID POST len={data.Length}");
                    // Same per-call GUID-suffixed temp file + atomic Replace/Move
                    // as HandleSaveApi's POST — see that method's comment for why
                    // a fixed temp filename isn't safe under concurrent writers.
                    var tmpPath = FriendIdentityFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(tmpPath, data);
                    if (File.Exists(FriendIdentityFilePath)) File.Replace(tmpPath, FriendIdentityFilePath, null);
                    else File.Move(tmpPath, FriendIdentityFilePath);
                    WriteJson(ctx, 200, "{\"ok\":true}");
                    break;
                }
                case "DELETE":
                {
                    LogApiEvent("FRIEND-ID DELETE");
                    if (File.Exists(FriendIdentityFilePath)) File.Delete(FriendIdentityFilePath);
                    WriteJson(ctx, 200, "{\"ok\":true}");
                    break;
                }
                default:
                    ctx.Response.StatusCode = 405;
                    break;
            }
        }
        catch (Exception ex)
        {
            LogApiEvent($"FRIEND-ID EXCEPTION method={ctx.Request.HttpMethod}: {ex}");
            WriteJson(ctx, 500, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    private static void WriteJson(HttpListenerContext ctx, int status, string json)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private void StopServer()
    {
        try { _listener?.Stop(); _listener?.Close(); }
        catch { /* already gone */ }
    }
}
