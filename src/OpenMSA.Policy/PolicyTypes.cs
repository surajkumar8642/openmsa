namespace OpenMSA.Policy;

public sealed record PolicyRule(
    string Id,
    string Effect,
    string[] Operations,
    Condition? When = null);

public sealed record Condition(
    Dictionary<string, Dictionary<string, string>>? Field,
    Condition[]? All,
    Condition[]? Any);

public sealed record PolicyDocument(
    string Version,
    string Default,
    PolicyRule[] Rules);

public static class PolicyEffect
{
    public const string Allow = "allow";
    public const string Deny = "deny";
}
