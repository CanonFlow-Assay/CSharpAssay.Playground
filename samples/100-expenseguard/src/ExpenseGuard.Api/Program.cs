using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json.Serialization;
using ExpenseGuard.Api;
using ExpenseGuard.Application;
using ExpenseGuard.Domain;
using ExpenseGuard.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Npgsql;

if (args.FirstOrDefault() == "--guided-freshness") { Environment.ExitCode = GuidedFreshness.Run(args); return; }

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = ReceiptBlobStore.MaximumBytes + 8192);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    o.SerializerOptions.RespectRequiredConstructorParameters = true;
    o.SerializerOptions.RespectNullableAnnotations = true;
});
var connection = builder.Configuration["ConnectionStrings:Expenses"] ?? throw new InvalidOperationException("Expenses connection string required.");
var identities = builder.Configuration.GetSection("ExpenseGuard:Identities").Get<Identity[]>() ?? [];
if (identities.Length == 0 || identities.Any(i => string.IsNullOrWhiteSpace(i.Employee) || i.Token.Length < 32 || !Enum.IsDefined(i.Role)) ||
    identities.Select(i => i.Token).Distinct(StringComparer.Ordinal).Count() != identities.Length)
    throw new InvalidOperationException("Configure unique server-side identity tokens (32+ characters), employees and roles.");
builder.Services.AddSingleton(identities);
builder.Services.AddAuthentication("ExpenseKey").AddScheme<AuthenticationSchemeOptions, KeyAuthentication>("ExpenseKey", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddSingleton(NpgsqlDataSource.Create(connection));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PostgresExpenseStore>();
builder.Services.AddSingleton<IExpenseStore>(s => s.GetRequiredService<PostgresExpenseStore>());
var rateItems = builder.Configuration.GetSection("ExpenseGuard:Rates").Get<ExchangeRate[]>() ?? [];
if (rateItems.GroupBy(r => (r.From, r.To, r.On)).Any(g => g.Count() > 1)) throw new InvalidOperationException("Duplicate FX fixture entries.");
builder.Services.AddSingleton<IRateSource>(new PinnedRateSource(rateItems.ToImmutableArray()));
builder.Services.AddSingleton<IExpenseAdvisor>(new DemoAdvisor(builder.Configuration["ExpenseGuard:DemoRecommendation"] ?? "Approved"));
builder.Services.AddSingleton<ExpenseService>();
var receiptsDirectory = Path.GetFullPath(builder.Configuration["ExpenseGuard:ReceiptDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, ".local/receipts"));
var publicDirectory = Path.GetFullPath(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));
if (receiptsDirectory == publicDirectory || receiptsDirectory.StartsWith(publicDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
    throw new InvalidOperationException("Receipt storage must be outside the public web root.");
builder.Services.AddSingleton(new ReceiptBlobStore(receiptsDirectory));
if (builder.Configuration.GetValue<bool>("ExpenseGuard:EnableLiveExtraction"))
{
    var model = builder.Configuration["ExpenseGuard:OpenAiModel"];
    var key = builder.Configuration["ExpenseGuard:OpenAiApiKey"];
    var maximum = builder.Configuration.GetValue("ExpenseGuard:MaximumExtractionRequests", 3);
    if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(key) || maximum is < 1 or > 8)
        throw new InvalidOperationException("Live extraction requires a model, API key and request budget between 1 and 8.");
    builder.Services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) });
    builder.Services.AddSingleton<IReceiptExtractor>(s => new OpenAiReceiptExtractor(s.GetRequiredService<HttpClient>(), model, key, maximum));
}
else builder.Services.AddSingleton<IReceiptExtractor, DisabledReceiptExtractor>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    var receiptUpload = HttpMethods.IsPost(context.Request.Method) && context.Request.Path.Value?.EndsWith("/receipt-file", StringComparison.Ordinal) == true;
    if (!receiptUpload && context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
        size.MaxRequestBodySize = 16384;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'none'";
    try { await next(context); }
    catch (BadHttpRequestException e) when (!context.Response.HasStarted)
    { await Results.Problem("Invalid or oversized request.", statusCode: e.StatusCode).ExecuteAsync(context); }
    catch (Exception e) when (e is not OperationCanceledException && !context.Response.HasStarted)
    {
        app.Logger.LogError(e, "Expense operation failed.");
        context.Response.Clear();
        await Results.Problem("Operation failed. No successful state change is being reported.", statusCode: 500).ExecuteAsync(context);
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Ok(new { status = "running", mode = "bounded-preview", payments = "simulated", ai = "offline-demo" }));
app.MapGet("/", (HttpRequest request) => Results.Redirect(request.Query["demo"] == "recorded" ? "/index.html?demo=recorded" : "/index.html"));
app.MapGet("/claims/{id:guid}/evidence", (IWebHostEnvironment env) => Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));
var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/me", (HttpContext c, IReceiptExtractor extractor) => { var actor = GetActor(c); return Results.Ok(new { employee = actor.Id.Value, role = actor.Role.ToString(), policy = Policies.Demo,
    receiptExtraction = extractor.Mode, extractionEnabled = extractor.Enabled, mode = "bounded-preview" }); });
api.MapGet("/claims", async (HttpContext context, IExpenseStore store, CancellationToken ct) =>
    Results.Ok((await store.ListAsync(GetActor(context), ct)).Select(r => new { claim = ClaimDocument.From(r.Claim), version = r.Version, hashChainValid = r.HashChainValid })));
api.MapPost("/claims", async (DraftRequest request, HttpContext context, ExpenseService service, CancellationToken ct) =>
{
    var actor = GetActor(context);
    if (request.Merchant is not { Length: > 0 and <= 200 } || request.Currency is not ("USD" or "EUR" or "GBP") ||
        request.Units is < 1 or > 366 || !Enum.IsDefined(request.Category)) return Results.BadRequest(new { error = "InvalidDraftInput" });
    var c = new ExpenseClaim(new(Guid.NewGuid()), actor.Id, new(request.Amount, request.Currency), request.Category,
        request.Merchant, request.SpentOn, request.Units, new MissingReceipt(), new Draft(), new NoAssessment());
    return Response(await service.CreateAsync(c, actor, ct));
});
api.MapGet("/claims/{id:guid}", async (Guid id, HttpContext context, IExpenseStore store, CancellationToken ct) =>
{
    var bundle = await store.ReadAsync(new(id), ct);
    return bundle is null || !Access.CanRead(bundle.Claim, GetActor(context)) ? Results.NotFound() :
        Results.Ok(new { claim = ClaimDocument.From(bundle.Claim), version = bundle.Version });
});
api.MapPut("/claims/{id:guid}", async (Guid id, RevisionRequest request, HttpContext context, ExpenseService service, CancellationToken ct) =>
{
    if (request.Merchant is not { Length: > 0 and <= 200 } || request.Currency is not ("USD" or "EUR" or "GBP") ||
        request.Units is < 1 or > 366 || !Enum.IsDefined(request.Category)) return Results.BadRequest(new { error = "InvalidDraftInput" });
    return Response(await service.ExecuteAsync(new(id), request.ExpectedVersion,
        new Revise(new(request.Amount, request.Currency), request.Category, request.Merchant, request.SpentOn, request.Units), GetActor(context), ct));
});
api.MapPost("/claims/{id:guid}/receipt", async (Guid id, ReceiptRequest request, HttpContext context, ExpenseService service, CancellationToken ct) =>
{
    if (request.Reference is not { Length: > 0 and <= 500 } || request.Reference.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
        request.Sha256 is not { Length: 64 } || !request.Sha256.All(Uri.IsHexDigit))
        return Results.BadRequest(new { error = "InvalidReceiptReference" });
    return Response(await service.ExecuteAsync(new(id), request.ExpectedVersion,
        new Attach(new(request.Reference, request.Sha256.ToLowerInvariant(), request.Itemized)), GetActor(context), ct));
});
api.MapPost("/claims/{id:guid}/receipt-file", async (Guid id, int expectedVersion, bool itemized, HttpContext context,
    IExpenseStore store, ReceiptBlobStore blobs, ExpenseService service, CancellationToken ct) =>
{
    var actor = GetActor(context); var claim = await store.ReadAsync(new(id), ct);
    if (claim is null || actor.Role != Role.Employee || actor.Id != claim.Claim.Employee) return Results.NotFound();
    if (!claim.HashChainValid) return Results.Conflict(new { error = "LedgerIntegrityFailure" });
    if (claim.Version != expectedVersion) return Results.Conflict(new { error = "VersionConflict" });
    if (claim.Claim.State is not (Draft or NeedsMoreInfo)) return Results.Conflict(new { error = "AttachNotAllowed" });
    if (context.Request.ContentLength > ReceiptBlobStore.MaximumBytes) return Results.StatusCode(413);
    if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        limit.MaxRequestBodySize = ReceiptBlobStore.MaximumBytes + 8192;
    try { return Response(await ReceiptAttachment.UploadAsync(blobs, service, new(id), expectedVersion, itemized, actor, context.Request.Body, ct)); }
    catch (InvalidDataException) { return Results.BadRequest(new { error = "InvalidReceiptFile", detail = "Upload a PDF, PNG or JPEG of at most 3 MiB." }); }
});
api.MapGet("/claims/{id:guid}/receipt-file", async (Guid id, int? sequence, HttpContext context, IExpenseStore store, ReceiptBlobStore blobs, CancellationToken ct) =>
{
    var b = await store.ReadAsync(new(id), ct);
    if (b is null || !Access.CanRead(b.Claim, GetActor(context))) return Results.NotFound();
    if (!b.HashChainValid) return Results.Conflict(new { error = "LedgerIntegrityFailure" });
    var selected = sequence is int number ? b.Receipts.FirstOrDefault(r => r.Sequence == number) : null;
    if (sequence.HasValue && selected is null) return Results.NotFound();
    var snapshot = selected is null ? b.Claim : ClaimDocument.Parse(selected.SnapshotJson).ToDomain();
    if (snapshot.Receipt is not Receipt receipt) return Results.NotFound();
    try
    {
        var content = await blobs.ReadAsync(id, receipt.Reference, receipt.Sha256, ct);
        return content is null ? Results.NotFound() : Results.File(content.Bytes, content.ContentType,
            "receipt" + (content.ContentType == "application/pdf" ? ".pdf" : content.ContentType == "image/png" ? ".png" : ".jpg"));
    }
    catch (InvalidDataException) { return Results.Conflict(new { error = "ReceiptIntegrityFailure" }); }
});
api.MapPost("/claims/{id:guid}/receipt-extraction", async (Guid id, ExtractionRequest request, HttpContext context,
    IExpenseStore store, ReceiptBlobStore blobs, ExpenseService service, IReceiptExtractor extractor, CancellationToken ct) =>
{
    var actor = GetActor(context); var b = await store.ReadAsync(new(id), ct);
    if (b is null || actor.Role != Role.Employee || actor.Id != b.Claim.Employee) return Results.NotFound();
    if (!b.HashChainValid) return Results.Conflict(new { error = "LedgerIntegrityFailure" });
    if (b.Version != request.ExpectedVersion) return Results.Conflict(new { error = "VersionConflict" });
    if (!extractor.Enabled) return Results.Ok(new { proposal = await extractor.ExtractAsync([], "", ct), version = b.Version });
    if (!request.ConfirmExternalDisclosure) return Results.Conflict(new { error = "ExternalDisclosureRequired" });
    if (b.Claim.Receipt is not Receipt receipt) return Results.Conflict(new { error = "StoredReceiptRequired" });
    ReceiptContent? content;
    try { content = await blobs.ReadAsync(id, receipt.Reference, receipt.Sha256, ct); }
    catch (InvalidDataException) { return Results.Conflict(new { error = "ReceiptIntegrityFailure" }); }
    if (content is null) return Results.Conflict(new { error = "StoredReceiptRequired" });
    var audit = await service.RecordExtractionRequestAsync(new(id), b.Version, actor, ct);
    if (audit.Error.Length > 0) return Response(audit);
    var proposal = await extractor.ExtractAsync(content.Bytes, content.ContentType, ct);
    return Results.Ok(new { proposal, version = audit.Version });
});
api.MapPost("/claims/{id:guid}/assess", async (Guid id, VersionRequest request, HttpContext context, ExpenseService service, CancellationToken ct) =>
    Response(await service.AssessAsync(new(id), request.ExpectedVersion, GetActor(context), ct)));
api.MapPost("/claims/{id:guid}/commands", async (Guid id, CommandRequest request, HttpContext context, ExpenseService service, CancellationToken ct) =>
{
    if (request.Action == "reimburse") return Response(await service.RequestPaymentAsync(new(id), request.ExpectedVersion, GetActor(context), ct));
    Command? command = request.Action switch
    {
        "submit" => new Submit(), "approve" => new Approve(),
        "reject" when request.Reason is { Length: > 0 and <= 300 } r => new Reject(r),
        "hold" when request.Reason is { Length: > 0 and <= 300 } r => new Hold(r),
        _ => null
    };
    return command.HasValue ? Response(await service.ExecuteAsync(new(id), request.ExpectedVersion, command.GetValueOrDefault(), GetActor(context), ct)) :
        Results.BadRequest(new { error = "UnknownOrInvalidCommand" });
});
api.MapGet("/claims/{id:guid}/advice", async (Guid id, HttpContext context, IExpenseStore store, IExpenseAdvisor advisor, CancellationToken ct) =>
{
    var b = await store.ReadAsync(new(id), ct);
    return b is null || !Access.CanRead(b.Claim, GetActor(context)) ? Results.NotFound() : Results.Ok(await advisor.SuggestAsync(b.Claim, ct));
});
api.MapGet("/claims/{id:guid}/evidence", async (Guid id, HttpContext context, IExpenseStore store, PostgresExpenseStore payments, IExpenseAdvisor advisor, ReceiptBlobStore blobs, CancellationToken ct) =>
{
    var b = await store.ReadAsync(new(id), ct);
    return b is null || !Access.CanRead(b.Claim, GetActor(context)) ? Results.NotFound() : Results.Ok(new
    {
        claim = ClaimDocument.From(b.Claim), version = b.Version, decisionAuthority = "Deterministic Domain Policy Engine",
        audit = new { b.HashChainValid, receipts = b.Receipts },
        receiptStorage = await ObserveReceipt(b.Claim, blobs, ct),
        paymentWork = await payments.ReadPaymentWorkAsync(new(id), ct),
        csassay = BuildEvidence.Read(builder.Configuration["ExpenseGuard:EvidenceRoot"]),
        aiAdvice = await advisor.SuggestAsync(b.Claim, ct), paymentMode = "SIMULATED — no money moved"
    });
});
if (builder.Configuration.GetValue<bool>("ExpenseGuard:Initialize")) await app.Services.GetRequiredService<PostgresExpenseStore>().InitializeAsync(default);
await app.RunAsync();

static Actor GetActor(HttpContext c) => new(new(c.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Identity missing.")),
    Enum.Parse<Role>(c.User.FindFirstValue(ClaimTypes.Role) ?? ""));
static IResult Response(StoreResult r) => r.Error.Length == 0 ? Results.Ok(new { claim = r.Claim is ExpenseClaim c ? ClaimDocument.From(c) : null, version = r.Version }) :
    r.Error == "NotFound" ? Results.NotFound() : Results.Json(new { error = r.Error }, statusCode: 409);
static async Task<string> ObserveReceipt(ExpenseClaim claim, ReceiptBlobStore blobs, CancellationToken ct)
{
    if (claim.Receipt is not Receipt receipt) return "NotProvided";
    if (!receipt.Reference.StartsWith("blob:", StringComparison.Ordinal)) return "ExternalAssertion";
    try { return await blobs.ReadAsync(claim.Id.Value, receipt.Reference, receipt.Sha256, ct) is null ? "Missing" : "VerifiedHash"; }
    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { return "Invalid"; }
}

namespace ExpenseGuard.Api
{
    public sealed record DraftRequest(decimal Amount, string? Currency, Category Category, string? Merchant, DateOnly SpentOn, int Units = 1);
    public sealed record RevisionRequest(int ExpectedVersion, decimal Amount, string? Currency, Category Category, string? Merchant, DateOnly SpentOn, int Units = 1);
    public sealed record ExtractionRequest(int ExpectedVersion, bool ConfirmExternalDisclosure);
    public sealed record ReceiptRequest(int ExpectedVersion, string? Reference, string? Sha256, bool Itemized);
    public sealed record VersionRequest(int ExpectedVersion);
    public sealed record CommandRequest(int ExpectedVersion, string? Action, string? Reason = null);
}
