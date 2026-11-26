using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Identity;

/// <summary>What an operator token is for (operator-security contract, Tokens rule 1). Owned by ISK-0010.</summary>
public enum OperatorTokenPurpose
{
    /// <summary>Code <c>bootstrap</c>, prefix <c>iskb</c>: initializes the installation with its first operator.</summary>
    Bootstrap,

    /// <summary>Code <c>recovery</c>, prefix <c>iskr</c>: creates another operator on an initialized installation.</summary>
    Recovery,

    /// <summary>Code <c>enrollment</c>, prefix <c>iske</c>: attaches a principal's first sign-in method.</summary>
    Enrollment,
}

/// <summary>The stored codes and text prefixes of <see cref="OperatorTokenPurpose"/>. Owned by ISK-0010.</summary>
public static class OperatorTokenPurposes
{
    public static string ToCode(this OperatorTokenPurpose purpose) => purpose switch
    {
        OperatorTokenPurpose.Bootstrap => "bootstrap",
        OperatorTokenPurpose.Recovery => "recovery",
        OperatorTokenPurpose.Enrollment => "enrollment",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };

    /// <summary>The letter after <c>isk</c> in the token text: <c>b</c>, <c>r</c>, or <c>e</c>.</summary>
    public static char ToPrefixLetter(this OperatorTokenPurpose purpose) => purpose switch
    {
        OperatorTokenPurpose.Bootstrap => 'b',
        OperatorTokenPurpose.Recovery => 'r',
        OperatorTokenPurpose.Enrollment => 'e',
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };

    /// <summary>Reads a stored code. Anything else returns <c>false</c>.</summary>
    public static bool TryParse(string? code, out OperatorTokenPurpose purpose)
    {
        foreach (var candidate in Enum.GetValues<OperatorTokenPurpose>())
        {
            if (string.Equals(candidate.ToCode(), code, StringComparison.Ordinal))
            {
                purpose = candidate;
                return true;
            }
        }

        purpose = default;
        return false;
    }
}
