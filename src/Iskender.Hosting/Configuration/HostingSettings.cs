using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Hosting.Configuration;

/// <summary>The validated <c>iskender.hosting</c> section, read once. Owned by ISK-0006.</summary>
internal sealed class HostingSettings
{
    private HostingSettings(ConfigurationSection section) => RequestTimeout = section.GetDuration("requestTimeout")!.Value;

    /// <summary>How long a request in Iskender's route group may run, from its start (hosting contract, Request bootstrap rule 4).</summary>
    public TimeSpan RequestTimeout { get; }

    /// <summary>Reads the section from a validated snapshot.</summary>
    public static HostingSettings From(ConfigurationSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        return new(snapshot.GetSection(HostingSectionSchema.Schema));
    }
}
