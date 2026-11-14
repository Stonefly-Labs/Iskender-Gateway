using Iskender.Core.Contracts.Context;
using Iskender.Hosting.Requests;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>
/// User Story 4, acceptance scenario 1, and FR-007: every combination of registrations that satisfies its requirements
/// builds with scope and build validation on, and every Iskender service has its contracted lifetime (hosting contract,
/// Registration rule 6 and Lifetimes).
/// </summary>
public class LifetimesContainerValidationTests
{
    private static readonly Type[] Scoped = [typeof(RequestContextHolder), typeof(GatewayExecutionContext)];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Combinations => new()
    {
        "hosting",
        "hosting,coordination",
        "hosting,persistence",
        "hosting,coordination,persistence",
        "hosting,coordination,persistence,postgresql-coordinator",
        "hosting,coordination,redis-coordinator",
        "hosting,coordination,persistence,postgresql-coordinator,redis-coordinator",
    };

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task EachCombinationBuildsWithScopeAndBuildValidation(string combination)
    {
        await using var host = await EmbeddedHost.CreateAsync(builder => Register(builder.Services, combination), start: false, ct: Token);

        Assert.NotNull(host.Services);
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void EveryIskenderServiceIsASingletonExceptTheRequestContext(string combination)
    {
        var services = new ServiceCollection();

        Register(services, combination);

        Assert.DoesNotContain(services, descriptor => descriptor.Lifetime == ServiceLifetime.Transient);
        Assert.Equal(
            Scoped.Select(type => type.FullName),
            services.Where(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped).Select(descriptor => descriptor.ServiceType.FullName));
        Assert.All(
            services.Where(descriptor => !Scoped.Contains(descriptor.ServiceType)),
            descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
    }

    private static void Register(IServiceCollection services, string combination)
    {
        var parts = combination.Split(',');
        services.AddIskender(_ => { });
        if (parts.Contains("coordination"))
        {
            services.AddIskenderCoordination();
        }

        if (parts.Contains("persistence"))
        {
            services.AddIskenderPostgreSqlPersistence();
        }

        if (parts.Contains("postgresql-coordinator"))
        {
            services.AddIskenderPostgreSqlCoordination();
        }

        if (parts.Contains("redis-coordinator"))
        {
            services.AddIskenderRedisCoordination();
        }
    }
}
