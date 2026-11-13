using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class CoordinationNameTests
{
    public static TheoryData<string, string> Valid => new()
    {
        { "a", "b" },
        { "a" + new string('9', 30), "b" + new string('.', 62) },
        { "quota_2", "window.month_1" },
    };

    public static TheoryData<string?, string?> Invalid => new()
    {
        { null, "name" },
        { "module", null },
        { "", "name" },
        { "module", "" },
        { "a" + new string('9', 31), "name" },
        { "module", "b" + new string('.', 63) },
        { "Module", "name" },
        { "module", "Name" },
        { "1module", "name" },
        { "module", "1name" },
        { "_module", "name" },
        { "module", ".name" },
        { "mod.ule", "name" },
        { "mod-ule", "name" },
        { "module", "na-me" },
        { "module", "name\n" },
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void ValidModulesAndNamesAreAccepted(string module, string name)
    {
        var coordination = new CoordinationName(module, name);
        var cache = new CacheNamespace(module, name);

        Assert.Equal((module, name), (coordination.Module, coordination.Name));
        Assert.Equal((module, name), (cache.Module, cache.Name));
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void InvalidModulesAndNamesAreRejected(string? module, string? name)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CoordinationName(module!, name!)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CacheNamespace(module!, name!)).Code);
    }

    [Fact]
    public void NamesHaveValueEquality()
    {
        Assert.Equal(new CoordinationName("quota", "monthly"), new CoordinationName("quota", "monthly"));
        Assert.NotEqual(new CoordinationName("quota", "monthly"), new CoordinationName("quota", "daily"));
        Assert.NotEqual(new CoordinationName("quota", "monthly"), new CoordinationName("rate", "monthly"));
        Assert.Equal(new CacheNamespace("keys", "api"), new CacheNamespace("keys", "api"));
        Assert.NotEqual(new CacheNamespace("keys", "api"), new CacheNamespace("keys", "admin"));
    }
}
