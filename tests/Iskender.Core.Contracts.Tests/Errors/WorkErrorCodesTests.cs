using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Tests.Errors;

/// <summary>The eight <c>work.*</c> codes ISK-0009 adds in <c>core-contracts</c> 6 (work contract, Error codes).</summary>
public class WorkErrorCodesTests
{
    public static TheoryData<string, string, string> WorkCodes => new()
    {
        { "work.unit_not_open", "internal", "A work item could not be recorded." },
        { "work.scope_mismatch", "internal", "A work item could not be recorded in this scope." },
        { "work.kind_undeclared", "internal", "A work item of this kind cannot be recorded." },
        { "work.payload_too_large", "internal", "A work item is too large to be recorded." },
        { "work.payload_invalid", "internal", "A work item could not be processed." },
        { "work.handler_timeout", "deadline_exceeded", "A work item handler did not finish in time." },
        { "work.handler_cancelled", "cancelled", "A work item handler was cancelled." },
        { "work.attempt_abandoned", "internal", "A work item attempt did not finish." },
    };

    [Theory]
    [MemberData(nameof(WorkCodes))]
    public void EachCodeIsActiveSinceSixWithItsCategoryAndSafeMessage(string id, string category, string safeMessage)
    {
        Assert.True(ErrorCodes.TryGet(id, out var code));

        Assert.Equal((category, RegistryStatus.Active, 6, safeMessage), (code.Category.Id, code.Status, code.Since, code.SafeMessage));
    }

    [Theory]
    [MemberData(nameof(WorkCodes))]
    public void NoSafeMessageHoldsAPlaceholder(string id, string category, string safeMessage)
    {
        Assert.True(ErrorCodes.TryGet(id, out var code));

        Assert.NotEmpty(category);
        Assert.DoesNotContain('{', code.SafeMessage);
        Assert.DoesNotContain('}', code.SafeMessage);
        Assert.DoesNotContain('<', safeMessage);
    }

    [Fact]
    public void TheWorkCodesAreTheVersionSixCodesAndComeLast()
    {
        string[] expected =
        [
            "work.unit_not_open", "work.scope_mismatch", "work.kind_undeclared", "work.payload_too_large",
            "work.payload_invalid", "work.handler_timeout", "work.handler_cancelled", "work.attempt_abandoned",
        ];

        var versionSix = ErrorCodes.All.Where(code => code.Since == 6).ToList();

        Assert.Equal(expected, versionSix.Select(code => code.Id));
        Assert.Equal(versionSix, ErrorCodes.All.Skip(ErrorCodes.All.Count - versionSix.Count));
    }

    [Fact]
    public void EachWorkFieldIsTaggedWithVersionSix()
    {
        var fields = typeof(ErrorCodes).GetFields().Where(field => field.GetValue(null) is ErrorCode { Since: 6 }).ToList();

        Assert.Equal(8, fields.Count);
        Assert.All(fields, field =>
        {
            var tag = Assert.Single(field.GetCustomAttributes(typeof(ContractVersionAttribute), inherit: false).Cast<ContractVersionAttribute>());
            Assert.Equal(("core-contracts", 6), (tag.ContractId, tag.Since));
        });
    }
}
