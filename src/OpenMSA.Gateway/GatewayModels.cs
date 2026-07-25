namespace OpenMSA.Gateway;

public sealed record SpaceCreationRequest(string Name, string OwnerAlias);

public sealed record InboxDepositRequest(string ResourceId, string StorageObjectRef, IDictionary<string, string> Claims);

public sealed record ResourceSummary(
    string ResourceId,
    string Kind,
    string Section,
    string ResourceType,
    string CreatedAtUtc,
    IDictionary<string, string> Claims,
    string? Status = null);

public sealed record PagedResourceResponse(
    IReadOnlyList<ResourceSummary> Items,
    string? NextCursor = null);

public enum GatewayErrorType
{
    None,
    NotFoundOrForbidden,
    InvalidInput,
    Internal
}

public sealed record GatewayResult<T>(bool Success, T? Value, GatewayErrorType Error, string? Message = null);

public static class GenericResponses
{
    public static GatewayResult<T> ForbiddenOrNotFound<T>() => new(false, default, GatewayErrorType.NotFoundOrForbidden, null);
    public static GatewayResult<T> Invalid<T>(string message) => new(false, default, GatewayErrorType.InvalidInput, message);
    public static GatewayResult<T> Fail<T>(string message) => new(false, default, GatewayErrorType.Internal, message);
    public static GatewayResult<T> Ok<T>(T value) => new(true, value, GatewayErrorType.None, null);
}
