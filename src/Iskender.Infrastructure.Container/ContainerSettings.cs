using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Infrastructure.Container;

/// <summary>The validated <c>iskender.container</c> section, read once (container contract, The <c>container</c> section). Owned by ISK-0007.</summary>
public sealed class ContainerSettings
{
    internal ContainerSettings(int trafficPort, int healthPort, TimeSpan shutdownDelay, TimeSpan shutdownTimeout, TimeSpan readinessCacheDuration)
    {
        TrafficPort = trafficPort;
        HealthPort = healthPort;
        ShutdownDelay = shutdownDelay;
        ShutdownTimeout = shutdownTimeout;
        ReadinessCacheDuration = readinessCacheDuration;
    }

    /// <summary>The port of the traffic listener.</summary>
    public int TrafficPort { get; }

    /// <summary>The port of the health listener.</summary>
    public int HealthPort { get; }

    /// <summary>How long requests keep being served after a stop signal, so load balancers can drain.</summary>
    public TimeSpan ShutdownDelay { get; }

    /// <summary>How long the server waits for requests in flight once it stops accepting connections.</summary>
    public TimeSpan ShutdownTimeout { get; }

    /// <summary>How long a readiness probe's result is reused.</summary>
    public TimeSpan ReadinessCacheDuration { get; }

    /// <summary>
    /// Reads a validated section. <c>trafficPort</c> equal to <c>healthPort</c> fails with <c>config.field.out_of_range</c>,
    /// naming <c>healthPort</c> in the error's diagnostics (research R9).
    /// </summary>
    public static ContainerSettings From(ConfigurationSection section)
    {
        if (section is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var trafficPort = (int)section.GetInt64("trafficPort")!.Value;
        var healthPort = (int)section.GetInt64("healthPort")!.Value;
        if (trafficPort == healthPort)
        {
            throw new GatewayException(GatewayError.Create(
                ErrorCodes.ConfigFieldOutOfRange,
                RequestCorrelationId.New(),
                OutputVisibility.None,
                diagnostics: new ErrorDiagnostics(values: [new("field", $"{section.Path}.healthPort")])));
        }

        return new(
            trafficPort,
            healthPort,
            section.GetDuration("shutdownDelay")!.Value,
            section.GetDuration("shutdownTimeout")!.Value,
            section.GetDuration("readinessCacheDuration")!.Value);
    }
}
