using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.PostgreSql.Migrations;

/// <summary>
/// A release's validated, ordered migrations (contract Migrations rules 4 and 5, research R11). It refuses, before
/// anything is applied, a missing reverse script, a duplicate version within a module, an unknown dependency, a
/// dependency cycle, invalid UTF-8, and a script naming another <c>isk_</c> schema or <c>public</c>, each with
/// <c>persistence.schema_incompatible</c> and its safe reason. Modules are ordered by dependency, ties by ordinal name,
/// then versions ascending; reverse order is the exact opposite. Immutable. Owned by ISK-0004.
/// </summary>
public sealed class MigrationSet
{
    private readonly Dictionary<string, MigrationModule> _modules;
    private readonly Dictionary<(string Module, int Version), MigrationScript> _scripts;

    /// <summary>Two modules with one name fail with <c>core.value.invalid</c>; any other refusal also logs event 4012.</summary>
    public MigrationSet(IEnumerable<MigrationModule> modules, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        var declared = (modules ?? throw new GatewayException(ErrorCodes.ValueInvalid)).ToList();
        if (declared.Any(module => module is null) || declared.DistinctBy(module => module.Name, StringComparer.Ordinal).Count() != declared.Count)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        _modules = declared.ToDictionary(module => module.Name, StringComparer.Ordinal);
        Modules = Order(declared, _modules, logger);

        var scripts = new List<MigrationScript>();
        foreach (var module in Modules)
        {
            scripts.AddRange(Scripts(module, logger));
        }

        Migrations = scripts;
        Reversed = [.. Enumerable.Reverse(scripts)];
        _scripts = scripts.ToDictionary(script => (script.Identity.Module, script.Identity.Version));
    }

    /// <summary>The modules in dependency order, ties broken by ordinal name.</summary>
    public IReadOnlyList<MigrationModule> Modules { get; }

    /// <summary>Every migration in global forward order.</summary>
    public IReadOnlyList<MigrationScript> Migrations { get; }

    /// <summary>Every migration in exact reverse order.</summary>
    public IReadOnlyList<MigrationScript> Reversed { get; }

    internal bool TryGetModule(string name, out MigrationModule module) => _modules.TryGetValue(name, out module!);

    internal bool TryGetScript(string module, int version, out MigrationScript script) => _scripts.TryGetValue((module, version), out script!);

    // Kahn's algorithm, always taking the ordinally smallest ready module.
    private static List<MigrationModule> Order(List<MigrationModule> declared, Dictionary<string, MigrationModule> byName, ILogger logger)
    {
        foreach (var module in declared.OrderBy(module => module.Name, StringComparer.Ordinal))
        {
            if (module.DependsOn.FirstOrDefault(dependency => !byName.ContainsKey(dependency)) is not null)
            {
                throw MigrationRefusal.Refuse(logger, ErrorCodes.PersistenceSchemaIncompatible, MigrationRefusal.UnknownDependency, module.Name);
            }
        }

        var waiting = declared.ToDictionary(module => module.Name, module => module.DependsOn.Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);
        var ready = new SortedSet<string>(waiting.Where(entry => entry.Value == 0).Select(entry => entry.Key), StringComparer.Ordinal);
        var ordered = new List<MigrationModule>(declared.Count);
        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            ordered.Add(byName[next]);
            foreach (var dependent in declared.Where(module => module.DependsOn.Contains(next, StringComparer.Ordinal)))
            {
                if (--waiting[dependent.Name] == 0)
                {
                    ready.Add(dependent.Name);
                }
            }
        }

        if (ordered.Count != declared.Count)
        {
            throw MigrationRefusal.Refuse(logger, ErrorCodes.PersistenceSchemaIncompatible, MigrationRefusal.DependencyCycle, CycleMember(declared, ordered));
        }

        return ordered;
    }

    // Peels modules that only depend on the cycle, so the module named is on a cycle.
    private static string CycleMember(List<MigrationModule> declared, List<MigrationModule> ordered)
    {
        var remaining = declared.Except(ordered).ToList();
        while (remaining.FirstOrDefault(candidate => !remaining.Any(module => module.DependsOn.Contains(candidate.Name, StringComparer.Ordinal))) is { } downstream)
        {
            remaining.Remove(downstream);
        }

        return remaining.Select(module => module.Name).Order(StringComparer.Ordinal).First();
    }

    private static List<MigrationScript> Scripts(MigrationModule module, ILogger logger)
    {
        var scripts = new List<MigrationScript>();
        foreach (var version in module.ReadFiles().GroupBy(file => file.Version).OrderBy(group => group.Key))
        {
            var files = version.ToList();
            if (files.Select(file => file.Slug).Distinct(StringComparer.Ordinal).Count() > 1 || files.Count(file => file.Forward) > 1 || files.Count(file => !file.Forward) > 1)
            {
                throw MigrationRefusal.Refuse(logger, ErrorCodes.PersistenceSchemaIncompatible, MigrationRefusal.DuplicateVersion, module.Name, version.Key);
            }

            if (files.Count != 2)
            {
                throw MigrationRefusal.Refuse(logger, ErrorCodes.PersistenceSchemaIncompatible, MigrationRefusal.MissingReverse, module.Name, version.Key);
            }

            var identity = new MigrationIdentity(module.Name, version.Key);
            if (!MigrationScript.TryCreate(identity, files.Single(file => file.Forward).Content, files.Single(file => !file.Forward).Content, out var script))
            {
                throw MigrationRefusal.Refuse(logger, ErrorCodes.PersistenceSchemaIncompatible, MigrationRefusal.InvalidEncoding, module.Name, version.Key);
            }

            if (SchemaReferenceScanner.ReferencesOtherSchema(script.Forward, module.Schema) || SchemaReferenceScanner.ReferencesOtherSchema(script.Reverse, module.Schema))
            {
                throw MigrationRefusal.Refuse(logger, ErrorCodes.PersistenceSchemaIncompatible, MigrationRefusal.CrossSchemaReference, module.Name, version.Key);
            }

            scripts.Add(script);
        }

        return scripts;
    }
}
