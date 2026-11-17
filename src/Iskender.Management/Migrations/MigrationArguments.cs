using System.Globalization;

namespace Iskender.Management.Migrations;

/// <summary>
/// Parses the arguments after <c>migrate</c> (container contract, Command line rules 1 and 2; research R14). Owned by
/// ISK-0007.
/// </summary>
public static class MigrationArguments
{
    private const string Steps = "--steps";

    private const string ConfirmDataLoss = "--confirm-data-loss";

    /// <summary>
    /// The operation <paramref name="args"/> names, or <c>null</c> for a usage error: no operation, an unknown operation or
    /// option, an option given twice, an option after <c>apply</c> or <c>status</c>, or a <c>--steps</c> value that is not
    /// an integer from 1 to 2,147,483,647. <c>reverse</c> alone is <c>Reverse(1, false)</c>.
    /// </summary>
    public static MigrationOperation? Parse(IReadOnlyList<string> args)
    {
        if (args is null || args.Count == 0)
        {
            return null;
        }

        return args[0] switch
        {
            "apply" when args.Count == 1 => new MigrationOperation.Apply(),
            "status" when args.Count == 1 => new MigrationOperation.Status(),
            "reverse" => Reverse([.. args.Skip(1)]),
            _ => null,
        };
    }

    private static MigrationOperation.Reverse? Reverse(IReadOnlyList<string> options)
    {
        int? steps = null;
        var confirm = false;
        for (var index = 0; index < options.Count; index++)
        {
            switch (options[index])
            {
                case Steps when steps is null && index + 1 < options.Count && Count(options[index + 1]) is { } count:
                    steps = count;
                    index++;
                    break;
                case ConfirmDataLoss when !confirm:
                    confirm = true;
                    break;
                default:
                    return null;
            }
        }

        return new MigrationOperation.Reverse(steps ?? 1, confirm);
    }

    // Digits only, so a sign, white space, or a group separator is a usage error.
    private static int? Count(string value) =>
        value.Length > 0 && value.All(char.IsAsciiDigit) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 1 ? count : null;
}
