namespace Iskender.Domain.Registry.Composition;

/// <summary>
/// What a composition root reads its configuration from, copied once by the root and registered as one container
/// instance (hosting contract, Registration rules 1 and 2). Owned by ISK-0006, as Hosting's runtime options; moved here by
/// ISK-0007.
/// </summary>
public sealed record CompositionSources(
    CompositionDocument? BaseDocument,
    CompositionDocument? OverlayDocument,
    bool ReadEnvironmentVariables,
    string? EnvironmentName);
