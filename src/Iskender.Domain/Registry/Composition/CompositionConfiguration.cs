using System.Globalization;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Domain.Registry.Composition;

/// <summary>
/// The container's one validated configuration (hosting contract, Start-up rules 1, 3, 4, and 9). The first caller
/// validates under a lock, and every later caller gets the stored snapshot or the same <c>hosting.configuration_rejected</c>
/// failure. A cancelled run stores nothing, so the next caller starts over. Owned by ISK-0006, in Hosting; moved here by
/// ISK-0007.
/// </summary>
public sealed class CompositionConfiguration : IValidatedConfiguration, IDisposable
{
    /// <summary>The logger category ISK-0003's events 3001 to 3003 are written under.</summary>
    public const string LoggerCategory = "Iskender.Configuration";

    private readonly CompositionSources _sources;

    private readonly ConfigurationSchemaRegistry _registry;

    private readonly ILogger _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private ConfigurationSnapshot? _snapshot;

    private GatewayException? _rejection;

    public CompositionConfiguration(CompositionSources sources, ConfigurationSchemaRegistry registry, ILoggerFactory? loggerFactory)
    {
        _sources = sources;
        _registry = registry;
        _logger = loggerFactory?.CreateLogger(LoggerCategory) ?? NullLogger.Instance;
    }

    /// <summary>Validates on first need, then returns the stored snapshot or throws the stored failure.</summary>
    public ConfigurationSnapshot Snapshot => Validate(CancellationToken.None);

    /// <summary>The revision of <see cref="Snapshot"/>.</summary>
    public ConfigurationRevision Revision => Snapshot.Revision;

    /// <summary>
    /// Validates <paramref name="sources"/> once against <paramref name="registry"/> with no logger, so it writes no event,
    /// and returns the snapshot, or <c>null</c> when the configuration is rejected or a document cannot be read. For a
    /// check that reads the configuration without composing a container, such as the container <c>health</c> command.
    /// </summary>
    public static ConfigurationSnapshot? Probe(CompositionSources sources, ConfigurationSchemaRegistry registry)
    {
        using var configuration = new CompositionConfiguration(sources, registry, loggerFactory: null);
        try
        {
            return configuration.Validate(CancellationToken.None);
        }
        catch (GatewayException)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs validation once. A rejected configuration, or a document that cannot be read, fails with
    /// <c>hosting.configuration_rejected</c>, now and on every later call. Cancellation before the outcome is stored
    /// throws <see cref="OperationCanceledException"/> and leaves the configuration not validated.
    /// </summary>
    public ConfigurationSnapshot Validate(CancellationToken cancellationToken)
    {
        if (Stored() is { } stored)
        {
            return stored;
        }

        _gate.Wait(cancellationToken);
        try
        {
            if (Stored() is { } raced)
            {
                return raced;
            }

            var request = Request(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Failure is { } unreadable)
            {
                Volatile.Write(ref _rejection, unreadable);
                throw unreadable;
            }

            var report = ConfigurationValidator.Validate(request.Load!, _logger);
            if (report.Snapshot is not { } snapshot)
            {
                var rejection = Rejected(report);
                Volatile.Write(ref _rejection, rejection);
                throw rejection;
            }

            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// A <c>hosting.configuration_rejected</c> failure for a <paramref name="layer"/> that cannot be used, naming the layer
    /// and source name and no content, counted as one error (hosting contract, Start-up rule 4).
    /// </summary>
    public static GatewayException Unreadable(string layer, string sourceName) =>
        Failure(
            string.Create(CultureInfo.InvariantCulture, $"The {layer} configuration document \"{sourceName}\" could not be read."),
            1,
            1,
            [new("layer", layer), new("source", sourceName)]);

    private ConfigurationSnapshot? Stored() => Volatile.Read(ref _rejection) is { } rejection ? throw rejection : Volatile.Read(ref _snapshot);

    private (ConfigurationLoadRequest? Load, GatewayException? Failure) Request(CancellationToken cancellationToken)
    {
        var baseDocument = Read(_sources.BaseDocument, "base", cancellationToken);
        if (baseDocument.Failure is { } baseFailure)
        {
            return (null, baseFailure);
        }

        var overlayDocument = Read(_sources.OverlayDocument, "overlay", cancellationToken);
        if (overlayDocument.Failure is { } overlayFailure)
        {
            return (null, overlayFailure);
        }

        var environment = _sources.ReadEnvironmentVariables ? ConfigurationEnvironmentSource.FromProcess() : null;
        return (new ConfigurationLoadRequest(_registry, baseDocument.Source, overlayDocument.Source, environment, _sources.EnvironmentName), null);
    }

    /// <summary>Reads one document through ISK-0003's bounded sources. A file, factory, or stream that fails is reported by layer and source name only.</summary>
    private static (ConfigurationDocumentSource? Source, GatewayException? Failure) Read(CompositionDocument? document, string layer, CancellationToken cancellationToken)
    {
        if (document is null)
        {
            return (null, null);
        }

        try
        {
            if (document.Text is { } text)
            {
                return (ConfigurationDocumentSource.FromString(document.SourceName, document.Format, text), null);
            }

            if (document.FilePath is { } path)
            {
                return (ConfigurationDocumentSource.FromFile(path, document.Format, document.SourceName), null);
            }

            using var stream = document.Open!() ?? throw new InvalidOperationException();
            return (ConfigurationDocumentSource.FromStream(document.SourceName, document.Format, stream), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return (null, Unreadable(layer, document.SourceName));
        }
    }

    private static GatewayException Rejected(ConfigurationValidationReport report) =>
        Failure(report.ToString(), report.TotalErrorCount, report.Errors.Count, []);

    private static GatewayException Failure(string text, int errorCount, int reportedCount, KeyValuePair<string, string>[] detail) =>
        new(GatewayError.Create(
            ErrorCodes.HostingConfigurationRejected,
            RequestCorrelationId.New(),
            OutputVisibility.None,
            diagnostics: new ErrorDiagnostics(
                text,
                [
                    new("errorCount", errorCount.ToString(CultureInfo.InvariantCulture)),
                    new("reportedCount", reportedCount.ToString(CultureInfo.InvariantCulture)),
                    .. detail,
                ])));
}
