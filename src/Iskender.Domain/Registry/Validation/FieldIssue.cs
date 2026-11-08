using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>Collects field errors and orders them as the contract states (FR-018).</summary>
internal sealed class FieldIssues
{
    private readonly List<ConfigurationFieldError> _errors = [];

    public int Count => _errors.Count;

    public void Add(ErrorCode code, string path, ConfigurationSourceLocation location, ErrorText text) =>
        _errors.Add(new ConfigurationFieldError(code, path, location, text.Problem, text.Fix));

    public void Add(ConfigurationFieldError error) => _errors.Add(error);

    /// <summary>The first 100 errors in order, and the total.</summary>
    public (IReadOnlyList<ConfigurationFieldError> Reported, int Total) Truncated() =>
        (Ordered().Take(ConfigurationLimits.MaxReportedErrors).ToList(), _errors.Count);

    /// <summary>Every error ordered by layer, line, column, path, and code.</summary>
    public IReadOnlyList<ConfigurationFieldError> Ordered() =>
        _errors.OrderBy(error => error.Location.Layer)
            .ThenBy(error => error.Location.Line)
            .ThenBy(error => error.Location.Column)
            .ThenBy(error => error.Path, StringComparer.Ordinal)
            .ThenBy(error => error.Code.Id, StringComparer.Ordinal)
            .ToList();
}
