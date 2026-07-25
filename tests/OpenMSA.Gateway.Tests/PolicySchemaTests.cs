using OpenMSA.Policy;
using OpenMSA.Core;

namespace OpenMSA.Gateway.Tests;

public class PolicySchemaTests
{
    [Fact]
    public void Missing_rules_are_invalid()
    {
        var policy = new PolicyDocument("1.0", "deny", null!);

        var valid = PolicySchema.IsValid(policy, out var errors);

        Assert.False(valid);
        Assert.Contains("rules is required.", errors);
    }

    [Fact]
    public void Invalid_effect_is_rejected()
    {
        var policy = new PolicyDocument(
            "1.0",
            "deny",
            [new PolicyRule("bad", "invalid", ["read"])]);

        var valid = PolicySchema.IsValid(policy, out var errors);

        Assert.False(valid);
        Assert.Contains("rule bad: effect must be allow or deny.", errors);
    }

    [Fact]
    public void Evaluator_denies_when_no_rules_match()
    {
        var policy = new PolicyDocument(
            "1.0",
            "deny",
            [
                new PolicyRule(
                    "owner-match",
                    "allow",
                    ["read"],
                    new Condition(
                        new Dictionary<string, Dictionary<string, string>>
                        {
                            ["subject.id"] = new() { ["equals"] = "owner" }
                        },
                        null,
                        null))
            ]);

        var evaluator = new DeclarativePolicyEvaluator();
        var decision = evaluator.Evaluate(
            policy,
            new SubjectClaims("not-owner"),
            "read",
            "SalesBill",
            new Dictionary<string, object>());

        Assert.False(decision.IsAllowed);
    }
}
