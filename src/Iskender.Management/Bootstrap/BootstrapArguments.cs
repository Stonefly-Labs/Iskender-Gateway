namespace Iskender.Management.Bootstrap;

/// <summary>
/// Parses the arguments after <c>bootstrap</c> (container contract, Command line rules 1 and 2, Bootstrap command;
/// research R14). Owned by ISK-0010.
/// </summary>
public static class BootstrapArguments
{
    /// <summary>
    /// The operation <paramref name="args"/> names: <see cref="BootstrapOperation.Issue"/> for exactly <c>issue</c>,
    /// <see cref="BootstrapOperation.Recover"/> for exactly <c>recover</c>, and otherwise <c>null</c>, a usage error: no
    /// argument, another argument, or more than one.
    /// </summary>
    public static BootstrapOperation? Parse(IReadOnlyList<string> args) =>
        args is not { Count: 1 } ? null : args[0] switch
        {
            "issue" => BootstrapOperation.Issue,
            "recover" => BootstrapOperation.Recover,
            _ => null,
        };
}
