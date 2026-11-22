using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Tests.Errors;

/// <summary>The eleven <c>operator.*</c> codes and the <c>principal</c> resource type ISK-0010 adds in <c>core-contracts</c> 7 (research R17).</summary>
public class OperatorErrorCodesTests
{
    public static TheoryData<string, string, string> OperatorCodes => new()
    {
        { "operator.redemption_failed", "permission_denied", "The token was not accepted." },
        { "operator.redemption_limited", "limit_exceeded", "Too many redemption attempts; try again later." },
        { "operator.uninitialized", "permission_denied", "The installation has not been initialized." },
        { "operator.state_unavailable", "internal", "The installation state cannot be read." },
        { "operator.authorization_unavailable", "internal", "The authorization decision cannot be made." },
        { "operator.already_initialized", "conflict", "The installation is already initialized." },
        { "operator.not_initialized", "conflict", "The installation is not initialized." },
        { "operator.route_undeclared", "internal", "An admin route declares no requirement." },
        { "operator.transport_unprotected", "invalid_request", "The traffic listener has no transport protection." },
        { "operator.certificate_unusable", "invalid_request", "The TLS certificate or key cannot be used." },
        { "operator.enrollment_invalid", "permission_denied", "The enrollment code was not accepted." },
    };

    [Theory]
    [MemberData(nameof(OperatorCodes))]
    public void EachCodeIsActiveSinceSevenWithItsCategoryAndSafeMessage(string id, string category, string safeMessage)
    {
        Assert.True(ErrorCodes.TryGet(id, out var code));

        Assert.Equal((category, RegistryStatus.Active, 7, safeMessage), (code.Category.Id, code.Status, code.Since, code.SafeMessage));
    }

    [Theory]
    [MemberData(nameof(OperatorCodes))]
    public void NoSafeMessageHoldsAPlaceholder(string id, string category, string safeMessage)
    {
        Assert.True(ErrorCodes.TryGet(id, out var code));

        Assert.NotEmpty(category);
        Assert.DoesNotContain('{', code.SafeMessage);
        Assert.DoesNotContain('}', code.SafeMessage);
        Assert.DoesNotContain('<', safeMessage);
    }

    [Fact]
    public void TheOperatorCodesAreTheVersionSevenCodesAndComeLast()
    {
        string[] expected =
        [
            "operator.redemption_failed", "operator.redemption_limited", "operator.uninitialized", "operator.state_unavailable",
            "operator.authorization_unavailable", "operator.already_initialized", "operator.not_initialized",
            "operator.route_undeclared", "operator.transport_unprotected", "operator.certificate_unusable",
            "operator.enrollment_invalid",
        ];

        var versionSeven = ErrorCodes.All.Where(code => code.Since == 7).ToList();

        Assert.Equal(expected, versionSeven.Select(code => code.Id));
        Assert.Equal(versionSeven, ErrorCodes.All.Skip(ErrorCodes.All.Count - versionSeven.Count));
    }

    [Fact]
    public void EachOperatorFieldIsTaggedWithVersionSeven()
    {
        var fields = typeof(ErrorCodes).GetFields().Where(field => field.GetValue(null) is ErrorCode { Since: 7 }).ToList();

        Assert.Equal(11, fields.Count);
        Assert.All(fields, field =>
        {
            var tag = Assert.Single(field.GetCustomAttributes(typeof(ContractVersionAttribute), inherit: false).Cast<ContractVersionAttribute>());
            Assert.Equal(("core-contracts", 7), (tag.ContractId, tag.Since));
        });
    }

    [Fact]
    public void ThePrincipalTypeIsActiveSinceSevenAndComesLast()
    {
        Assert.Equal(("principal", "prin", RegistryStatus.Active, 7), (ResourceTypes.Principal.Name, ResourceTypes.Principal.Prefix, ResourceTypes.Principal.Status, ResourceTypes.Principal.Since));
        Assert.Same(ResourceTypes.Principal, ResourceTypes.All[^1]);
        Assert.True(ResourceTypes.TryGetByPrefix("prin", out var found));
        Assert.Same(ResourceTypes.Principal, found);

        var tag = Assert.Single(typeof(ResourceTypes).GetField(nameof(ResourceTypes.Principal))!.GetCustomAttributes(typeof(ContractVersionAttribute), inherit: false).Cast<ContractVersionAttribute>());
        Assert.Equal(("core-contracts", 7), (tag.ContractId, tag.Since));
    }
}
