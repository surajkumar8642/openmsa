using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenMSA.Core;

namespace OpenMSA.ExampleServer.Tests;

public sealed class ExampleServerApiTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Create_space_requires_authentication_and_returns_problem()
    {
        var response = await _client.PostAsJsonAsync("/v1/spaces", new { name = "NoAuthSpace" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://openmsa.dev/problem/auth", body.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Depositor_can_not_read_inbox_entry_but_owner_can()
    {
        var owner = await RegisterAndLoginAsync("+1-555-901-1010");
        var sender = await RegisterAndLoginAsync("+1-555-901-1011");

        var createSpace = await SendWithBearer(
            HttpMethod.Post,
            "/v1/spaces",
            owner,
            JsonContent.Create(new SpaceRequest("Demo")));
        Assert.Equal(HttpStatusCode.OK, createSpace.StatusCode);
        var createSpaceDoc = await createSpace.Content.ReadFromJsonAsync<JsonElement>();
        var spaceRef = createSpaceDoc.GetProperty("spaceId").GetString()!;

        var mobileHash = new string('a', 64);
        var salesBill = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "SalesBill",
            new ResourceMetadata(string.Empty, spaceRef, string.Empty, "1.0.0", DateTimeOffset.UtcNow),
            new Dictionary<string, string> { ["receiverMobileHash"] = mobileHash, ["receiverSubjectId"] = sender.SubjectId },
            new { amount = 99 },
            string.Empty);
        var salesBillJson = JsonSerializer.Serialize(salesBill);

        var createResource = await SendWithBearer(
            HttpMethod.Post,
            $"/v1/spaces/{spaceRef}/resources?section=salesBills",
            owner,
            new StringContent(salesBillJson, Encoding.UTF8, "application/json"));
        var createResourceBody = await createResource.Content.ReadAsStringAsync();
        Assert.True(createResource.StatusCode == HttpStatusCode.OK, $"{createResource.StatusCode}: {createResourceBody}; request={salesBillJson}");
        var createResourceDoc = await createResource.Content.ReadFromJsonAsync<JsonElement>();
        var storageObjectRef = createResourceDoc.GetProperty("storageObjectRef").GetString()!;

        var depositRequest = new
        {
            ResourceId = IdGenerator.NewId(IdSchemes.Resource),
            StorageObjectRef = storageObjectRef,
            Claims = new Dictionary<string, string> { ["receiverMobileHash"] = mobileHash, ["receiverSubjectId"] = sender.SubjectId }
        };
        var deposit = await SendWithBearer(HttpMethod.Post, $"/v1/spaces/{spaceRef}/inbox", sender, JsonContent.Create(depositRequest));
        var depositBody = await deposit.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, deposit.StatusCode);
        var depositDoc = await deposit.Content.ReadFromJsonAsync<JsonElement>();
        var depositResourceId = depositDoc.GetProperty("metadata").GetProperty("resourceId").GetString()!;

        var senderRead = await SendWithBearer(
            HttpMethod.Get,
            $"/v1/spaces/{spaceRef}/resources/{depositResourceId}?section=inbox",
            sender);
        Assert.Equal(HttpStatusCode.NotFound, senderRead.StatusCode);

        var ownerRead = await SendWithBearer(
            HttpMethod.Get,
            $"/v1/spaces/{spaceRef}/resources/{depositResourceId}?section=inbox",
            owner);
        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
    }

    private async Task<(string Token, string SubjectId)> RegisterAndLoginAsync(string mobile)
    {
        var registerResponse = await _client.PostAsJsonAsync("/v1/auth/register", new { mobile, password = "Passw0rd!", roles = new[] { "member" } });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var registerDoc = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var subjectId = registerDoc.GetProperty("userId").GetString()!;

        var loginResponse = await _client.PostAsJsonAsync("/v1/auth/login", new { mobile, password = "Passw0rd!" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginDoc = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginDoc.GetProperty("accessToken").GetString()!;

        return (token, subjectId);
    }

    private Task<HttpResponseMessage> SendWithBearer(HttpMethod method, string requestUri, (string Token, string SubjectId) auth, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, requestUri)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return _client.SendAsync(request);
    }
}

public sealed record SpaceRequest(string Name);
