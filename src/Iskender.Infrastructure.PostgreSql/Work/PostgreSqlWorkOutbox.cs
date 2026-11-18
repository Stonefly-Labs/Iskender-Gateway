using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Directory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>
/// The PostgreSQL <see cref="IWorkOutbox"/> (<c>work</c> contract The outbox and Work items, research R5): writes the item
/// and its scope's directory registration in the caller's unit, so both commit or roll back with the caller's own
/// change. Owned by ISK-0009.
/// </summary>
internal sealed class PostgreSqlWorkOutbox : IWorkOutbox
{
    /// <summary>The payload limit of Work items rule 2, fixed in every plane.</summary>
    public const int MaximumPayloadBytes = 65_536;

    /// <summary>The directory module name the outbox registers under.</summary>
    public const string DirectoryModule = "work";

    private const int MaximumTraceStateLength = 512;

    private const string InsertSql =
        "INSERT INTO isk_work.items (scope_key, item_id, module, kind, payload_version, payload, idempotency_key, bucket, state, due_at, trace_parent, trace_state) "
        + "VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 'pending', clock_timestamp() + $9::interval, $10, $11) "
        + "ON CONFLICT (scope_key, kind, idempotency_key) DO NOTHING RETURNING due_at";

    private const string ExistingSql = "SELECT item_id FROM isk_work.items WHERE {scope} AND kind = $2 AND idempotency_key = $3";

    // A property over the assembly's constant data, so no static field holds a writable array (hosting contract Static state).
    private static ReadOnlySpan<byte> BucketPrefix => "iskender.work.bucket.v1\0"u8;

    private readonly Dictionary<WorkKind, int> _highestVersions;
    private readonly ILogger _logger;

    public PostgreSqlWorkOutbox(IEnumerable<WorkKindDeclaration> declarations, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        _highestVersions = declarations.ToDictionary(declaration => declaration.Kind, declaration => declaration.HighestPayloadVersion);
        _logger = logger ?? NullLogger.Instance;
    }

    public async ValueTask<WorkEnqueueResult> EnqueueAsync(IUnitOfWork unit, WorkItemRequest request, CancellationToken cancellationToken)
    {
        if (unit is null || request is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var adapter = PostgreSqlUnitOfWork.From(unit);
        if (unit.State != UnitOfWorkState.Open)
        {
            throw new GatewayException(ErrorCodes.WorkUnitNotOpen);
        }

        if (!string.Equals(CoordinationKey.ScopeKey(request.Owner), CoordinationKey.ScopeKey(unit.Scope.Owner), StringComparison.Ordinal))
        {
            throw adapter.Fault(FailureClassifier.Raised(ErrorCodes.WorkScopeMismatch, PersistenceFailureClass.ScopeViolation));
        }

        if (!_highestVersions.TryGetValue(request.Kind, out var highest) || request.PayloadVersion > highest)
        {
            throw adapter.Fault(FailureClassifier.Raised(ErrorCodes.WorkKindUndeclared, PersistenceFailureClass.Unexpected));
        }

        if (request.Payload.Length > MaximumPayloadBytes)
        {
            throw adapter.Fault(FailureClassifier.Raised(ErrorCodes.WorkPayloadTooLarge, PersistenceFailureClass.Unexpected));
        }

        var itemId = Guid.CreateVersion7();
        var (traceParent, traceState) = TraceContext(Activity.Current);
        var inserted = await adapter.Command(WorkTables.Items, InsertSql)
            .With(itemId)
            .With(request.Kind.Module)
            .With(request.Kind.ToString())
            .With(request.PayloadVersion)
            .With(request.Payload.ToArray())
            .With(request.IdempotencyKey)
            .With((short)Bucket(itemId))
            .With(request.Delay)
            .With(traceParent)
            .With(traceState)
            .QueryAsync(reader => reader.GetFieldValue<DateTime>(0), cancellationToken);

        WorkEnqueueResult result;
        if (inserted.Count == 1)
        {
            await ScopeDirectory.RegisterAsync(unit, DirectoryModule, inserted[0], cancellationToken);
            result = new WorkEnqueueResult(itemId, Created: true);
        }
        else
        {
            var existing = await adapter.Command(WorkTables.Items, ExistingSql)
                .With(request.Kind.ToString())
                .With(request.IdempotencyKey)
                .QueryAsync(reader => reader.GetGuid(0), cancellationToken);
            if (existing.Count == 0)
            {
                throw adapter.Fault(FailureClassifier.Raised(ErrorCodes.PersistenceConcurrencyConflict, PersistenceFailureClass.TransientConflict));
            }

            result = new WorkEnqueueResult(existing[0], Created: false);
        }

        WorkLog.ItemEnqueued(_logger, request.Kind, result.Created);
        WorkTelemetry.RecordEnqueued(request.Kind, result.Created);
        return result;
    }

    /// <summary>
    /// The bucket of Work items rule 4: the low 6 bits of the first byte of SHA-256 over UTF-8
    /// <c>iskender.work.bucket.v1</c>, <c>0x00</c>, and the item ID's lowercase 36-character text.
    /// </summary>
    public static int Bucket(Guid itemId)
    {
        var text = Encoding.UTF8.GetBytes(itemId.ToString("D"));
        var input = new byte[BucketPrefix.Length + text.Length];
        BucketPrefix.CopyTo(input);
        text.CopyTo(input, BucketPrefix.Length);
        return SHA256.HashData(input)[0] & 0x3F;
    }

    // Tracing rule 1: the W3C traceparent of the current activity, and its tracestate when it is at most 512 characters.
    private static (string? Parent, string? State) TraceContext(Activity? activity)
    {
        if (activity is null || activity.IdFormat != ActivityIdFormat.W3C || activity.Id is not { } parent)
        {
            return (null, null);
        }

        var state = activity.TraceStateString;
        return (parent, state is { Length: > 0 and <= MaximumTraceStateLength } ? state : null);
    }
}
