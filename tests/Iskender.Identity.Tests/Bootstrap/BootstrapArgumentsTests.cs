using Iskender.Management.Bootstrap;

namespace Iskender.Identity.Tests.Bootstrap;

/// <summary>The arguments after <c>bootstrap</c> (ISK-0010 T028, container contract Command line rules 1 and 2).</summary>
public class BootstrapArgumentsTests
{
    [Fact]
    public void ExactlyIssueOrRecoverParses()
    {
        Assert.Equal(BootstrapOperation.Issue, BootstrapArguments.Parse(["issue"]));
        Assert.Equal(BootstrapOperation.Recover, BootstrapArguments.Parse(["recover"]));
    }

    /// <summary>Space-separated arguments; <c>&lt;blank&gt;</c> is one empty argument.</summary>
    public static TheoryData<string> UsageErrors() => ["", "reissue", "Issue", "<blank>", "issue recover", "issue --force", "recover extra"];

    [Theory]
    [MemberData(nameof(UsageErrors))]
    public void AMissingUnknownOrExtraArgumentIsAUsageError(string args) =>
        Assert.Null(BootstrapArguments.Parse(args switch { "" => [], "<blank>" => [""], _ => args.Split(' ') }));

    [Fact]
    public void NullIsAUsageError() => Assert.Null(BootstrapArguments.Parse(null!));

    [Fact]
    public void EachOperationHasItsNameAndPurpose()
    {
        Assert.Equal(("issue", Domain.Identity.OperatorTokenPurpose.Bootstrap), (BootstrapOperation.Issue.ToName(), BootstrapOperation.Issue.ToPurpose()));
        Assert.Equal(("recover", Domain.Identity.OperatorTokenPurpose.Recovery), (BootstrapOperation.Recover.ToName(), BootstrapOperation.Recover.ToPurpose()));
    }
}
