using Microsoft.IdentityModel.Tokens;
using OpenMSA.Gateway;
using OpenMSA.Identity;
using OpenMSA.Index;
using OpenMSA.Policy;
using OpenMSA.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IdentityKeyRing>(_ =>
{
    var keyRing = new IdentityKeyRing();
    keyRing.GenerateNew(Guid.NewGuid().ToString("N"));
    return keyRing;
});
builder.Services.AddSingleton(_ => new PasswordService());
builder.Services.AddSingleton<IIdentityStore, InMemoryIdentityStore>();
builder.Services.AddSingleton<JwtService>(_ =>
{
    var options = new JwtService(
        _.GetRequiredService<IdentityKeyRing>(),
        new JwtOptions
        {
            Issuer = "openmsa-identity",
            Audience = "openmsa-gateway",
            Expiry = TimeSpan.FromMinutes(30),
            MobileHashSecret = "openmsa-mobile-signing-salt"
        });
    return options;
});
builder.Services.AddSingleton<IdentityService>(_ =>
{
    var serviceProvider = _.GetRequiredService<IServiceProvider>();
    return new IdentityService(
        serviceProvider.GetRequiredService<IIdentityStore>(),
        serviceProvider.GetRequiredService<PasswordService>(),
        serviceProvider.GetRequiredService<JwtService>(),
        "openmsa-mobile-signing-salt");
});
builder.Services.AddSingleton<DeclarativePolicyEvaluator>();
builder.Services.AddSingleton<IPolicyEvaluator, DeclarativePolicyEvaluator>();
builder.Services.AddSingleton<InMemoryManifestStore>();
builder.Services.AddSingleton<IManifestStore>(sp => sp.GetRequiredService<InMemoryManifestStore>());
builder.Services.AddSingleton<InMemoryPolicyStore>(sp => new InMemoryPolicyStore());
builder.Services.AddSingleton<IPolicyStore>(sp => sp.GetRequiredService<InMemoryPolicyStore>());
builder.Services.AddSingleton<InMemorySpaceResolver>();
builder.Services.AddSingleton<ISpaceResolver>(sp => sp.GetRequiredService<InMemorySpaceResolver>());
builder.Services.AddSingleton<NoopAuditSink>();
builder.Services.AddSingleton<IAuditSink>(sp => sp.GetRequiredService<NoopAuditSink>());
builder.Services.AddSingleton(_ =>
{
    var db = Path.Combine(Environment.CurrentDirectory, ".openmsa", "index.sqlite");
    Directory.CreateDirectory(Path.GetDirectoryName(db)!);
    return new SqliteIndexAdapter(db);
});
builder.Services.AddSingleton<LocalFileStorage>(_ =>
{
    var root = Path.Combine(Environment.CurrentDirectory, ".openmsa", "objects");
    Directory.CreateDirectory(root);
    return new LocalFileStorage(root);
});
builder.Services.AddSingleton<IStorageAdapter>(sp => sp.GetRequiredService<LocalFileStorage>());
builder.Services.AddSingleton<FixedWindowRateLimiter>();
builder.Services.AddSingleton<IRateLimiter>(sp => sp.GetRequiredService<FixedWindowRateLimiter>());
builder.Services.AddSingleton<GatewayService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    context.Request.EnableBuffering();
    await next();
});

var policyStore = app.Services.GetRequiredService<InMemoryPolicyStore>();
var policy = new PolicyDocument(
    "1.0",
    "deny",
    new[]
    {
        new PolicyRule(
            "owner-full-access",
            "allow",
            ["create", "read", "update", "delete", "query", "deposit"],
            new Condition(
                new Dictionary<string, Dictionary<string, string>>
                {
                    ["subject.id"] = new Dictionary<string, string> { ["equals"] = "owner" }
                },
                null,
                null)),
        new PolicyRule(
            "receiver-can-read",
            "allow",
            ["read", "query"],
            new Condition(
                new Dictionary<string, Dictionary<string, string>>
                {
                    ["subject.mobile_verified"] = new Dictionary<string, string> { ["equals"] = "true" },
                    ["subject.mobile_hash"] = new Dictionary<string, string> { ["equals"] = "X" },
                    ["subject.id"] = new Dictionary<string, string> { ["equalsResource"] = "receiverSubjectId" }
                },
                null,
                null))
    });
policyStore.Add("global", "policies/sales-bills.json", policy);

app.MapGet("/.well-known/jwks.json", (IdentityService identity) => Results.Content(identity.PublicJwks(), "application/json"));

app.MapPost("/v1/auth/register", async (IdentityService identity, RegisterUserRequest req) =>
{
    try
    {
        var user = await identity.RegisterAsync(req);
        return Results.Ok(new { userId = user.SubjectId });
    }
    catch (SecurityTokenException ex)
    {
        return Results.Problem(type: "https://openmsa.dev/problem/invalid", statusCode: 400, title: "Invalid registration", detail: ex.Message);
    }
});

app.MapPost("/v1/auth/login", async (IdentityService identity, LoginRequest req) =>
{
    try
    {
        var token = await identity.AuthenticateAsync(req, "openmsa-gateway");
        return Results.Ok(new { token.AccessToken, token.ExpiresAtUtc });
    }
    catch
    {
        return Results.Problem(type: "https://openmsa.dev/problem/auth", statusCode: 401, title: "Unauthorized", detail: "Invalid credentials.");
    }
});

app.MapPost("/v1/spaces", async (GatewayService gateway, HttpContext http) =>
{
    var token = ExtractBearer(http);
    var request = await http.Request.ReadFromJsonAsync<SpaceCreateRequest>() ?? new SpaceCreateRequest(string.Empty);
    if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(request.Name))
        return Results.Problem(type: "https://openmsa.dev/problem/input", statusCode: 400, title: "Bad request", detail: "missing fields");

    var result = await gateway.CreateSpaceAsync(request.Name, token);
    return result.Success
        ? Results.Ok(new { spaceId = result.Value })
        : Results.Problem(type: "https://openmsa.dev/problem/gateway", statusCode: 403, title: "Forbidden", detail: result.Message ?? "Operation not allowed");
});

app.MapGet("/v1/spaces/{spaceRef}/manifest", async (string spaceRef, GatewayService gateway) =>
{
    var result = await gateway.GetManifestAsync(spaceRef);
    return result.Success
        ? Results.Ok(result.Value)
        : Results.Problem(type: "https://openmsa.dev/problem/gateway", statusCode: 404, title: "Not Found", detail: "Resource not available.");
});

app.MapPost("/v1/spaces/{spaceRef}/inbox", async (string spaceRef, HttpContext http, GatewayService gateway) =>
{
    var token = ExtractBearer(http);
    if (string.IsNullOrWhiteSpace(token))
        return Results.Problem(type: "https://openmsa.dev/problem/auth", statusCode: 401, title: "Unauthorized", detail: "Bearer token required.");
    var json = await new StreamReader(http.Request.Body).ReadToEndAsync();
    var result = await gateway.DepositInboxAsync(spaceRef, token, json);
    if (!result.Success)
        return NotFoundOrGeneric(result);
    return Results.Ok(result.Value);
});

app.MapGet("/v1/spaces/{spaceRef}/resources", async (string spaceRef, string? receiverMobileHash, string? cursor, int limit, string? section, HttpContext http, GatewayService gateway) =>
{
    var token = ExtractBearer(http);
    if (string.IsNullOrWhiteSpace(token))
        return Results.Problem(type: "https://openmsa.dev/problem/auth", statusCode: 401, title: "Unauthorized", detail: "Bearer token required.");
    var targetSection = section ?? "salesBills";
    var result = await gateway.ListResourcesAsync(spaceRef, targetSection, token, receiverMobileHash, cursor, limit == 0 ? 25 : limit);
    return result.Success
        ? Results.Ok(result.Value)
        : NotFoundOrGeneric(result);
});

app.MapGet("/v1/spaces/{spaceRef}/resources/{resourceId}", async (string spaceRef, string resourceId, string? section, HttpContext http, GatewayService gateway) =>
{
    var token = ExtractBearer(http);
    if (string.IsNullOrWhiteSpace(token))
        return Results.Problem(type: "https://openmsa.dev/problem/auth", statusCode: 401, title: "Unauthorized", detail: "Bearer token required.");
    var result = await gateway.GetResourceAsync(spaceRef, section ?? "salesBills", resourceId, token);
    return result.Success
        ? Results.Ok(result.Value)
        : NotFoundOrGeneric(result);
});

app.MapPost("/v1/spaces/{spaceRef}/resources", async (string spaceRef, HttpContext http, GatewayService gateway) =>
{
    var token = ExtractBearer(http);
    if (string.IsNullOrWhiteSpace(token))
        return Results.Problem(type: "https://openmsa.dev/problem/auth", statusCode: 401, title: "Unauthorized", detail: "Bearer token required.");
    var section = http.Request.Query["section"].ToString();
    var body = await new StreamReader(http.Request.Body).ReadToEndAsync();
    var result = await gateway.CreateResourceAsync(spaceRef, string.IsNullOrWhiteSpace(section) ? "salesBills" : section!, token, body);
    return result.Success ? Results.Ok(result.Value) : NotFoundOrGeneric(result);
});

app.MapPatch("/v1/spaces/{spaceRef}/resources/{resourceId}", async (string spaceRef, string resourceId, string? section, HttpContext http, GatewayService gateway) =>
{
    var token = ExtractBearer(http);
    if (string.IsNullOrWhiteSpace(token))
        return Results.Problem(type: "https://openmsa.dev/problem/auth", statusCode: 401, title: "Unauthorized", detail: "Bearer token required.");
    var body = await new StreamReader(http.Request.Body).ReadToEndAsync();
    var result = await gateway.UpdateResourceAsync(spaceRef, section ?? "salesBills", resourceId, token, body);
    return result.Success ? Results.Ok(result.Value) : NotFoundOrGeneric(result);
});

app.MapDelete("/v1/spaces/{spaceRef}/resources/{resourceId}", async (string spaceRef, string resourceId, string? section, HttpContext http, GatewayService gateway) =>
{
    var token = ExtractBearer(http);
    if (string.IsNullOrWhiteSpace(token))
        return Results.Problem(type: "https://openmsa.dev/problem/auth", statusCode: 401, title: "Unauthorized", detail: "Bearer token required.");
    var result = await gateway.DeleteResourceAsync(spaceRef, section ?? "private", resourceId, token);
    return result.Success ? Results.Ok(new { deleted = true }) : NotFoundOrGeneric(result);
});

app.Run();

string ExtractBearer(HttpContext context)
{
    var header = context.Request.Headers["Authorization"].ToString();
    if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return string.Empty;
    return header["Bearer ".Length..].Trim();
}

IResult NotFoundOrGeneric<T>(GatewayResult<T> result)
{
    var status = result.Error is GatewayErrorType.NotFoundOrForbidden ? 404 : 400;
    var message = result.Error is GatewayErrorType.NotFoundOrForbidden
        ? "The requested resource is unavailable."
        : "The request could not be completed.";
    return Results.Problem(type: "https://openmsa.dev/problem/gateway", statusCode: status, title: status == 404 ? "Not Found" : "Bad Request", detail: message);
}

record SpaceCreateRequest(string Name);
