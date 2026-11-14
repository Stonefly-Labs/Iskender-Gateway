using System.Globalization;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Hosting.Composition;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Hosting.Configuration;

/// <summary>
/// The container's one validated configuration (research R6, R7, and R15; hosting contract, Start-up rules 1, 3, 4, and
/// 9). The first caller validates under a lock, and every later caller gets the stored snapshot or the same
/// <c>hosting.configuration_rejected</c> failure. A cancelled run stores nothing, so the next caller starts over.
/// Owned by ISK-0006.
/// </summary>
internal sealed class IskenderConfiguration : IValidatedConfiguration, IDisposable
{
    /// <summary>The logger category ISK-0003's events 3001 to 3003 are written under.</summary>
    public const string LoggerCategory = "Iskender.Configuration";

    private readonly IskenderRuntimeOptions _options;

    private readonly ConfigurationSchemaRegistry _registry;

    private readonly ILogger _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private ConfigurationSnapshot? _snapshot;

    private GatewayException? _rejection;

    public IskenderConfiguration(IskenderRuntimeOptions options, ConfigurationSchemaRegistry registry, ILoggerFactory? loggerFactory)
    {
        _options = options;
        _registry = registry;
        _logger = loggerFactory?.CreateLogger(LoggerCategory) ?? NullLogger.Instance;
    }

    /// <summary>Validates on first need, then returns the stored snapshot or throws the stored failure.</summary>
    public ConfigurationSnapshot Snapshot => Validate(CancellationToken.None);

    /// <summary>The revision of <see cref="Snapshot"/>.</summary>
    public ConfigurationRevision Revision => Snapshot.Revision;

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

    private ConfigurationSnapshot? Stored() => Volatile.Read(ref _rejection) is { } rejection ? throw rejection : Volatile.Read(ref _snapshot);

    private (ConfigurationLoadRequest? Load, GatewayException? Failure) Request(CancellationToken cancellationToken)
    {
        var baseDocument = Read(_options.BaseDocument, "base", cancellationToken);
        if (baseDocument.Failure is { } baseFailure)
        {
            return (null, baseFailure);
        }

        var overlayDocument = Read(_options.OverlayDocument, "overlay", cancellationToken);
        if (overlayDocument.Failure is { } overlayFailure)
        {
            return (null, overlayFailure);
        }

        var environment = _options.ReadEnvironmentVariables ? ConfigurationEnvironmentSource.FromProcess() : null;
        return (new ConfigurationLoadRequest(_registry, baseDocument.Source, overlayDocument.Source, environment, _options.EnvironmentName), null);
    }

    /// <summary>Reads one document through ISK-0003's bounded sources. A file, factory, or stream that fails is reported by layer and source name only.</summary>
    private static (ConfigurationDocumentSource? Source, GatewayException? Failure) Read(IskenderConfigurationDocument? document, string layer, CancellationToken cancellationToken)
    {
        if (document is null)
        {
            return (null, null);
        }

        var format = document.Format == IskenderDocumentFormat.Yaml ? ConfigurationDocumentFormat.Yaml : ConfigurationDocumentFormat.Json;
        try
        {
            if (document.Text is { } text)
            {
                return (ConfigurationDocumentSource.FromString(document.SourceName, format, text), null);
            }

            if (document.FilePath is { } path)
            {
                return (ConfigurationDocumentSource.FromFile(path, format, document.SourceName), null);
            }

            using var stream = document.Open!() ?? throw new InvalidOperationException();
            return (ConfigurationDocumentSource.FromStream(document.SourceName, format, stream), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return (null, Unreadable(layer, document.SourceName));
        }
    }

    private static GatewayException Rejected(ConfigurationValidationReport report) =>
        Failure(report.ToString(), report.TotalErrorCount, report.Errors.Count, []);

    private static GatewayException Unreadable(string layer, string sourceName) =>
        Failure(
            string.Create(CultureInfo.InvariantCulture, $"The {layer} configuration document \"{sourceName}\" could not be read."),
            1,
            1,
            [new("layer", layer), new("source", sourceName)]);

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
