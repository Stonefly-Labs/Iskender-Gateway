using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Tests.Modules.Alpha;

internal sealed record WidgetNote(string Id, string OwnerOrganizationId, string OwnerProjectId, string WidgetId, string Body);

/// <summary>
/// The fixture repository for <c>isk_alpha.widget_notes</c>, a project-owned table that refers to a widget by ID. The
/// reference is checked through a read the unit's scope can see, so a foreign widget reads as not found, though the
/// foreign key alone would accept it.
/// </summary>
internal sealed class WidgetNoteRepository(bool ownerPredicate = true)
{
    private string Owner(int organization, int project) =>
        ownerPredicate ? $" AND owner_organization_id = ${organization} AND owner_project_id = ${project}" : "";

    /// <summary>Adds a note to a widget the scope can see, or fails with <c>core.resource.not_found</c>.</summary>
    public async Task AddAsync(IUnitOfWork unit, string id, string widgetId, string body, CancellationToken cancellationToken)
    {
        var widgetOwner = ownerPredicate ? " AND owner_organization_id = $2" : "";
        var added = await PostgreSqlUnitOfWork.From(unit).Command(
                AlphaTables.WidgetNotes,
                "INSERT INTO isk_alpha.widget_notes (id, owner_organization_id, owner_project_id, widget_id, body) " +
                $"SELECT $1, $2, $3, $4, $5 WHERE EXISTS (SELECT 1 FROM isk_alpha.widgets WHERE id = $4{widgetOwner})")
            .With(id).With(ScopeOwner.Organization(unit)).With(ScopeOwner.Project(unit)).With(widgetId).With(body)
            .ExecuteAsync(cancellationToken);
        if (added == 0)
        {
            throw new GatewayException(ErrorCodes.ResourceNotFound);
        }
    }

    public async Task<WidgetNote> GetAsync(IUnitOfWork unit, string id, CancellationToken cancellationToken)
    {
        var rows = await Owned(unit, PostgreSqlUnitOfWork.From(unit).Command(AlphaTables.WidgetNotes, $"SELECT {Columns} FROM isk_alpha.widget_notes WHERE id = $1" + Owner(2, 3)).With(id))
            .QueryAsync(Map, cancellationToken);
        return rows.Count == 1 ? rows[0] : throw new GatewayException(ErrorCodes.ResourceNotFound);
    }

    public async Task<IReadOnlyList<WidgetNote>> ListAsync(IUnitOfWork unit, CancellationToken cancellationToken) =>
        await Owned(unit, PostgreSqlUnitOfWork.From(unit).Command(AlphaTables.WidgetNotes, $"SELECT {Columns} FROM isk_alpha.widget_notes WHERE true" + Owner(1, 2) + " ORDER BY id"))
            .QueryAsync(Map, cancellationToken);

    public async Task<int> UpdateAsync(IUnitOfWork unit, string id, string body, CancellationToken cancellationToken) =>
        await Owned(unit, PostgreSqlUnitOfWork.From(unit).Command(AlphaTables.WidgetNotes, "UPDATE isk_alpha.widget_notes SET body = $2 WHERE id = $1" + Owner(3, 4)).With(id).With(body))
            .ExecuteAsync(cancellationToken);

    public async Task<int> DeleteAsync(IUnitOfWork unit, string id, CancellationToken cancellationToken) =>
        await Owned(unit, PostgreSqlUnitOfWork.From(unit).Command(AlphaTables.WidgetNotes, "DELETE FROM isk_alpha.widget_notes WHERE id = $1" + Owner(2, 3)).With(id))
            .ExecuteAsync(cancellationToken);

    private const string Columns = "id, owner_organization_id, owner_project_id, widget_id, body";

    private UnitCommand Owned(IUnitOfWork unit, UnitCommand command) =>
        ownerPredicate ? command.With(ScopeOwner.Organization(unit)).With(ScopeOwner.Project(unit)) : command;

    private static WidgetNote Map(Npgsql.NpgsqlDataReader row) => new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4));
}
