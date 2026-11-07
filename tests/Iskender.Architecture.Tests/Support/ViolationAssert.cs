namespace Iskender.Architecture.Tests.Support;

/// <summary>
/// Fails with every violation in full. xUnit shortens the items of a collection it prints, which would cut a message
/// off before its target and contract path, and FR-031 and SC-004 need all of them in one run.
/// </summary>
public static class ViolationAssert
{
    public static void None(IReadOnlyCollection<Violation> violations)
    {
        var report = string.Join('\n', violations.Select(violation => violation.Message));

        Assert.True(violations.Count == 0, $"{violations.Count} violation(s):\n{report}");
    }
}
