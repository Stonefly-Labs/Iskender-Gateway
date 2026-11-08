using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Tests;

public class ErrorCodesTests
{
    private static readonly Regex CodePattern = new(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant);

    [Fact]
    public void CodesAreUnique()
    {
        Assert.Equal(ErrorCodes.All.Count, ErrorCodes.All.Select(code => code.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void CodesMatchThePattern()
    {
        Assert.All(ErrorCodes.All, code => Assert.Matches(CodePattern, code.Id));
    }

    [Fact]
    public void EachCodeBelongsToOneOfTheThirteenCategories()
    {
        Assert.All(ErrorCodes.All, code => Assert.Contains(code.Category, ErrorCategory.All));
    }

    [Fact]
    public void EachCodeHasAKnownStatusAndAFixedSafeMessage()
    {
        // A later version may add a code or retire one, so neither status nor since is pinned to version 1 here.
        Assert.All(ErrorCodes.All, code =>
        {
            Assert.True(Enum.IsDefined(code.Status));
            Assert.False(string.IsNullOrWhiteSpace(code.SafeMessage));
            Assert.False(string.IsNullOrWhiteSpace(code.Meaning));
        });
    }

    [Fact]
    public void RegistryHoldsTheTwentyTwoVersionOneCodesInContractOrderWithTheirCategories()
    {
        (string Id, string Category)[] expected =
        [
            ("core.resource_id.malformed", "invalid_request"),
            ("core.resource_id.unknown_type", "invalid_request"),
            ("core.resource_id.retired_type", "invalid_request"),
            ("core.resource_id.wrong_type", "invalid_request"),
            ("core.resource_id.non_canonical", "invalid_request"),
            ("core.resource.not_found", "not_found"),
            ("core.context.already_resolved", "internal"),
            ("core.context.not_resolved", "internal"),
            ("core.error.unregistered_code", "internal"),
            ("core.error.retired_code", "internal"),
            ("core.error.retry_not_permitted", "internal"),
            ("core.random_source_failed", "internal"),
            ("core.value.invalid", "invalid_request"),
            ("core.deadline_exceeded", "deadline_exceeded"),
            ("core.cancelled", "cancelled"),
            ("core.internal", "internal"),
            ("identity.unauthenticated", "unauthenticated"),
            ("identity.auth_denied", "permission_denied"),
            ("policy.model_disallowed", "permission_denied"),
            ("governance.quota_exceeded", "limit_exceeded"),
            ("governance.budget_blocked", "budget_blocked"),
            ("routing.no_healthy_backend", "upstream_unavailable"),
        ];

        var versionOne = ErrorCodes.All.Where(code => code.Since == 1).ToList();

        Assert.Equal(expected, versionOne.Select(code => (code.Id, code.Category.Id)));
        Assert.Equal(versionOne, ErrorCodes.All.Take(versionOne.Count));
    }

    [Fact]
    public void TheLastFiveVersionOneCodesCarryTheDocs08Reasons()
    {
        string[] reasons = ["auth_denied", "model_disallowed", "quota_exceeded", "budget_blocked", "no_healthy_backend"];

        Assert.Equal(reasons, ErrorCodes.All.Where(code => code.Since == 1).TakeLast(5).Select(code => code.Id[(code.Id.LastIndexOf('.') + 1)..]));
    }

    [Fact]
    public void TryGetIsOrdinal()
    {
        Assert.True(ErrorCodes.TryGet("core.value.invalid", out var found));
        Assert.Same(ErrorCodes.ValueInvalid, found);
        Assert.False(ErrorCodes.TryGet("CORE.VALUE.INVALID", out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void ValidationRejectsADuplicateId()
    {
        var duplicate = new ErrorCode("core.value.invalid", ErrorCategory.InvalidRequest, "m", "s", RegistryStatus.Active, 1);

        Assert.Throws<InvalidOperationException>(() => ErrorCodes.Validate([ErrorCodes.ValueInvalid, duplicate]));
    }

    [Theory]
    [InlineData("core")]
    [InlineData("Core.value")]
    [InlineData("core.Value")]
    [InlineData("core..value")]
    [InlineData("core.value.")]
    [InlineData("1core.value")]
    [InlineData("core-value.invalid")]
    public void ValidationRejectsAnIdOutsideThePattern(string id)
    {
        var code = new ErrorCode(id, ErrorCategory.InvalidRequest, "m", "s", RegistryStatus.Active, 1);

        Assert.Throws<InvalidOperationException>(() => ErrorCodes.Validate([code]));
    }

    [Fact]
    public void GatewayExceptionCarriesItsCode()
    {
        var exception = new GatewayException(ErrorCodes.ValueInvalid);

        Assert.Same(ErrorCodes.ValueInvalid, exception.Code);
    }
}
