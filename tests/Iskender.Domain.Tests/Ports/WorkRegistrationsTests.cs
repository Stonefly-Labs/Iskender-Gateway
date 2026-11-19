using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Domain.Tests.Ports;

public class WorkRegistrationsTests
{
    private static readonly WorkKind Kind = new("config", "publish");

    [Fact]
    public void OneOfEachRegistrationIsAdded()
    {
        var services = new ServiceCollection();

        WorkRegistrations.AddKind(services, new WorkKindDeclaration(Kind, 1));
        WorkRegistrations.AddHandler<Handler>(services, Handling(Kind));
        WorkRegistrations.AddJob<Job>(services, Job.Registration("work.noop"));

        Assert.Equal(Kind, Assert.Single(Instances<WorkKindDeclaration>(services)).Kind);
        Assert.Equal(Kind, Assert.Single(Instances<WorkHandlerRegistration>(services)).Kind);
        Assert.Equal("work.noop", Assert.Single(Instances<RecurringJobRegistration>(services)).Name);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(Handler) && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(Job) && descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    // Each job needs its own feature, wherever it is registered (work contract Leases and fencing rule 1, ISK-0009 T041).
    [Fact]
    public void AJobRegistersItsFeatureNeedingStrictLeasesAndFencingThatFailsClosed()
    {
        var services = new ServiceCollection();

        WorkRegistrations.AddJob<Job>(services, Job.Registration("work.noop"));

        var feature = Assert.Single(Instances<FeatureRequirement>(services));
        Assert.Equal(("work.job.work.noop", "work", true), (feature.Name, feature.Module, feature.MandatoryPath));
        Assert.Equal(
            new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.Leases] = ConsistencyLevel.Strict, [CoordinationPrimitive.Fencing] = ConsistencyLevel.Strict },
            feature.Minimums);
        Assert.Same(OutageBehavior.FailClosed, feature.Outage);
    }

    [Fact]
    public void ASecondDeclarationOfAKindIsADuplicate()
    {
        var services = new ServiceCollection();
        WorkRegistrations.AddKind(services, new WorkKindDeclaration(Kind, 1));

        var failure = Assert.Throws<GatewayException>(() => WorkRegistrations.AddKind(services, new WorkKindDeclaration(Kind, 2)));

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, failure.Code);
        WorkRegistrations.AddKind(services, new WorkKindDeclaration(new WorkKind("config", "retire"), 1));
    }

    [Fact]
    public void ASecondHandlerOfAKindIsADuplicate()
    {
        var services = new ServiceCollection();
        WorkRegistrations.AddHandler<Handler>(services, Handling(Kind));

        var failure = Assert.Throws<GatewayException>(() => WorkRegistrations.AddHandler<Handler>(services, Handling(Kind)));

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, failure.Code);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(WorkHandlerRegistration));
    }

    [Fact]
    public void ASecondJobOfANameIsADuplicate()
    {
        var services = new ServiceCollection();
        WorkRegistrations.AddJob<Job>(services, Job.Registration("work.noop"));

        var failure = Assert.Throws<GatewayException>(() => WorkRegistrations.AddJob<Job>(services, Job.Registration("work.noop")));

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, failure.Code);
        WorkRegistrations.AddJob<Job>(services, Job.Registration("work.other"));
    }

    [Fact]
    public void AKindDeclarationAndAHandlerOfOneKindCoexist()
    {
        var services = new ServiceCollection();

        WorkRegistrations.AddKind(services, new WorkKindDeclaration(Kind, 1));
        WorkRegistrations.AddHandler<Handler>(services, Handling(Kind));

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(WorkKindDeclaration));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(WorkHandlerRegistration));
    }

    [Fact]
    public void ARegistrationOfAnotherTypeIsRejected()
    {
        var services = new ServiceCollection();

        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => WorkRegistrations.AddHandler<Handler>(services, new WorkHandlerRegistration(Kind, 1, 1, null, null, false, typeof(OtherHandler)))).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => WorkRegistrations.AddJob<Job>(services, new RecurringJobRegistration("work.noop", TimeSpan.FromHours(1), 1, typeof(OtherJob)))).Code);
        Assert.Empty(services);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<GatewayException>(() => WorkRegistrations.AddKind(null!, new WorkKindDeclaration(Kind, 1)));
        Assert.Throws<GatewayException>(() => WorkRegistrations.AddKind(new ServiceCollection(), null!));
        Assert.Throws<GatewayException>(() => WorkRegistrations.AddHandler<Handler>(new ServiceCollection(), null!));
        Assert.Throws<GatewayException>(() => WorkRegistrations.AddJob<Job>(new ServiceCollection(), null!));
    }

    private static IEnumerable<T> Instances<T>(IServiceCollection services) =>
        services.Where(descriptor => descriptor.ServiceType == typeof(T)).Select(descriptor => descriptor.ImplementationInstance).OfType<T>();

    private static WorkHandlerRegistration Handling(WorkKind kind) => new(kind, 1, 1, null, null, false, typeof(Handler));

    internal sealed class Handler : IWorkHandler
    {
        public ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    internal sealed class OtherHandler : IWorkHandler
    {
        public ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    internal sealed class Job : IRecurringJob
    {
        public string Name => "work.noop";

        public static RecurringJobRegistration Registration(string name) => new(name, TimeSpan.FromHours(1), 1, typeof(Job));

        public ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken) => ValueTask.FromResult(RecurringJobOutcome.Completed);
    }

    internal sealed class OtherJob : IRecurringJob
    {
        public string Name => "work.other";

        public ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken) => ValueTask.FromResult(RecurringJobOutcome.Completed);
    }
}
