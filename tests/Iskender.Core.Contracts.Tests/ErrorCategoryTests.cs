using Iskender.Core.Contracts.Errors;

namespace Iskender.Core.Contracts.Tests;

public class ErrorCategoryTests
{
    [Fact]
    public void ThirteenCategoriesInContractOrderWithTheirRetryRules()
    {
        (string Id, RetryRule Rule)[] expected =
        [
            ("invalid_request", RetryRule.Never),
            ("unauthenticated", RetryRule.Never),
            ("permission_denied", RetryRule.Never),
            ("not_found", RetryRule.Never),
            ("conflict", RetryRule.Never),
            ("capability_unavailable", RetryRule.Never),
            ("limit_exceeded", RetryRule.WhenResetKnown),
            ("budget_blocked", RetryRule.Never),
            ("upstream_unavailable", RetryRule.BeforeOutputOnly),
            ("upstream_failed", RetryRule.BeforeOutputWhenRetriable),
            ("deadline_exceeded", RetryRule.Never),
            ("cancelled", RetryRule.Never),
            ("internal", RetryRule.Never),
        ];

        Assert.Equal(expected, ErrorCategory.All.Select(category => (category.Id, category.RetryRule)));
    }

    [Fact]
    public void RetryRuleValuesAreExplicit()
    {
        Assert.Equal([0, 1, 2, 3], Enum.GetValues<RetryRule>().Select(rule => (int)rule));
    }

    [Fact]
    public void CategoryHasNoPublicConstructor()
    {
        Assert.Empty(typeof(ErrorCategory).GetConstructors());
    }
}
