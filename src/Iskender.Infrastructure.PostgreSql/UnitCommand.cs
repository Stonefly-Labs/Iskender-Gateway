using Npgsql;

namespace Iskender.Infrastructure.PostgreSql;

/// <summary>
/// One statement in a unit of work, with positional parameters only (<c>$1</c>, <c>$2</c>), so no value is ever written
/// into SQL text (research R2). It runs through the unit, which guards concurrent use, bounds it, and classifies its
/// failures. Owned by ISK-0004.
/// </summary>
internal sealed class UnitCommand
{
    private readonly PostgreSqlUnitOfWork _unit;
    private readonly string _sql;
    private readonly List<object?> _parameters = [];

    internal UnitCommand(PostgreSqlUnitOfWork unit, string sql)
    {
        _unit = unit;
        _sql = sql;
    }

    /// <summary>Adds the next positional parameter. A <c>null</c> value is sent as SQL <c>NULL</c>.</summary>
    public UnitCommand With(object? value)
    {
        _parameters.Add(value);
        return this;
    }

    /// <summary>Runs the statement and returns the number of rows it changed.</summary>
    public ValueTask<int> ExecuteAsync(CancellationToken cancellationToken) =>
        _unit.RunAsync((command, token) => command.ExecuteNonQueryAsync(token), this, FailureSite.Statement, cancellationToken);

    /// <summary>Runs the statement and returns the first column of the first row, or <c>default</c> when there is none.</summary>
    public ValueTask<T?> ScalarAsync<T>(CancellationToken cancellationToken) =>
        _unit.RunAsync(async (command, token) => await command.ExecuteScalarAsync(token) is { } value and not DBNull ? (T)value : default, this, FailureSite.Statement, cancellationToken);

    /// <summary>Runs the statement and maps every row.</summary>
    public ValueTask<IReadOnlyList<T>> QueryAsync<T>(Func<NpgsqlDataReader, T> map, CancellationToken cancellationToken) =>
        _unit.RunAsync<IReadOnlyList<T>>(
            async (command, token) =>
            {
                var rows = new List<T>();
                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    rows.Add(map(reader));
                }

                return rows;
            },
            this,
            FailureSite.Statement,
            cancellationToken);

    internal NpgsqlCommand Build(NpgsqlConnection connection, NpgsqlTransaction transaction, FailureSite site)
    {
        _ = site;
        var command = new NpgsqlCommand(_sql, connection, transaction);
        foreach (var value in _parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
        }

        return command;
    }
}
