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
    public void RegistryHoldsTheEighteenVersionTwoConfigurationCodesAfterTheVersionOneCodes()
    {
        (string Id, string Category)[] expected =
        [
            ("config.document.malformed", "invalid_request"),
            ("config.document.too_large", "invalid_request"),
            ("config.document.duplicate_key", "invalid_request"),
            ("config.document.unsupported_construct", "invalid_request"),
            ("config.namespace.unknown", "invalid_request"),
            ("config.field.unknown", "invalid_request"),
            ("config.field.required", "invalid_request"),
            ("config.field.type_mismatch", "invalid_request"),
            ("config.field.out_of_range", "invalid_request"),
            ("config.field.invalid_format", "invalid_request"),
            ("config.secret.plaintext", "invalid_request"),
            ("config.secret.reference_malformed", "invalid_request"),
            ("config.value.unsafe_for_environment", "invalid_request"),
            ("config.environment.invalid", "invalid_request"),
            ("config.overlay.conflict", "invalid_request"),
            ("config.schema.duplicate_namespace", "internal"),
            ("config.schema.invalid", "internal"),
            ("config.section.invalid_access", "internal"),
        ];

        var versionTwo = ErrorCodes.All.Where(code => code.Since == 2).ToList();

        Assert.Equal(expected, versionTwo.Select(code => (code.Id, code.Category.Id)));
        Assert.Equal(versionTwo, ErrorCodes.All.Skip(ErrorCodes.All.Count(code => code.Since == 1)).Take(versionTwo.Count));
        Assert.All(versionTwo, code => Assert.Equal(RegistryStatus.Active, code.Status));
    }

    [Fact]
    public void RegistryHoldsTheTwelveVersionThreePersistenceCodesAfterVersionTwo()
    {
        (string Id, string Category)[] expected =
        [
            ("persistence.unavailable", "internal"),
            ("persistence.timeout", "deadline_exceeded"),
            ("persistence.cancelled", "cancelled"),
            ("persistence.concurrency_conflict", "conflict"),
            ("persistence.unique_conflict", "conflict"),
            ("persistence.scope_required", "internal"),
            ("persistence.scope_not_permitted", "permission_denied"),
            ("persistence.lock_timeout", "conflict"),
            ("persistence.migration_locked", "internal"),
            ("persistence.migration_checksum_mismatch", "internal"),
            ("persistence.schema_incompatible", "internal"),
            ("persistence.reverse_requires_confirmation", "invalid_request"),
        ];

        var versionThree = ErrorCodes.All.Where(code => code.Since == 3).ToList();

        Assert.Equal(expected, versionThree.Select(code => (code.Id, code.Category.Id)));
        Assert.Equal(versionThree, ErrorCodes.All.Skip(ErrorCodes.All.Count(code => code.Since < 3)).Take(versionThree.Count));
        Assert.All(versionThree, code => Assert.Equal(RegistryStatus.Active, code.Status));
    }

    [Fact]
    public void RegistryHoldsTheSevenVersionFourCoordinationCodesLast()
    {
        (string Id, string Category, string SafeMessage)[] expected =
        [
            ("coordination.unavailable", "internal", "The coordination service is unavailable."),
            ("coordination.timeout", "deadline_exceeded", "The coordination service did not respond in time."),
            ("coordination.cancelled", "cancelled", "The coordination operation was cancelled."),
            ("coordination.lease_lost", "conflict", "The lease is no longer held."),
            ("coordination.fencing_rejected", "conflict", "A newer holder has already changed this resource."),
            ("coordination.capability_unsupported", "internal", "This feature is not available in the current deployment."),
            ("coordination.scope_required", "internal", "A coordination operation was attempted without an owner scope."),
        ];

        var versionFour = ErrorCodes.All.Where(code => code.Since == 4).ToList();

        Assert.Equal(expected, versionFour.Select(code => (code.Id, code.Category.Id, code.SafeMessage)));
        Assert.Equal(versionFour, ErrorCodes.All.Skip(ErrorCodes.All.Count - versionFour.Count));
        Assert.All(versionFour, code => Assert.Equal(RegistryStatus.Active, code.Status));
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
