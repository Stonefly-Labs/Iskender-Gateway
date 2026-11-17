namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// Readiness bodies for the comparison tests (data-model Test fixtures): the body each plane answers before ISK-0021,
/// as T027 recorded it from running planes, and variants that each change only the fields their names say.
/// </summary>
internal static class RecordedReadiness
{
    /// <summary>The expected body, with status 503 (local-environment contract, Smoke assertions 3).</summary>
    public const string Expected =
        "{\"version\":1,\"status\":\"not_ready\",\"checks\":[" +
        "{\"name\":\"lifecycle\",\"status\":\"ready\",\"reason\":\"started\"}," +
        "{\"name\":\"persistence\",\"status\":\"not_ready\",\"reason\":\"authentication_failed\"}," +
        "{\"name\":\"coordination\",\"status\":\"ready\",\"reason\":\"ok\"}," +
        "{\"name\":\"single_instance\",\"status\":\"ready\",\"reason\":\"not_applicable\"}," +
        "{\"name\":\"capability\",\"status\":\"ready\",\"reason\":\"none_refused\"}]," +
        "\"refused\":[]}";

    /// <summary>Persistence ready with <c>ok</c>, so the whole answer is ready.</summary>
    public static string PersistenceOk { get; } = Expected
        .Replace("{\"version\":1,\"status\":\"not_ready\"", "{\"version\":1,\"status\":\"ready\"", StringComparison.Ordinal)
        .Replace("{\"name\":\"persistence\",\"status\":\"not_ready\",\"reason\":\"authentication_failed\"}", "{\"name\":\"persistence\",\"status\":\"ready\",\"reason\":\"ok\"}", StringComparison.Ordinal);

    /// <summary>Coordination not ready with <c>unreachable</c>.</summary>
    public static string CoordinationUnreachable { get; } = Expected
        .Replace("{\"name\":\"coordination\",\"status\":\"ready\",\"reason\":\"ok\"}", "{\"name\":\"coordination\",\"status\":\"not_ready\",\"reason\":\"unreachable\"}", StringComparison.Ordinal);

    /// <summary>Lifecycle not ready with <c>starting</c>.</summary>
    public static string Starting { get; } = Expected
        .Replace("{\"name\":\"lifecycle\",\"status\":\"ready\",\"reason\":\"started\"}", "{\"name\":\"lifecycle\",\"status\":\"not_ready\",\"reason\":\"starting\"}", StringComparison.Ordinal);

    /// <summary>A non-empty <c>refused</c> list.</summary>
    public static string Refused { get; } = Expected.Replace("\"refused\":[]", "\"refused\":[\"conversation.durable\"]", StringComparison.Ordinal);
}
