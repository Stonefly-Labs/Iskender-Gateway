using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>A document error: its code and the location where reading stopped. Its path is always <c>(document)</c>.</summary>
internal sealed record DocumentError(ErrorCode Code, ConfigurationSourceLocation Location);

/// <summary>A document's tree, or the one error that stopped reading it.</summary>
internal sealed record DocumentRead(ObjectNode? Root, DocumentError? Error)
{
    public static DocumentRead Failed(ErrorCode code, ConfigurationSourceLocation location) => new(null, new DocumentError(code, location));
}

/// <summary>Thrown inside a reader to stop at the first document error.</summary>
internal sealed class DocumentErrorException(DocumentError error) : Exception
{
    public DocumentError Error { get; } = error;
}
