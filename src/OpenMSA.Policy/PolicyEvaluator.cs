using OpenMSA.Core;

namespace OpenMSA.Policy;

public interface IPolicyEvaluator
{
    AuthorizationDecision Evaluate(PolicyDocument policy, SubjectClaims subject, string op, string? resourceType, IDictionary<string, object>? resource = null);
}

public sealed class DeclarativePolicyEvaluator : IPolicyEvaluator
{
    public AuthorizationDecision Evaluate(PolicyDocument policy, SubjectClaims subject, string op, string? resourceType, IDictionary<string, object>? resource = null)
    {
        var context = new EvaluationContext(subject, op, resourceType, resource);

        foreach (var rule in policy.Rules)
        {
            if (!rule.Operations.Contains(op, StringComparer.OrdinalIgnoreCase))
                continue;
            if (EvaluateCondition(rule.When, context))
            {
                return new AuthorizationDecision(string.Equals(rule.Effect, PolicyEffect.Allow, StringComparison.OrdinalIgnoreCase) ? Decision.Allow : Decision.Deny, rule.Id);
            }
        }

        return string.Equals(policy.Default, "allow", StringComparison.OrdinalIgnoreCase)
            ? new AuthorizationDecision(Decision.Allow)
            : new AuthorizationDecision(Decision.Deny);
    }

    private static bool EvaluateCondition(Condition? condition, EvaluationContext context)
    {
        if (condition is null)
            return true;

        if (condition.All is { Length: > 0 })
        {
            foreach (var child in condition.All)
            {
                if (!EvaluateCondition(child, context))
                    return false;
            }
            return true;
        }

        if (condition.Any is { Length: > 0 })
        {
            foreach (var child in condition.Any)
            {
                if (EvaluateCondition(child, context))
                    return true;
            }
            return false;
        }

        if (condition.Field is { Count: > 0 })
        {
            foreach (var entry in condition.Field)
            {
                var (lhs, opSpec) = entry;
                if (!lhs.Contains('.'))
                    return false;

                var lhsValue = context.Resolve(lhs);
                foreach (var op in opSpec)
                {
                    if (op.Key.Equals("equals", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!EqualsString(lhsValue, op.Value)) return false;
                    }
                    else if (op.Key.Equals("equalsResource", StringComparison.OrdinalIgnoreCase))
                    {
                        var rhs = context.Resolve(op.Value);
                        if (!EqualsString(lhsValue, rhs)) return false;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool EqualsString(object? lhs, object? rhs)
        => string.Equals(Convert.ToString(lhs) ?? string.Empty, Convert.ToString(rhs) ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}

internal sealed class EvaluationContext
{
    private readonly SubjectClaims _subject;
    private readonly string _operation;
    private readonly string? _resourceType;
    private readonly IDictionary<string, object>? _resource;

    public EvaluationContext(SubjectClaims subject, string operation, string? resourceType, IDictionary<string, object>? resource)
    {
        _subject = subject;
        _operation = operation;
        _resourceType = resourceType;
        _resource = resource;
    }

    public object? Resolve(string path)
    {
        if (path.StartsWith("subject.", StringComparison.OrdinalIgnoreCase))
        {
            var key = path["subject.".Length..];
            return key.ToLowerInvariant() switch
            {
                "id" => _subject.Id,
                "mobile_verified" => _subject.MobileVerified,
                "mobile_hash" => _subject.MobileHash,
                _ => null
            };
        }

        if (path.StartsWith("resource.", StringComparison.OrdinalIgnoreCase))
        {
            var key = path["resource.".Length..];
            if (_resource is null) return null;
            _resource.TryGetValue(key, out var value);
            return value;
        }

        if (string.Equals(path, "operation", StringComparison.OrdinalIgnoreCase))
            return _operation;

        if (string.Equals(path, "resourceType", StringComparison.OrdinalIgnoreCase))
            return _resourceType;

        return null;
    }
}

public static class PolicySchema
{
    public static bool IsValid(PolicyDocument policy, out IReadOnlyList<string> errors)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(policy.Version)) list.Add("version is required.");
        if (string.IsNullOrWhiteSpace(policy.Default)) list.Add("default is required.");
        if (policy.Rules is null) list.Add("rules is required.");
        foreach (var rule in policy.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
                list.Add("rule.id is required.");
            if (string.IsNullOrWhiteSpace(rule.Effect))
                list.Add($"rule {rule.Id}: effect is required.");
            if (rule.Effect != "allow" && rule.Effect != "deny")
                list.Add($"rule {rule.Id}: effect must be allow or deny.");
        }
        errors = list;
        return list.Count == 0;
    }
}
