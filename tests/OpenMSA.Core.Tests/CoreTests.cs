namespace OpenMSA.Core.Tests;

public class CoreTests
{
    [Fact]
    public void Mobile_hash_is_normalized_and_hashed()
    {
        var mobile = " +1 (555) 010-2030 ";
        var normalized = MobileHasher.Normalize(mobile);

        Assert.Equal("15550102030", normalized);

        var hash = MobileHasher.HashNormalized(normalized, "unit-test-key");
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[a-f0-9]{64}$", hash);
    }

    [Fact]
    public void Generates_distinct_opaque_ids()
    {
        var first = IdGenerator.NewId(IdSchemes.Space);
        var second = IdGenerator.NewId(IdSchemes.Space);

        Assert.StartsWith($"{IdSchemes.Space}_", first);
        Assert.StartsWith($"{IdSchemes.Space}_", second);
        Assert.NotEqual(first, second);
    }
}
