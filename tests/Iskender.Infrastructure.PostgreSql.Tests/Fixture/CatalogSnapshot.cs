using System.Text;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Fixture;

/// <summary>
/// A deterministic text rendering of the objects in every <c>isk_*</c> schema except <c>isk_migrations</c>, read from
/// <c>pg_catalog</c> (research R20): relations with their kind, row-level security flags, and grants; columns with type,
/// nullability, and default; constraints; indexes; and policies with their expressions, each sorted by name. Columns are
/// sorted by name, not position, so a column dropped and added again renders the same. The schemas themselves are left
/// out, because the runner never drops one, so reversing a module's first migration matches the state before it.
/// </summary>
internal static class CatalogSnapshot
{
    private const string Schemas = "n.nspname LIKE 'isk\\_%' AND n.nspname <> 'isk_migrations'";

    private static readonly string[] Queries =
    [
        $"""
        SELECT format('relation %s.%s kind=%s rls=%s force=%s owner=%s acl=%s', n.nspname, c.relname, c.relkind, c.relrowsecurity, c.relforcerowsecurity,
            pg_get_userbyid(c.relowner), coalesce(array_to_string(ARRAY(SELECT a::text FROM unnest(c.relacl) a ORDER BY a::text COLLATE "C"), ','), '-'))
        FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE {Schemas} AND c.relkind IN ('r', 'p', 'v', 'm', 'S', 'f')
        ORDER BY n.nspname COLLATE "C", c.relname COLLATE "C"
        """,
        $"""
        SELECT format('column %s.%s.%s %s notnull=%s default=%s', n.nspname, c.relname, a.attname, format_type(a.atttypid, a.atttypmod), a.attnotnull,
            coalesce(pg_get_expr(d.adbin, d.adrelid), '-'))
        FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
        LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
        WHERE {Schemas} AND c.relkind IN ('r', 'p', 'v', 'm', 'f') AND a.attnum > 0 AND NOT a.attisdropped
        ORDER BY n.nspname COLLATE "C", c.relname COLLATE "C", a.attname COLLATE "C"
        """,
        $"""
        SELECT format('constraint %s.%s.%s %s', n.nspname, c.relname, k.conname, pg_get_constraintdef(k.oid))
        FROM pg_constraint k JOIN pg_class c ON c.oid = k.conrelid JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE {Schemas}
        ORDER BY n.nspname COLLATE "C", c.relname COLLATE "C", k.conname COLLATE "C"
        """,
        $"""
        SELECT format('index %s.%s %s', n.nspname, i.relname, pg_get_indexdef(i.oid))
        FROM pg_index x JOIN pg_class i ON i.oid = x.indexrelid JOIN pg_namespace n ON n.oid = i.relnamespace
        WHERE {Schemas}
        ORDER BY n.nspname COLLATE "C", i.relname COLLATE "C"
        """,
        $"""
        SELECT format('policy %s.%s.%s cmd=%s permissive=%s roles=%s using=%s check=%s', n.nspname, c.relname, p.polname, p.polcmd, p.polpermissive,
            array_to_string(ARRAY(SELECT x.name FROM (SELECT CASE WHEN r = 0 THEN 'public' ELSE pg_get_userbyid(r)::text END AS name FROM unnest(p.polroles) r) x ORDER BY x.name COLLATE "C"), ','),
            coalesce(pg_get_expr(p.polqual, p.polrelid), '-'), coalesce(pg_get_expr(p.polwithcheck, p.polrelid), '-'))
        FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE {Schemas}
        ORDER BY n.nspname COLLATE "C", c.relname COLLATE "C", p.polname COLLATE "C"
        """,
    ];

    /// <summary>The snapshot of <paramref name="database"/>, one line per object, each ending in LF.</summary>
    public static async Task<string> RenderAsync(PostgreSqlFixture fixture, string database)
    {
        await using var connection = await fixture.OpenSuperuserAsync(database);
        var text = new StringBuilder();
        foreach (var query in Queries)
        {
            await using var command = new NpgsqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                text.Append(reader.GetString(0)).Append('\n');
            }
        }

        return text.ToString();
    }
}
