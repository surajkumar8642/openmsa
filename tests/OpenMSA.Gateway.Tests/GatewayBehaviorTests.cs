using System.Text.Json;
using OpenMSA.Core;
using OpenMSA.Identity;
using OpenMSA.Index;
using OpenMSA.Policy;
using OpenMSA.Storage;

namespace OpenMSA.Gateway.Tests;

public class GatewayBehaviorTests
{
    [Fact]
    public async Task Owner_can_create_and_read_sales_bill_resource()
    {
        var (gateway, identity, manifestStore) = BuildGateway();
        var owner = await Register(identity, "+1-555-010-1000", "owner");
        var token = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-1000", "Passw0rd!"), "openmsa-gateway");

        var spaceResult = await gateway.CreateSpaceAsync("Supplier", token.AccessToken);
        Assert.True(spaceResult.Success, $"create-space failed: {spaceResult.Message}");
        var spaceRef = spaceResult.Value!;

        var envelope = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "SalesBill",
            new ResourceMetadata(string.Empty, spaceRef, owner.SubjectId, "1.0.0", DateTimeOffset.UtcNow),
            new Dictionary<string, string>
            {
                ["receiverMobileHash"] = owner.MobileHash,
                ["receiverSubjectId"] = owner.SubjectId,
                ["billNumber"] = "INV-101"
            },
            new { amount = 100 },
            string.Empty);

        var created = await gateway.CreateResourceAsync(spaceRef, "private", token.AccessToken, JsonSerializer.Serialize(envelope));
        Assert.True(created.Success, $"create-resource failed: {created.Message}");

        var read = await gateway.GetResourceAsync(spaceRef, "private", created.Value!.Metadata.ResourceId, token.AccessToken);
        Assert.True(read.Success);
        Assert.NotNull(read.Value);
        Assert.Equal("SalesBill", read.Value!.Kind);
    }

    [Fact]
    public async Task Visitor_cannot_read_private_resource()
    {
        var (gateway, identity, _) = BuildGateway();
        var owner = await Register(identity, "+1-555-010-2000", "owner");
        var visitor = await Register(identity, "+1-555-010-2001", "visitor");

        var ownerToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-2000", "Passw0rd!"), "openmsa-gateway");
        var visitorToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-2001", "Passw0rd!"), "openmsa-gateway");

        var spaceResult = await gateway.CreateSpaceAsync("PrivateSpace", ownerToken.AccessToken);
        var spaceId = spaceResult.Value!;

        var envelope = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "Secret",
            new ResourceMetadata(string.Empty, spaceId, owner.SubjectId, "1.0.0", DateTimeOffset.UtcNow),
            new Dictionary<string, string> { ["receiverMobileHash"] = owner.MobileHash },
            new { note = "restricted" },
            string.Empty);

        var created = await gateway.CreateResourceAsync(spaceId, "private", ownerToken.AccessToken, JsonSerializer.Serialize(envelope));
        Assert.True(created.Success);

        Assert.NotNull(created.Value);
        var byVisitor = await gateway.GetResourceAsync(spaceId, "private", created.Value!.Metadata.ResourceId, visitorToken.AccessToken);
        Assert.False(byVisitor.Success);
    }

    [Fact]
    public async Task Depositor_cannot_list_or_read_inbox()
    {
        var (gateway, identity, storage) = BuildGateway();
        var owner = await Register(identity, "+1-555-010-3000", "owner");
        var sender = await Register(identity, "+1-555-010-3001", "sender");

        var ownerToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-3000", "Passw0rd!"), "openmsa-gateway");
        var senderToken = await identity.AuthenticateAsync(new LoginRequest("+1-555-010-3001", "Passw0rd!"), "openmsa-gateway");

        var spaceResult = await gateway.CreateSpaceAsync("InboxSample", ownerToken.AccessToken);
        Assert.True(spaceResult.Success, $"create-space failed: {spaceResult.Message}");
        Assert.True(spaceResult.Success, $"create-space failed: {spaceResult.Message}");
        var spaceRef = spaceResult.Value!;

        var objectPayload = new ResourceEnvelope(
            "openmsa.io/v1alpha1",
            "SalesBill",
            new ResourceMetadata(IdGenerator.NewId(IdSchemes.Resource), spaceRef, sender.SubjectId, "1.0.0", DateTimeOffset.UtcNow),
            new Dictionary<string, string>
            {
                ["receiverMobileHash"] = owner.MobileHash,
                ["receiverSubjectId"] = owner.SubjectId,
                ["billNumber"] = "INV-999"
            },
            new { amount = 999 });

        var stored = await storage.PutAsync(
            IdGenerator.NewId("obj"),
            JsonSerializer.SerializeToUtf8Bytes(objectPayload),
            "application/json");

        var deposit = await gateway.DepositInboxAsync(spaceRef, senderToken.AccessToken,
            JsonSerializer.Serialize(new
            {
                ResourceId = IdGenerator.NewId(IdSchemes.Resource),
                StorageObjectRef = stored.ObjectId,
                Claims = new Dictionary<string, string>
                {
                    ["receiverMobileHash"] = owner.MobileHash,
                    ["receiverSubjectId"] = owner.SubjectId
                }
            }));
        Assert.True(deposit.Success, $"inbox deposit failed: {deposit.Error} {deposit.Message}");

        var senderList = await gateway.ListResourcesAsync(spaceRef, "inbox", senderToken.AccessToken, null, null, 10);
        Assert.True(senderList.Success);
        Assert.NotNull(senderList.Value);
        Assert.Empty(senderList.Value!.Items);

        var senderRead = await gateway.GetResourceAsync(spaceRef, "inbox", deposit.Value!.Metadata.ResourceId, senderToken.AccessToken);
        Assert.False(senderRead.Success);

        var ownerRead = await gateway.GetResourceAsync(spaceRef, "inbox", deposit.Value!.Metadata.ResourceId, ownerToken.AccessToken);
        Assert.True(ownerRead.Success, $"{ownerRead.Error} {ownerRead.Message}");
    }

    [Fact]
    public async Task Local_storage_rejects_path_traversal_object_id()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"openmsa-store-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var storage = new LocalFileStorage(tempDir);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await storage.PutAsync("../evil", Array.Empty<byte>(), "text/plain");
            });
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private static async Task<IdentityUser> Register(IdentityService identity, string mobile, string role)
    {
        return await identity.RegisterAsync(new RegisterUserRequest(mobile, "Passw0rd!", [role]));
    }

    private static (GatewayService Gateway, IdentityService Identity, LocalFileStorage Storage) BuildGateway()
    {
        var keyRing = new IdentityKeyRing();
        keyRing.GenerateNew("server");
        var jwt = new JwtService(keyRing, new JwtOptions
        {
            Issuer = "openmsa-identity",
            Audience = "openmsa-gateway",
            MobileHashSecret = "test-secret",
            Expiry = TimeSpan.FromMinutes(30)
        });

        var identity = new IdentityService(new InMemoryIdentityStore(), new PasswordService(), jwt, "test-secret");
        var manifestStore = new InMemoryManifestStore();
        var policyStore = new InMemoryPolicyStore();

        var indexPath = Path.Combine(Path.GetTempPath(), $"openmsa-index-{Guid.NewGuid()}.sqlite");
        var index = new SqliteIndexAdapter(indexPath);

        var storagePath = Path.Combine(Path.GetTempPath(), $"openmsa-store-{Guid.NewGuid()}");
        Directory.CreateDirectory(storagePath);
        var storage = new LocalFileStorage(storagePath);

        var gateway = new GatewayService(
            jwt,
            new InMemorySpaceResolver(manifestStore),
            manifestStore,
            policyStore,
            index,
            storage,
            new DeclarativePolicyEvaluator(),
            new NoopAuditSink(),
            new FixedWindowRateLimiter());

        return (gateway, identity, storage);
    }
}
