using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Management.Security;

/// <summary>The validated <c>iskender.operator</c> section, read once (operator-security contract, Configuration). Owned by ISK-0010.</summary>
public sealed class OperatorSettings
{
    private OperatorSettings(ConfigurationSection section)
    {
        var bootstrap = section.GetSection("bootstrap");
        TokenLifetime = bootstrap.GetDuration("tokenLifetime")!.Value;
        AttemptsPerMinute = (int)bootstrap.GetInt64("attemptsPerMinute")!.Value;
        EnrollmentCodeLifetime = section.GetSection("enrollment").GetDuration("codeLifetime")!.Value;
    }

    /// <summary>The bootstrap and recovery tokens' lifetime, <c>bootstrap.tokenLifetime</c>.</summary>
    public TimeSpan TokenLifetime { get; }

    /// <summary>Redemption attempts per control-plane host (service container) per minute, across both redemption routes, <c>bootstrap.attemptsPerMinute</c>.</summary>
    public int AttemptsPerMinute { get; }

    /// <summary>The enrollment codes' lifetime, <c>enrollment.codeLifetime</c>.</summary>
    public TimeSpan EnrollmentCodeLifetime { get; }

    /// <summary>Reads the section from a validated snapshot.</summary>
    public static OperatorSettings From(ConfigurationSnapshot snapshot) =>
        snapshot is null ? throw new GatewayException(ErrorCodes.ValueInvalid) : new(snapshot.GetSection(OperatorSectionSchema.Schema));
}
