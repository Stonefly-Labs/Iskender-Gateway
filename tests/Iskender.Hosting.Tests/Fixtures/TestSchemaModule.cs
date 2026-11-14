using Iskender.Domain.Ports;
using Iskender.Domain.Tests.Configuration;
using Iskender.Infrastructure.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Tests.Fixtures;

/// <summary>
/// A test-only module, <c>test-schema</c>, carrying ISK-0003's test schemas, so ISK-0003's fixture documents can drive
/// start-up refusal (data-model "Test fixtures"). The schemas are compiled in from <c>Iskender.Domain.Tests</c> by a
/// linked source file, and the fixture documents are copied beside this assembly, so no test project's internals are
/// referenced.
/// </summary>
internal static class TestSchemaModule
{
    public const string Name = "test-schema";

    /// <summary>Where ISK-0003's fixture documents are copied, relative to this assembly's output directory.</summary>
    public static string FixtureRoot { get; } = Path.Combine(AppContext.BaseDirectory, "Isk0003Fixtures");

    public static IServiceCollection AddTestSchema(this IServiceCollection services)
    {
        CompositionRegistration.Add(services, new CompositionModule(Name, TestSchemas.Registry().Schemas, ["hosting"]));
        return services;
    }

    /// <summary>The full path of an ISK-0003 fixture, given relative to its <c>Fixtures</c> folder.</summary>
    public static string Fixture(string relative) => Path.Combine(FixtureRoot, relative.Replace('/', Path.DirectorySeparatorChar));
}
