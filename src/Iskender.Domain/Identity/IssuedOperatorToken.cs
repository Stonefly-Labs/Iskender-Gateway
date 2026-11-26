using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Identity;

/// <summary>
/// A token that has just been stored: its purpose, identifier, and expiry, and its text, which <see cref="Reveal"/>
/// returns exactly once (operator-security contract, Tokens rule 5). <see cref="ToString"/> never holds the secret.
/// Owned by ISK-0010.
/// </summary>
public sealed class IssuedOperatorToken
{
    private readonly OperatorTokenText _text;
    private int _revealed;

    /// <summary>
    /// A stored token, which superseded <paramref name="superseded"/> active tokens of its purpose in the same commit. A
    /// <c>null</c> text, a default expiry, or a negative count fails with <c>core.value.invalid</c>.
    /// </summary>
    public IssuedOperatorToken(OperatorTokenText text, DateTimeOffset expiresAt, int superseded = 0)
    {
        _text = text ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        ExpiresAt = expiresAt == default ? throw new GatewayException(ErrorCodes.ValueInvalid) : expiresAt;
        Superseded = superseded >= 0 ? superseded : throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    public OperatorTokenPurpose Purpose => _text.Purpose;

    public string TokenId => _text.TokenId;

    /// <summary>When the token expires, by the database's clock.</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>How many active tokens of the purpose its issue superseded, for event 9004 and the superseded metric.</summary>
    public int Superseded { get; }

    /// <summary>The full token text. A second call fails with <c>core.internal</c>, so the text is shown only once.</summary>
    public string Reveal() =>
        Interlocked.Exchange(ref _revealed, 1) == 0 ? _text.FullText() : throw new GatewayException(ErrorCodes.Internal);

    /// <summary><c>&lt;purpose&gt; token &lt;id&gt;</c>, never the secret.</summary>
    public override string ToString() => _text.ToString();
}
