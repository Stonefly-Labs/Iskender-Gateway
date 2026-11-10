using Npgsql;

namespace Iskender.Infrastructure.PostgreSql;

/// <summary>Facts about the PostgreSQL adapter as a whole. Owned by ISK-0004.</summary>
internal static class PostgreSqlAdapter
{
    /// <summary>The name of the adapter's activity source and meter.</summary>
    public const string DiagnosticsName = "Iskender.Persistence";

    /// <summary>The driver the adapter is built on (research R1).</summary>
    internal static Type DriverType { get; } = typeof(NpgsqlDataSource);
}
