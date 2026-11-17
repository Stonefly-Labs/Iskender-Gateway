using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// The one composition root registered in a container: the plane it composes and its root module (hosting contract,
/// Ownership rule 2 and Registration rules 7 and 8; research R2). Owned by ISK-0007.
/// </summary>
public sealed record CompositionRoot
{
    /// <summary>The requirement name either root meets, kept from version 1 of the hosting contract.</summary>
    public const string Requirement = "hosting";

    /// <summary><paramref name="module"/> follows <see cref="CompositionModule"/>'s name rule; otherwise, or for an undefined plane, fails with <c>core.value.invalid</c>.</summary>
    public CompositionRoot(CompositionPlane plane, string module)
    {
        if (!Enum.IsDefined(plane) || !CompositionModule.IsName(module))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Plane = plane;
        Module = module;
    }

    public CompositionPlane Plane { get; }

    /// <summary>The root's own module, <c>hosting</c> or <c>control-plane</c>.</summary>
    public string Module { get; }
}
