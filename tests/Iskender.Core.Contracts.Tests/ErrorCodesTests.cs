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
    public void EachCodeIsActiveSinceVersionOneWithAFixedSafeMessage()
    {
        Assert.All(ErrorCodes.All, code =>
        {
            Assert.Equal(RegistryStatus.Active, code.Status);
            Assert.Equal(1, code.Since);
            Assert.False(string.IsNullOrWhiteSpace(code.SafeMessage));
            Assert.False(string.IsNullOrWhiteSpace(code.Meaning));
        });
    }

    [Fact]
    public void RegistryHoldsThePhaseTwoCodes()
    {
        string[] expected =
        [
            "core.resource_id.malformed", "core.resource_id.unknown_type", "core.resource_id.retired_type",
            "core.resource_id.wrong_type", "core.resource_id.non_canonical", "core.random_source_failed", "core.value.invalid",
        ];

        Assert.All(expected, id => Assert.True(ErrorCodes.TryGet(id, out _), id));
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
