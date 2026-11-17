namespace Iskender.Domain.Ports;

/// <summary>The plane a composition root composes (hosting contract, Ownership rule 2). Owned by ISK-0007.</summary>
public enum CompositionPlane
{
    /// <summary>The data plane, composed by <c>AddIskender(...)</c>.</summary>
    DataPlane,

    /// <summary>The control plane, composed by <c>AddIskenderControlPlane(...)</c>.</summary>
    ControlPlane,
}
