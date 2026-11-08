using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Tests;

public class GatewayErrorTests
{
    private static readonly RequestCorrelationId Correlation = RequestCorrelationId.New();

    /// <summary>
    /// One code per category: the registered one where the registry has one, otherwise a test code. The registry has no
    /// code yet for <c>conflict</c>, <c>capability_unavailable</c>, or <c>upstream_failed</c>.
    /// </summary>
    internal static readonly IReadOnlyList<ErrorCode> CodePerCategory = ErrorCategory.All
        .Select(category => ErrorCodes.All.FirstOrDefault(code => ReferenceEquals(code.Category, category))
            ?? new ErrorCode($"test.{category.Id}", category, "Test code", "A test error occurred.", RegistryStatus.Active, 1))
        .ToArray();

    internal static readonly IReadOnlyList<ErrorCode> Registry = ErrorCodes.All.Union(CodePerCategory).ToArray();

    [Fact]
    public void CategoryAndSafeMessageComeFromTheCode()
    {
        var error = GatewayError.Create(ErrorCodes.IdentityAuthDenied, Correlation, OutputVisibility.None);

        Assert.Same(ErrorCodes.IdentityAuthDenied, error.Code);
        Assert.Same(ErrorCategory.PermissionDenied, error.Category);
        Assert.Equal("The caller is not allowed to perform this action.", error.SafeMessage);
        Assert.Same(Correlation, error.CorrelationId);
        Assert.Null(error.Retry);
        Assert.Null(error.Provider);
        Assert.True(error.Diagnostics.IsEmpty);
        Assert.Equal(RedactionStatus.None, error.Redaction);
    }

    // US3 scenario 5.
    [Fact]
    public void AnUnregisteredCodeFails()
    {
        var lookalike = new ErrorCode("core.internal", ErrorCategory.Internal, "m", "s", RegistryStatus.Active, 1);
        var unknown = new ErrorCode("module.unknown", ErrorCategory.Internal, "m", "s", RegistryStatus.Active, 1);

        Assert.Equal("core.error.unregistered_code", Assert.Throws<GatewayException>(() => GatewayError.Create(lookalike, Correlation, OutputVisibility.None)).Code.Id);
        Assert.Equal("core.error.unregistered_code", Assert.Throws<GatewayException>(() => GatewayError.Create(unknown, Correlation, OutputVisibility.None)).Code.Id);
        Assert.Equal("core.error.unregistered_code", Assert.Throws<GatewayException>(() => GatewayError.Create(null!, Correlation, OutputVisibility.None)).Code.Id);
    }

    [Fact]
    public void ARetiredCodeFails()
    {
        var retired = new ErrorCode("module.retired", ErrorCategory.Internal, "m", "s", RegistryStatus.Retired, 1);
        IReadOnlyList<ErrorCode> registry = [.. ErrorCodes.All, retired];

        var exception = Assert.Throws<GatewayException>(() => GatewayError.Create(registry, retired, Correlation, OutputVisibility.None));

        Assert.Equal("core.error.retired_code", exception.Code.Id);
    }

    [Fact]
    public void AMissingCorrelationOrUndefinedVisibilityFails()
    {
        Assert.Equal("core.value.invalid", Assert.Throws<GatewayException>(() => GatewayError.Create(ErrorCodes.Internal, null!, OutputVisibility.None)).Code.Id);
        Assert.Equal("core.value.invalid", Assert.Throws<GatewayException>(() => GatewayError.Create(ErrorCodes.Internal, Correlation, (OutputVisibility)7)).Code.Id);
    }

    public static TheoryData<string> Categories() => new(ErrorCategory.All.Select(category => category.Id));

    // Every category's retry rule, before any output.
    [Theory]
    [MemberData(nameof(Categories))]
    public void RetryAdviceIsAcceptedOnlyWhereTheCategoryRulePermits(string categoryId)
    {
        var code = CodePerCategory.Single(candidate => candidate.Category.Id == categoryId);
        var rule = code.Category.RetryRule;
        (RetryAdvice Advice, bool Permitted)[] cases =
        [
            (new RetryAdvice(), rule is RetryRule.BeforeOutputOnly),
            (new RetryAdvice(TimeSpan.FromSeconds(5)), rule is RetryRule.WhenResetKnown or RetryRule.BeforeOutputOnly),
            (new RetryAdvice(retriable: true), rule is RetryRule.BeforeOutputOnly or RetryRule.BeforeOutputWhenRetriable),
            (new RetryAdvice(TimeSpan.FromSeconds(5), retriable: true), rule is not RetryRule.Never),
        ];

        foreach (var (advice, permitted) in cases)
        {
            if (permitted)
            {
                Assert.Same(advice, GatewayError.Create(Registry, code, Correlation, OutputVisibility.None, advice).Retry);
            }
            else
            {
                var exception = Assert.Throws<GatewayException>(() => GatewayError.Create(Registry, code, Correlation, OutputVisibility.None, advice));
                Assert.Equal("core.error.retry_not_permitted", exception.Code.Id);
            }
        }

        Assert.Null(GatewayError.Create(Registry, code, Correlation, OutputVisibility.None).Retry);
    }

    // US3 scenario 3 and FR-025: after visible output, no advice in any category.
    [Theory]
    [MemberData(nameof(Categories))]
    public void NoRetryAdviceAfterVisibleOutput(string categoryId)
    {
        var code = CodePerCategory.Single(candidate => candidate.Category.Id == categoryId);
        RetryAdvice[] advice = [new(), new(TimeSpan.FromSeconds(5)), new(retriable: true), new(TimeSpan.FromSeconds(5), retriable: true)];

        foreach (var item in advice)
        {
            var exception = Assert.Throws<GatewayException>(() => GatewayError.Create(Registry, code, Correlation, OutputVisibility.Visible, item));
            Assert.Equal("core.error.retry_not_permitted", exception.Code.Id);
        }

        Assert.Null(GatewayError.Create(Registry, code, Correlation, OutputVisibility.Visible).Retry);
    }

    [Fact]
    public void NotFoundIsAlwaysWithheld()
    {
        var error = GatewayError.Create(ErrorCodes.ResourceNotFound, Correlation, OutputVisibility.None);

        Assert.True(error.Diagnostics.IsEmpty);
        Assert.Equal(RedactionStatus.Withheld, error.Redaction);
    }

    [Fact]
    public void DiagnosticsMakeAnErrorWithheld()
    {
        var withheld = GatewayError.Create(ErrorCodes.Internal, Correlation, OutputVisibility.None, diagnostics: new ErrorDiagnostics("stack trace"));
        var clear = GatewayError.Create(ErrorCodes.Internal, Correlation, OutputVisibility.None, diagnostics: new ErrorDiagnostics());

        Assert.Equal(RedactionStatus.Withheld, withheld.Redaction);
        Assert.Equal(RedactionStatus.None, clear.Redaction);
    }

    [Fact]
    public void AProviderCodeThatResemblesAGatewayCodeIsNeverPromoted()
    {
        var provider = new ProviderErrorMetadata("openai", providerErrorCode: "core.resource.not_found");

        var error = GatewayError.Create(ErrorCodes.RoutingNoHealthyBackend, Correlation, OutputVisibility.None, provider: provider);

        Assert.Same(ErrorCodes.RoutingNoHealthyBackend, error.Code);
        Assert.Equal("core.resource.not_found", error.Provider!.ProviderErrorCode);
    }

    [Fact]
    public void AGatewayExceptionCarriesItsError()
    {
        var error = GatewayError.Create(ErrorCodes.Cancelled, Correlation, OutputVisibility.None);

        var exception = new GatewayException(error);

        Assert.Same(error, exception.Error);
        Assert.Same(ErrorCodes.Cancelled, exception.Code);
    }
}
