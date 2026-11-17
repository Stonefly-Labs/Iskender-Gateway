using Iskender.Management.Migrations;

namespace Iskender.Container.Tests.Migrations;

/// <summary>Container contract, Command line rules 1 and 2, and research R14: every accepted <c>migrate</c> form and every usage error.</summary>
public class MigrationArgumentsTests
{
    public static TheoryData<string[], MigrationOperation> Accepted => new()
    {
        { ["apply"], new MigrationOperation.Apply() },
        { ["status"], new MigrationOperation.Status() },
        { ["reverse"], new MigrationOperation.Reverse(1, false) },
        { ["reverse", "--steps", "3"], new MigrationOperation.Reverse(3, false) },
        { ["reverse", "--confirm-data-loss"], new MigrationOperation.Reverse(1, true) },
        { ["reverse", "--steps", "2", "--confirm-data-loss"], new MigrationOperation.Reverse(2, true) },
        { ["reverse", "--confirm-data-loss", "--steps", "2"], new MigrationOperation.Reverse(2, true) },
        { ["reverse", "--steps", "1"], new MigrationOperation.Reverse(1, false) },
        { ["reverse", "--steps", "2147483647"], new MigrationOperation.Reverse(int.MaxValue, false) },
    };

    // Each case's arguments, joined with "|", so an argument may hold a space.
    public static TheoryData<string> UsageErrors => new()
    {
        "",
        "migrate",
        "upgrade",
        "Apply",
        "apply|--steps|1",
        "apply|--confirm-data-loss",
        "status|--verbose",
        "reverse|--steps",
        "reverse|--steps|0",
        "reverse|--steps|-1",
        "reverse|--steps|+1",
        "reverse|--steps|two",
        "reverse|--steps|1.5",
        "reverse|--steps| 1",
        "reverse|--steps|2147483648",
        "reverse|--steps|1|--steps|2",
        "reverse|--confirm-data-loss|--confirm-data-loss",
        "reverse|--steps=2",
        "reverse|--force",
        "reverse|2",
        "apply|status",
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void AnAcceptedFormParsesToItsOperation(string[] args, MigrationOperation expected)
    {
        Assert.Equal(expected, MigrationArguments.Parse(args));
    }

    [Theory]
    [MemberData(nameof(UsageErrors))]
    public void AUsageErrorParsesToNull(string args)
    {
        Assert.Null(MigrationArguments.Parse(args.Length == 0 ? [] : args.Split('|')));
    }

    [Fact]
    public void EachOperationIsNamedAsEvent7006WritesIt()
    {
        Assert.Equal(["apply", "reverse", "status"], [new MigrationOperation.Apply().Name, new MigrationOperation.Reverse(1, false).Name, new MigrationOperation.Status().Name]);
    }
}
