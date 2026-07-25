namespace OpenMSA.Core;

public sealed record GlobalContract(
    string Name,
    string Version,
    string[] AllowedSections,
    string[] ReservedSections,
    string[] MandatoryOperations,
    string[] MandatoryPolicyOperators,
    int MaxPageSize,
    int MaxResourceBytes,
    int DefaultMaxResults,
    TimeSpan AuthzRequestTTL,
    IReadOnlyDictionary<string, string>? Requirements = null);

public sealed record SectionSpec(
    string Name,
    OperationKind[] Operations,
    string? PolicyRef = null,
    bool IsServiceSection = false);

public sealed record ManagedSpaceMetadata(string SpaceId, string OwnerSubjectId, string Name, string Version);
public sealed record ManagedSpaceSpec(string GlobalContractVersion, IDictionary<string, SectionSpec> Sections);

public sealed record ManagedSpaceManifest(
    string ApiVersion,
    string Kind,
    ManagedSpaceMetadata Metadata,
    ManagedSpaceSpec Spec);
