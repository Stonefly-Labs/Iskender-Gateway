namespace Iskender.Samples.EmbeddedHost;

/// <summary>The sample's own service, reading <c>Sample:Greeting</c> from the host's configuration. Owned by ISK-0006.</summary>
public sealed class GreetingService(IConfiguration configuration)
{
    /// <summary>The configuration key the greeting is read from.</summary>
    public const string Key = "Sample:Greeting";

    public string Greeting => configuration[Key] ?? "";
}
