using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>One migration: its owning module and its positive version. Owned by ISK-0004.</summary>
public sealed record MigrationIdentity
{
    /// <summary>An invalid module name or a version below 1 fails with <c>core.value.invalid</c>.</summary>
    public MigrationIdentity(string module, int version)
    {
        Module = PortNames.RequireModule(module);
        Version = version > 0 ? version : throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    public string Module { get; }

    public int Version { get; }

    public override string ToString() => $"{Module}/{Version}";
}
