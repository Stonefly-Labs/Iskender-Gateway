using System.Text.Json.Nodes;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Management;
using Iskender.Management.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Identity.Tests.Fixture;

/// <summary>What a control-plane test host composes and maps (ISK-0010 T017).</summary>
internal sealed record OperatorHostOptions
{
    /// <summary>The <c>operator</c> section's fields, as JSON text without braces.</summary>
    public string OperatorFields { get; init; } = "";

    /// <summary>The admin group's prefix.</summary>
    public string Prefix { get; init; } = "/admin/v1";

    /// <summary>Points the host at a closed port, so every store read fails.</summary>
    public bool Unreachable { get; init; }

    /// <summary>Registers the fixture's test principal resolver.</summary>
    public bool WithResolver { get; init; } = true;

    /// <summary>Further registrations, after the fixture's own.</summary>
    public Action<IServiceCollection>? Services { get; init; }

    /// <summary>Replaces the default mapping, <c>MapIskenderOperatorSecurity(Prefix)</c>.</summary>
    public Action<WebApplication>? Map { get; init; }

    /// <summary>Starts the host.</summary>
    public bool Start { get; init; } = true;
}

/// <summary>
/// A control-plane test host on the operator fixture's database (operator-security contract, Test fixture rule 1):
/// <c>AddIskenderControlPlane(...)</c>, <c>AddIskenderOperatorSecurity()</c>, <c>AddIskenderPostgreSqlIdentity()</c>,
/// and the persistence and outbox adapters, on a <c>TestServer</c> with the admin group mapped. Each host has its own
/// service provider, so several share one database as replicas do.
/// </summary>
internal sealed class OperatorHost : IAsyncDisposable
{
    private OperatorHost(EmbeddedHost host, TestOperatorPrincipalResolver resolver)
    {
        Host = host;
        Resolver = resolver;
    }

    public EmbeddedHost Host { get; }

    public TestOperatorPrincipalResolver Resolver { get; }

    public HttpClient Client => Host.Client;

    public IServiceProvider Services => Host.Services;

    public static async Task<OperatorHost> CreateAsync(OperatorFixture fixture, OperatorHostOptions options)
    {
        var resolver = new TestOperatorPrincipalResolver();
        var document = Document(fixture, options);
        var host = await EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Services.AddIskenderControlPlane(root =>
                {
                    root.BaseDocument = CompositionDocument.FromText("base.json", ConfigurationDocumentFormat.Json, document);
                    root.EnvironmentName = "development";
                });
                builder.Services.AddIskenderPostgreSqlPersistence();
                builder.Services.AddIskenderPostgreSqlWork();
                builder.Services.AddIskenderPostgreSqlIdentity();
                builder.Services.AddIskenderOperatorSecurity();
                builder.Services.AddSingleton<IDatabaseCredentialProvider>(TestCredentialProvider.For(fixture.Postgres));
                if (options.WithResolver)
                {
                    builder.Services.AddSingleton<IOperatorPrincipalResolver>(resolver);
                }

                options.Services?.Invoke(builder.Services);
            },
            options.Map ?? (app => app.MapIskenderOperatorSecurity(options.Prefix)),
            options.Start,
            TestContext.Current.CancellationToken);
        return new(host, resolver);
    }

    /// <summary>The persistence section for the fixture's database, or a closed port, with the <c>operator</c> section beside it.</summary>
    public static string Document(OperatorFixture fixture, OperatorHostOptions options)
    {
        var overrides = options.Unreachable ? new Dictionary<string, object> { ["port"] = OperatorFixture.ClosedPort() } : null;
        var document = JsonNode.Parse(TestSettings.Document(fixture.Database, overrides, withMigration: false))!;
        document["iskender"]!["operator"] = JsonNode.Parse("{" + options.OperatorFields + "}");
        return document.ToJsonString();
    }

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}
