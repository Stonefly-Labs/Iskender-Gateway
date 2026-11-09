using System.Collections;
using System.Reflection;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>User Story 3: each module reads only the section it registered, and nothing outside <c>iskender</c> is validated.</summary>
public class SectionIsolationTests
{
    private const string Required = "\"persistence\":{\"host\":\"db\",\"pool\":{\"name\":\"main\"}}";

    private static ConfigurationValidationReport Json(string text) =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, text)));

    [Fact]
    public void Scenario1EachSchemaInstanceReadsOnlyItsOwnSection()
    {
        var snapshot = Json("{\"iskender\":{\"alpha\":{\"name\":\"a\"},\"beta\":{\"name\":\"b\"}," + Required + "}}").Snapshot!;

        Assert.Equal("a", snapshot.GetSection(TestSchemas.Alpha).GetString("name"));
        Assert.Equal("b", snapshot.GetSection(TestSchemas.Beta).GetString("name"));
        Assert.Equal("alpha", snapshot.GetSection(TestSchemas.Alpha).Namespace);

        var copy = SectionSchema.Create("alpha", TestSchemas.Alpha.OwnerModule, TestSchemas.Alpha.Version).WithFields([.. TestSchemas.Alpha.Fields]);
        var exception = Assert.Throws<GatewayException>(() => snapshot.GetSection(copy));
        Assert.Equal(ErrorCodes.ConfigSectionInvalidAccess, exception.Code);
        Assert.Throws<GatewayException>(() => snapshot.GetSection(TestSchemas.Beta).GetInt64("count"));
    }

    [Fact]
    public void Scenario2TwoRegistrationsOfOneNamespaceNameBothOwners()
    {
        var other = SectionSchema.Create("alpha", "Tests.Other", 1);

        var exception = Assert.Throws<GatewayException>(() => ConfigurationSchemaRegistry.Create([TestSchemas.Alpha, other]));

        Assert.Equal(ErrorCodes.ConfigSchemaDuplicateNamespace, exception.Code);
        var owners = exception.Error!.Diagnostics.Values.Select(pair => pair.Value).ToList();
        Assert.Contains(TestSchemas.Alpha.OwnerModule, owners);
        Assert.Contains("Tests.Other", owners);
    }

    [Theory]
    [InlineData("invalid/config.namespace.unknown/unknown-section.json")]
    [InlineData("invalid/config.namespace.unknown/unregistered-yaml.yaml")]
    public void Scenario3AnUnregisteredNamespaceIsUnknown(string fixture)
    {
        Assert.Equal("config.namespace.unknown", Assert.Single(FixtureLoader.AssertMatchesSidecar(fixture).Errors).Code.Id);
    }

    [Fact]
    public void Scenario4KeysOutsideTheRootAreNeverValidatedAndDoNotChangeTheRevision()
    {
        var outside = FixtureLoader.Validate("valid/outside-root.yaml");
        var bare = ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), ConfigurationDocumentSource.FromString(
            "t.yaml", ConfigurationDocumentFormat.Yaml, "iskender:\n  persistence:\n    host: db.example\n    pool:\n      name: main\n")));

        Assert.True(outside.IsValid, outside.ToString());
        Assert.Equal(bare.Snapshot!.Revision, outside.Snapshot!.Revision);
        Assert.Equal(bare.Snapshot.Revision, Json("{\"iskender\":{\"persistence\":{\"host\":\"db.example\",\"pool\":{\"name\":\"main\"}}},\"x\":\"y\",\"n\":[1,{\"a\":null}]}").Snapshot!.Revision);
    }

    [Theory]
    [InlineData("invalid/config.document.unsupported_construct/alias-outside-root.yaml")]
    [InlineData("invalid/config.document.duplicate_key/duplicate-outside-root.json")]
    public void Scenario4DocumentRulesStillApplyOutsideTheRoot(string fixture)
    {
        FixtureLoader.AssertMatchesSidecar(fixture);
    }

    [Fact]
    public void Scenario5NoPublicMemberOfASnapshotOrSectionCanChangeAValue()
    {
        var problems = new List<string>();
        var seen = new HashSet<Type>();
        var pending = new Queue<Type>([typeof(ConfigurationSnapshot), typeof(ConfigurationSection)]);
        while (pending.TryDequeue(out var type))
        {
            if (!seen.Add(type))
            {
                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (property.SetMethod is { IsPublic: true })
                {
                    problems.Add($"{type.Name}.{property.Name} has a public setter");
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Where(field => !field.IsInitOnly && !field.IsLiteral))
            {
                problems.Add($"{type.Name}.{field.Name} is a writable field");
            }

            var returned = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(method => method.DeclaringType != typeof(object))
                .Select(method => method.ReturnType);
            foreach (var result in returned)
            {
                foreach (var reached in Reached(result))
                {
                    if (IsWritableCollection(reached))
                    {
                        problems.Add($"{type.Name} returns writable {reached.Name}");
                    }
                    else if (reached.Namespace?.StartsWith("Iskender.", StringComparison.Ordinal) == true)
                    {
                        pending.Enqueue(reached);
                    }
                }
            }
        }

        Assert.Empty(problems);
        Assert.Contains(typeof(SectionVersion), seen);
        Assert.Contains(typeof(ConfigurationLayers), seen);
        Assert.Contains(typeof(SecretReference), seen);
    }

    /// <summary>The type and, for a generic type, its type arguments.</summary>
    private static IEnumerable<Type> Reached(Type type)
    {
        var bare = Nullable.GetUnderlyingType(type) ?? type;
        yield return bare;
        foreach (var argument in bare.IsGenericType ? bare.GetGenericArguments() : [])
        {
            foreach (var inner in Reached(argument))
            {
                yield return inner;
            }
        }
    }

    private static bool IsWritableCollection(Type type) =>
        type.IsArray
        || (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
            && (definition == typeof(List<>) || definition == typeof(ICollection<>) || definition == typeof(IList<>)
                || definition == typeof(IDictionary<,>) || definition == typeof(Dictionary<,>) || definition == typeof(ISet<>) || definition == typeof(HashSet<>)))
        || type == typeof(IList) || type == typeof(ICollection) || type == typeof(IDictionary);
}
