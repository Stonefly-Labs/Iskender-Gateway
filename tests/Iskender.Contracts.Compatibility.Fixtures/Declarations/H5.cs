using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

// H5: a second home for registry instances, outside ErrorCodes and ResourceTypes.
namespace Iskender.Contracts.Compatibility.Fixtures.Declarations.H5;

public static class SecondRegistry
{
    public static readonly ErrorCode Malformed = ErrorCodes.ResourceIdMalformed;

    public static ResourceType Organization => ResourceTypes.Organization;
}
