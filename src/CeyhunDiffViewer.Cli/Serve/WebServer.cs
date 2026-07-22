using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using CeyhunDiffViewer.Cli.Output;
using CeyhunDiffViewer.Core;

namespace CeyhunDiffViewer.Cli.Serve;

public static class ServeCommand
{
    public static int Run(string[] args)
    {
        var repo = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? ".";
        var port = 5099;
        for (var i = 0; i < args.Length; i++)
            if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
                port = p;

        new WebServer(new DiffService(repo), port).Start();
        return 0;
    }
}

/// <summary>Minimal, dependency-free local server exposing the engine to the web UI.</summary>
public sealed class WebServer
{
    private readonly DiffService _service;
    private readonly int _port;

    public WebServer(DiffService service, int port)
    {
        _service = service;
        _port = port;
    }

    public void Start()
    {
        var prefix = $"http://localhost:{_port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not start server on {prefix}: {ex.Message}");
            Console.Error.WriteLine("Try a different port with --port <n>.");
            return;
        }

        Console.WriteLine($"CeyhunDiffViewer running at {prefix}   (Ctrl+C to stop)");
        OpenBrowser(prefix);

        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = listener.GetContext(); }
            catch { break; }

            try
            {
                Handle(context);
            }
            catch (Exception ex)
            {
                WriteText(context, 500, "application/json; charset=utf-8",
                    $"{{\"error\":{JsonSerializer.Serialize(ex.Message)}}}");
            }
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var query = context.Request.QueryString;

        switch (path)
        {
            case "/":
                WriteText(context, 200, "text/html; charset=utf-8", WebUi.Html);
                break;

            case "/api/refs":
                WriteJson(context, RefsJson());
                break;

            case "/api/scan":
            {
                var baseRef = query["base"] ?? "HEAD~1";
                var targetRef = query["target"] ?? "HEAD";
                var filters = (query["filter"] ?? "*.prefab")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var changes = _service.Scan(baseRef, targetRef, filters);
                WriteJson(context, JsonOutput.Scan(baseRef, targetRef, changes));
                break;
            }

            case "/api/diff":
            {
                var baseRef = query["base"] ?? "HEAD~1";
                var targetRef = query["target"] ?? "HEAD";
                var diff = _service.Diff(baseRef, targetRef, query["path"], query["guid"]);
                WriteJson(context, JsonOutput.Diff(diff));
                break;
            }

            default:
                WriteText(context, 404, "text/plain; charset=utf-8", "not found");
                break;
        }
    }

    private string RefsJson()
    {
        var refs = _service.ListRefs();
        return JsonSerializer.Serialize(
            refs.Select(r => new { kind = r.Kind, value = r.Value, label = r.Label }));
    }

    private static void WriteJson(HttpListenerContext context, string json)
        => WriteText(context, 200, "application/json; charset=utf-8", json);

    private static void WriteText(HttpListenerContext context, int status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }

    private static void OpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false }); }
        catch { /* headless / no browser — the URL is printed anyway */ }
    }
}
