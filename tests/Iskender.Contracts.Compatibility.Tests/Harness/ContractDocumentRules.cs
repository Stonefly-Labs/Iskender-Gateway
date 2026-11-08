using System.Globalization;
using Iskender.Contracts.Compatibility.Tests.Registries;
using Iskender.Contracts.Compatibility.Tests.Support;

namespace Iskender.Contracts.Compatibility.Tests.Harness;

/// <summary>What H4 compares the contract's tables with.</summary>
public sealed record ContractSources(
    RegistrySnapshot Registries,
    IReadOnlyList<BreakEntry> BreakRecord,
    IReadOnlyList<ContractPackageInput> Packages,
    IReadOnlyList<string> OwnershipResolverAllowList);

/// <summary>
/// H4: the published contract's tables match their sources row for row, and its rule tables list exactly the harness's
/// rule catalog, in order. A mismatch names the table, the row, and the field.
/// </summary>
public static class ContractDocumentRules
{
    /// <summary>The contract's path below the specifications root that <c>.iskender/grounding.yaml</c> names.</summary>
    public const string ContractBelowSpecifications = "0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md";

    /// <summary>The fixed map from the "Retry advice permitted" prose to <c>RetryRule</c>, as the contract states it.</summary>
    public static IReadOnlyDictionary<string, string> RetryProse { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["No"] = "Never",
        ["Yes, when the limit states when it resets"] = "WhenResetKnown",
        ["Yes, before any output only"] = "BeforeOutputOnly",
        ["Only before any output, and only when the mapped failure is retriable"] = "BeforeOutputWhenRetriable",
    };

    /// <summary>Resolves the contract through the manifest and checks it. A contract that cannot be found fails H4.</summary>
    public static IReadOnlyList<Violation> Check(IFileSet files, ContractSources sources)
    {
        var root = GroundingManifest.Read(files, "speckit", "specificationsRoot");
        var path = root is null ? null : $"{root.TrimEnd('/')}/{ContractBelowSpecifications}";
        if (path is null || files.ReadText(path) is not { } text)
        {
            return [Violation("contract", path ?? GroundingManifest.RelativePath, "the contract cannot be found through speckit.specificationsRoot", files.Name)];
        }

        return Check(text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'), sources, files.Name);
    }

    public static IReadOnlyList<Violation> Check(IReadOnlyList<string> lines, ContractSources sources, string checkout)
    {
        var violations = new List<Violation>();

        void Report(string table, string row, string what) => violations.Add(Violation($"{table} table, row {row}", RuleCatalog.ContractPath, what, checkout));

        MarkdownTable? Table(string heading, int index = 0)
        {
            var tables = MarkdownTable.UnderHeading(lines, heading);
            if (tables is null || tables.Count <= index)
            {
                violations.Add(Violation($"{heading} table", RuleCatalog.ContractPath, "table missing", checkout));
                return null;
            }

            return tables[index];
        }

        void Field(string table, string row, string field, string documented, string source)
        {
            if (!string.Equals(documented, source, StringComparison.Ordinal))
            {
                Report(table, row, $"{field} is \"{documented}\", the source has \"{source}\"");
            }
        }

        void Keyed<T>(string table, MarkdownTable? markdown, IEnumerable<T> source, Func<T, string> key, Action<string, IReadOnlyList<string>, T> compare)
        {
            if (markdown is null)
            {
                return;
            }

            var expected = source.ToDictionary(key, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in markdown.Rows)
            {
                var id = MarkdownTable.Plain(row[0]);
                if (!seen.Add(id))
                {
                    Report(table, id, "row repeated");
                }
                else if (!expected.TryGetValue(id, out var item))
                {
                    Report(table, id, "row has no source entry");
                }
                else
                {
                    compare(id, row, item);
                }
            }

            foreach (var missing in expected.Keys.Where(id => !seen.Contains(id)).Order(StringComparer.Ordinal))
            {
                Report(table, missing, "row missing");
            }
        }

        Keyed("Wire contracts", Table("Wire contracts"), sources.Registries.WireContracts, contract => contract.Id, (id, row, contract) =>
        {
            Field("Wire contracts", id, "current version", Cell(row, 1), Number(contract.CurrentVersion));
            Field("Wire contracts", id, "supported versions", Cell(row, 2), string.Join(", ", contract.SupportedVersions.Select(Number)));
        });

        Keyed("Error categories", Table("Error categories"), sources.Registries.ErrorCategories, category => category.Id, (id, row, category) =>
        {
            var prose = Cell(row, 2);
            Field("Error categories", id, "retry rule", RetryProse.TryGetValue(prose, out var rule) ? rule : $"unmapped: {prose}", category.RetryRule);
        });

        Keyed("Error codes", Table("Error codes"), sources.Registries.ErrorCodes, code => code.Id, (id, row, code) =>
        {
            Field("Error codes", id, "category", MarkdownTable.Plain(Cell(row, 1)), code.Category);
            Field("Error codes", id, "status", Cell(row, 2), code.Status);
            Field("Error codes", id, "since", Cell(row, 3), Number(code.Since));
            Field("Error codes", id, "meaning", Cell(row, 4), code.Meaning);
            Field("Error codes", id, "safe message", Cell(row, 5), code.SafeMessage);
        });

        Keyed("Resource types", Table("Resource types"), sources.Registries.ResourceTypes, type => type.Name, (id, row, type) =>
        {
            Field("Resource types", id, "prefix", MarkdownTable.Plain(Cell(row, 1)), type.Prefix);
            Field("Resource types", id, "status", Cell(row, 2), type.Status);
            Field("Resource types", id, "since", Cell(row, 3), Number(type.Since));
        });

        Keyed("Contract versions", Table("Versioning policy"), sources.Packages, package => package.Project, (id, row, package) =>
        {
            Field("Contract versions", id, "contract ID", MarkdownTable.Plain(Cell(row, 1)), package.Contract);
            Field("Contract versions", id, "version", Cell(row, 2), Number(package.Version));
        });

        if (Table("Declaring a break") is { } record)
        {
            for (var index = 0; index < Math.Max(record.Rows.Count, sources.BreakRecord.Count); index++)
            {
                var row = (index + 1).ToString(CultureInfo.InvariantCulture);
                if (index >= record.Rows.Count)
                {
                    Report("Break record", row, $"row missing for entry {sources.BreakRecord[index]}");
                    continue;
                }

                if (index >= sources.BreakRecord.Count)
                {
                    Report("Break record", row, "row has no entry in break-record.json");
                    continue;
                }

                var cells = record.Rows[index];
                var entry = sources.BreakRecord[index];
                Field("Break record", row, "contract", MarkdownTable.Plain(Cell(cells, 0)), entry.Contract ?? string.Empty);
                Field("Break record", row, "item", MarkdownTable.Plain(Cell(cells, 1)), entry.Item ?? string.Empty);
                Field("Break record", row, "kind", MarkdownTable.Plain(Cell(cells, 2)), entry.Kind ?? string.Empty);
                Field("Break record", row, "reason", Cell(cells, 3), entry.Reason ?? string.Empty);
                Field("Break record", row, "approved by", Cell(cells, 4), entry.ApprovedBy ?? string.Empty);
                Field("Break record", row, "version", Cell(cells, 5), entry.Version is { } version ? Number(version) : string.Empty);
                Field("Break record", row, "date", Cell(cells, 6), entry.Date ?? string.Empty);
            }
        }

        Keyed("H6", Table("Other rules", 1), sources.OwnershipResolverAllowList, type => type, (_, _, _) => { });

        var rules = new List<string>();
        foreach (var table in MarkdownTable.UnderHeading(lines, "Break catalog") ?? [])
        {
            rules.AddRange(table.Rows.Select(row => MarkdownTable.Plain(row[0])));
        }

        if (Table("Other rules") is { } other)
        {
            rules.AddRange(other.Rows.Select(row => MarkdownTable.Plain(row[0])));
        }

        CompareRules(rules, Report);
        return violations;
    }

    private static void CompareRules(List<string> documented, Action<string, string, string> report)
    {
        var catalog = RuleCatalog.Order;
        foreach (var extra in documented.Where(id => !catalog.Contains(id)))
        {
            report("Rule", extra, "rule is not in the harness's rule catalog");
        }

        foreach (var missing in catalog.Where(id => !documented.Contains(id)))
        {
            report("Rule", missing, "rule is in the harness's rule catalog but not in the contract");
        }

        var common = documented.Where(catalog.Contains).ToList();
        var expected = catalog.Where(documented.Contains).ToList();
        for (var index = 0; index < Math.Min(common.Count, expected.Count); index++)
        {
            if (common[index] != expected[index])
            {
                report("Rule", common[index], $"rule is out of order: position {index + 1} should be {expected[index]}, as in the rule catalog");
                break;
            }
        }
    }

    private static string Cell(IReadOnlyList<string> row, int index) => index < row.Count ? row[index] : string.Empty;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static Violation Violation(string item, string compared, string what, string checkout) =>
        new("H4", "public-contracts", RuleCatalog.ContractVersion, item, what, compared, checkout);
}
