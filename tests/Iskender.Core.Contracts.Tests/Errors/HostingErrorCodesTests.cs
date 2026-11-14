using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Tests.Errors;

/// <summary>The three <c>hosting.*</c> codes ISK-0006 adds in <c>core-contracts</c> 5 (hosting contract, Error codes).</summary>
public class HostingErrorCodesTests
{
    public static TheoryData<string, string> HostingCodes => new()
    {
        { "hosting.configuration_rejected", "Iskender could not start because its configuration was rejected." },
        { "hosting.registration_duplicate", "An Iskender registration was added more than once." },
        { "hosting.registration_missing", "A required Iskender registration is missing." },
    };

    [Theory]
    [MemberData(nameof(HostingCodes))]
    public void EachCodeIsActiveInternalSinceFiveWithItsSafeMessage(string id, string safeMessage)
    {
        Assert.True(ErrorCodes.TryGet(id, out var code));

        Assert.Equal((ErrorCategory.Internal, RegistryStatus.Active, 5, safeMessage), (code.Category, code.Status, code.Since, code.SafeMessage));
    }

    [Theory]
    [MemberData(nameof(HostingCodes))]
    public void NoSafeMessageHoldsAPlaceholder(string id, string safeMessage)
    {
        Assert.True(ErrorCodes.TryGet(id, out var code));

        Assert.DoesNotContain('{', code.SafeMessage);
        Assert.DoesNotContain('}', code.SafeMessage);
        Assert.DoesNotContain('<', safeMessage);
    }

    [Fact]
    public void TheHostingCodesAreTheVersionFiveCodesAndComeLast()
    {
        string[] expected = ["hosting.configuration_rejected", "hosting.registration_duplicate", "hosting.registration_missing"];

        var versionFive = ErrorCodes.All.Where(code => code.Since == 5).ToList();

        Assert.Equal(expected, versionFive.Select(code => code.Id));
        Assert.Equal(versionFive, ErrorCodes.All.Skip(ErrorCodes.All.Count - versionFive.Count));
    }

    [Fact]
    public void EachHostingFieldIsTaggedWithVersionFive()
    {
        var fields = typeof(ErrorCodes).GetFields().Where(field => field.GetValue(null) is ErrorCode { Since: 5 }).ToList();

        Assert.Equal(3, fields.Count);
        Assert.All(fields, field =>
        {
            var tag = Assert.Single(field.GetCustomAttributes(typeof(ContractVersionAttribute), inherit: false).Cast<ContractVersionAttribute>());
            Assert.Equal(("core-contracts", 5), (tag.ContractId, tag.Since));
        });
    }
}
