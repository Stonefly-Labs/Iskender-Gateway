using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Iskender.Domain.Ports;
using Iskender.Hosting;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Work.Tests.Fixture;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Work.Tests.Registration;

/// <summary>
/// A handler is a container singleton (ISK-0009 T085, FR-007, hosting contract Lifetimes), so every attempt of its kind
/// is given the same instance, and a disposable handler is not kept by the root container once per attempt.
/// </summary>
public class HandlerLifetimeTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwoAttemptsOfOneKindAreGivenTheSameHandlerInstance()
    {
        work.RequirePostgreSql();
        var scope = WorkFixture.FreshOrganization();
        var first = await work.EnqueueAsync(scope, InstanceHandler.Kind, "instance-1");
        var second = await work.EnqueueAsync(scope, InstanceHandler.Kind, "instance-2");

        await using var host = await EmbeddedHost.CreateAsync(
            builder =>
            {
                var document = JsonNode.Parse(Iskender.Infrastructure.PostgreSql.Tests.Fixture.TestSettings.Document(work.Database))!;
                document["iskender"]!["coordination"] = JsonNode.Parse("{\"topology\":\"single_instance\",\"leases\":{\"coordinator\":\"postgresql\"}}");
                builder.Services.AddIskender(options =>
                {
                    options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, document.ToJsonString());
                    options.EnvironmentName = "development";
                });
                builder.Services.AddSingleton<IDatabaseCredentialProvider>(TestCredentialProvider.For(work.Postgres));
                builder.Services.AddIskenderPostgreSqlPersistence();
                builder.Services.AddIskenderCoordination();
                builder.Services.AddIskenderPostgreSqlCoordination();
                builder.Services.AddIskenderPostgreSqlWork();
                builder.Services.AddIskenderPostgreSqlWorkDispatcher();
                WorkRegistrations.AddHandler<InstanceHandler>(builder.Services, InstanceHandler.Registration());
            },
            ct: Token);

        await Eventually.StateAsync(work, first, "completed");
        await Eventually.StateAsync(work, second, "completed");
        var handler = host.Services.GetRequiredService<InstanceHandler>();
        var seen = InstanceHandler.Seen.Where(seen => seen.Item == first || seen.Item == second).ToList();
        Assert.Equal(new[] { first, second }.Order(), seen.Select(seen => seen.Item).Order());
        Assert.All(seen, delivery => Assert.Same(handler, delivery.Handler));
    }

    // Records, for each delivery, the instance that handled it.
    private sealed class InstanceHandler : IWorkHandler
    {
        public static WorkKind Kind { get; } = new("applies", "instance");

        public static ConcurrentQueue<(Guid Item, InstanceHandler Handler)> Seen { get; } = new();

        public static WorkHandlerRegistration Registration() =>
            new(Kind, 1, 1, maximumAttempts: null, timeout: null, hasExternalEffects: false, typeof(InstanceHandler));

        public ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken)
        {
            Seen.Enqueue((delivery.ItemId, this));
            return ValueTask.CompletedTask;
        }
    }
}
