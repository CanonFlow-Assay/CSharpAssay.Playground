using System.Net;
using System.Net.Http.Json;
namespace Assay.Tooling;

public static class LocalServices
{
    public static async Task ServeDocs(int port)
    {
        Json.Check(port is >= 1 and <= 65535, "Invalid loopback port.");
        var root = Path.Combine(Root, "docs");
        using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        Console.WriteLine($"Documentation: http://127.0.0.1:{port}/");
        while (listener.IsListening)
        {
            var context = await listener.GetContextAsync();
            try
            {
                Json.Check(context.Request.HttpMethod is "GET" or "HEAD", "Read-only documentation server.");
                var relative = Uri.UnescapeDataString(context.Request.Url!.AbsolutePath.TrimStart('/'));
                var path = Inside(root, relative.Length == 0 ? "index.html" : relative);
                if (!File.Exists(path)) { context.Response.StatusCode = 404; }
                else
                {
                    context.Response.ContentType = Path.GetExtension(path) switch { ".html" => "text/html", ".css" => "text/css", ".js" => "text/javascript", ".json" => "application/json", ".svg" => "image/svg+xml", ".png" => "image/png", _ => "application/octet-stream" };
                    context.Response.ContentLength64 = new FileInfo(path).Length;
                    if (context.Request.HttpMethod == "GET") { await using var stream = File.OpenRead(path); await stream.CopyToAsync(context.Response.OutputStream); }
                }
            }
            catch (Exception error) when (error is IOException or ArgumentException) { context.Response.StatusCode = 400; }
            finally { context.Response.Close(); }
        }
    }
    private static readonly string[] values = new[] { "choice" };

    public static JsonObject OllamaPayload(string model, JsonNode state, JsonNode question)
    {
        var criteria = question["criteria"] as JsonObject;
        Json.Check(question.S("type") == "choice" && criteria is not null && criteria.Count is >= 2 and <= 32, "Only bounded choice questions are supported.");
        foreach (var (key, value) in criteria)
        {
            Json.Check(key.Length > 0 && value?.GetValueKind() == JsonValueKind.String && value.GetValue<string>().Length > 0, "Choices require names and descriptions.");
        }

        Json.Check(question["instructions"]?.GetValueKind() == JsonValueKind.String, "Instructions require text.");
        return Json.Create(("model", model), ("stream", false), ("think", false), ("keep_alive", "0s"), ("options", Json.Create(("temperature", 0), ("num_predict", 128), ("num_ctx", 2048), ("num_thread", 4))), ("messages", new[] { Json.Create(("role", "system"), ("content", question.S("instructions") + "\nChoose using: " + criteria.ToJsonString())), Json.Create(("role", "user"), ("content", state.ToJsonString())) }), ("format", Json.Create(("type", "object"), ("properties", Json.Create(("choice", Json.Create(("type", "string"), ("enum", criteria.Select(p => p.Key)))))), ("required", values), ("additionalProperties", false))));
    }
    public static JsonObject OllamaResponse(string response, JsonNode question)
    {
        Json.Check(Encoding.UTF8.GetByteCount(response) <= 65536, "Oversized upstream response.");
        var result = Json.Parse(response); Json.Check(result.B("done") && result.S("done_reason") == "stop", "Incomplete generation.");
        var choice = Json.Parse(result["message"].S("content")).S("choice");
        return question["criteria"]?[choice] is null ? Json.Create(("type", "refusal")) : Json.Create(("type", "choice"), ("choice", choice));
    }
    public static async Task Serve(Arguments args)
    {
        Json.Check(args.Get("--backend", "ollama") == "ollama", "The Python SetFit backend was retired; use the C# Ollama gateway or a configured provider endpoint.");
        var model = args.Required("--model"); var port = args.Count("--port", 8001); var upstream = args.Count("--ollama-port", 11434);
        Json.Check(port is >= 1 and <= 65535 && upstream is >= 1 and <= 65535, "Ports must be in 1..65535.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false }; using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) }; using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        Console.WriteLine($"Local C# Ollama gateway listening on 127.0.0.1:{port}");
        while (listener.IsListening)
        {
            var context = await listener.GetContextAsync();
            JsonNode response; var status = 200;
            try
            {
                Json.Check(context.Request.HttpMethod == "POST" && context.Request.Headers["Transfer-Encoding"] is null && context.Request.ContentLength64 is > 0 and <= 65536, "Invalid request size/method/encoding.");
                if (context.Request.Url?.AbsolutePath != "/v1/systemone") { status = 404; response = Json.Create(("error", "Unsupported endpoint.")); }
                else
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35)); using var buffer = new MemoryStream();
                    var bytes = new byte[65537]; var total = 0;
                    while (total < bytes.Length) { var count = await context.Request.InputStream.ReadAsync(bytes.AsMemory(total), timeout.Token); if (count == 0) { break; } total += count; }
                    Json.Check(total <= 65536 && total == context.Request.ContentLength64, "Invalid input length.");
                    var data = Json.Parse(Encoding.UTF8.GetString(bytes, 0, total)); Json.Check(data.S("model") == model && data["questions"] is JsonObject questions && questions.Count == 1, "Model/question mismatch.");
                    var entry = data["questions"]!.AsObject().Single(); var payload = OllamaPayload(model, data["state"]!, entry.Value!);
                    using var upstreamResponse = await client.PostAsJsonAsync($"http://127.0.0.1:{upstream}/api/chat", payload, timeout.Token); _ = upstreamResponse.EnsureSuccessStatusCode();
                    await using var stream = await upstreamResponse.Content.ReadAsStreamAsync(timeout.Token); var raw = new byte[65537]; var length = 0;
                    while (length < raw.Length) { var count = await stream.ReadAsync(raw.AsMemory(length), timeout.Token); if (count == 0) { break; } length += count; }
                    var answer = OllamaResponse(Encoding.UTF8.GetString(raw, 0, length), entry.Value!);
                    response = Json.Create(("model", model), ("answers", Json.Create((entry.Key, answer))), ("adapter", "assay-generative-choice-gateway/0.1"));
                }
            }
            catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or InvalidOperationException) { status = 422; response = Json.Create(("error", "Invalid request or upstream output.")); }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException) { status = 503; response = Json.Create(("error", "Local advisor unavailable; no retry performed.")); }
            context.Response.StatusCode = status; context.Response.ContentType = "application/json"; var body = Encoding.UTF8.GetBytes(response.ToJsonString()); context.Response.ContentLength64 = body.Length; await context.Response.OutputStream.WriteAsync(body); context.Response.Close();
        }
    }
    public static async Task<int> WithDatabase(string[] command)
    {
        Json.Check(command.Length > 0, "Supply an explicit command after --.");
        var launch = command;
        var container = string.Empty;
        Dictionary<string, string>? environment = null;
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASSAY_INVENTORY_DATABASE")))
        {
            container = $"assay-inventory-{Guid.NewGuid():N}";
            environment = new() { ["ASSAY_TEMP_POSTGRES_NAME"] = container };
            launch = [Path.Combine(Root, "eng/with-inventory-database"), .. command];
        }

        var execution = await Processes.Run(launch, Directory.GetCurrentDirectory(), seconds: 14400, environment: environment);
        Console.Write(execution.Output);
        if (execution.State is not (ProcessState.TimedOut or ProcessState.Cancelled))
        {
            return execution.ExitCode;
        }

        // Killing the child process tree can bypass the launcher's exit trap.
        if (container.Length > 0)
        {
            (await Processes.Run(["podman", "rm", "--force", "--ignore", container], Root, seconds: 30))
                .Require("Remove interrupted PostgreSQL container");
        }

        return 2;
    }
}
