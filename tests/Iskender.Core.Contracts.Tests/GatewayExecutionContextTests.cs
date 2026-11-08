using System.Diagnostics;
using System.Reflection;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Core.Contracts.Tests;

public class GatewayExecutionContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static GatewayExecutionContext NewContext(DateTimeOffset? deadline = null, TimeProvider? time = null, CancellationToken cancellation = default) =>
        new(
            new CorrelationContext(RequestCorrelationId.New(), ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom()),
            deadline ?? Now.AddSeconds(30),
            time ?? new FixedTime(Now),
            cancellation);

    private static ProjectScope NewOwner() => new ProjectScope(ResourceId.New(ResourceTypes.Organization), ResourceId.New(ResourceTypes.Project));

    private static ScopedResourceReference NewEndpoint(OwnerScope owner) => TestResolver.Resolve(ResourceId.New(ResourceTypes.Endpoint), owner);

    [Fact]
    public void UnresolvedValuesFailWithNotResolved()
    {
        var context = NewContext(cancellation: TestContext.Current.CancellationToken);

        Assert.Equal("core.context.not_resolved", Assert.Throws<GatewayException>(() => context.Owner).Code.Id);
        Assert.Equal("core.context.not_resolved", Assert.Throws<GatewayException>(() => context.Endpoint).Code.Id);
        Assert.Equal("core.context.not_resolved", Assert.Throws<GatewayException>(() => context.ConfigurationRevision).Code.Id);
    }

    // US4 scenario 1 and the set-once state table.
    [Fact]
    public void EachValueResolvesOnceAndASecondResolveLeavesItUnchanged()
    {
        var context = NewContext(cancellation: TestContext.Current.CancellationToken);
        var owner = NewOwner();
        var endpoint = NewEndpoint(owner);
        var revision = new ConfigurationRevision("rev-1");

        context.ResolveOwner(owner);
        context.ResolveEndpoint(endpoint);
        context.ResolveConfigurationRevision(revision);

        Assert.Equal("core.context.already_resolved", Assert.Throws<GatewayException>(() => context.ResolveOwner(NewOwner())).Code.Id);
        Assert.Equal("core.context.already_resolved", Assert.Throws<GatewayException>(() => context.ResolveEndpoint(NewEndpoint(owner))).Code.Id);
        Assert.Equal("core.context.already_resolved", Assert.Throws<GatewayException>(() => context.ResolveConfigurationRevision(new ConfigurationRevision("rev-2"))).Code.Id);

        Assert.Same(owner, context.Owner);
        Assert.Same(endpoint, context.Endpoint);
        Assert.Same(revision, context.ConfigurationRevision);
    }

    [Fact]
    public void TheCorrelationIsFixedAtCreation()
    {
        var context = NewContext(cancellation: TestContext.Current.CancellationToken);

        Assert.Null(typeof(GatewayExecutionContext).GetProperty(nameof(GatewayExecutionContext.Correlation))!.SetMethod);
        Assert.Same(context.Correlation, context.ForExtension().Correlation);
    }

    [Fact]
    public void ANonEndpointReferenceIsRejectedAndLeavesTheSlotUnresolved()
    {
        var context = NewContext(cancellation: TestContext.Current.CancellationToken);
        var deployment = TestResolver.Resolve(ResourceId.New(ResourceTypes.Deployment), NewOwner());

        Assert.Equal("core.resource_id.wrong_type", Assert.Throws<GatewayException>(() => context.ResolveEndpoint(deployment)).Code.Id);
        Assert.Equal("core.context.not_resolved", Assert.Throws<GatewayException>(() => context.Endpoint).Code.Id);
    }

    [Fact]
    public void AMissingValueIsRejected()
    {
        var context = NewContext(cancellation: TestContext.Current.CancellationToken);

        Assert.Equal("core.value.invalid", Assert.Throws<GatewayException>(() => context.ResolveOwner(null!)).Code.Id);
        Assert.Equal("core.value.invalid", Assert.Throws<GatewayException>(() => context.ResolveEndpoint(null!)).Code.Id);
        Assert.Equal("core.value.invalid", Assert.Throws<GatewayException>(() => context.ResolveConfigurationRevision(null!)).Code.Id);
    }

    // US4 scenario 2.
    [Fact]
    public async Task ConcurrentContextsNeverSeeEachOthersValues()
    {
        const int contexts = 64;

        var results = await Task.WhenAll(Enumerable.Range(0, contexts).Select(index => Task.Run(async () =>
        {
            var context = NewContext();
            var owner = NewOwner();
            var endpoint = NewEndpoint(owner);
            var revision = new ConfigurationRevision($"rev-{index}");

            context.ResolveOwner(owner);
            await Task.Yield();
            context.ResolveEndpoint(endpoint);
            await Task.Yield();
            context.ResolveConfigurationRevision(revision);
            await Task.Yield();

            return context.Owner.Equals(owner) && ReferenceEquals(context.Endpoint, endpoint) && context.ConfigurationRevision == revision;
        })));

        Assert.All(results, Assert.True);
    }

    [Fact]
    public async Task OnlyOneOfManyRacingResolvesWins()
    {
        var context = NewContext(cancellation: TestContext.Current.CancellationToken);
        var owners = Enumerable.Range(0, 32).Select(_ => NewOwner()).ToArray();

        var outcomes = await Task.WhenAll(owners.Select(owner => Task.Run(() =>
        {
            try
            {
                context.ResolveOwner(owner);
                return owner;
            }
            catch (GatewayException exception) when (exception.Code == ErrorCodes.ContextAlreadyResolved)
            {
                return null;
            }
        })));

        var winner = Assert.Single(outcomes, outcome => outcome is not null);
        Assert.Same(winner, context.Owner);
    }

    [Fact]
    public void AContextWhoseDeadlineHasPassedIsExpiredAtCreation()
    {
        var context = NewContext(deadline: Now.AddSeconds(-1), cancellation: TestContext.Current.CancellationToken);

        Assert.True(context.IsExpired);
    }

    [Fact]
    public void ExpiryFollowsTheTimeProviderAndIncludesTheDeadlineItself()
    {
        var time = new FixedTime(Now);
        var context = NewContext(deadline: Now.AddSeconds(5), time: time, cancellation: TestContext.Current.CancellationToken);

        Assert.False(context.IsExpired);
        time.Current = Now.AddSeconds(5);
        Assert.True(context.IsExpired);
    }

    // US4 scenario 4.
    [Fact]
    public void EveryReaderGetsTheSameDeadlineAndCancellation()
    {
        using var source = new CancellationTokenSource();
        var context = NewContext(deadline: Now.AddMinutes(1), cancellation: source.Token);
        var extension = context.ForExtension();

        Assert.Equal(Now.AddMinutes(1), context.Deadline);
        Assert.Equal(context.Deadline, extension.Deadline);
        Assert.Equal(source.Token, context.CancellationToken);
        Assert.Equal(source.Token, extension.CancellationToken);

        source.Cancel();
        Assert.True(extension.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void TheViewsBelongToTheContext()
    {
        var context = NewContext(cancellation: TestContext.Current.CancellationToken);

        Assert.Same(context.Principal, context.Principal);
        Assert.Same(context.RoutingAttempts, context.RoutingAttempts);
        Assert.Same(context.BudgetReservations, context.BudgetReservations);
        Assert.Same(context.PolicyExecution, context.PolicyExecution);
        Assert.NotSame(context.Principal, NewContext(cancellation: TestContext.Current.CancellationToken).Principal);
    }

    [Theory]
    [InlineData(typeof(PrincipalView))]
    [InlineData(typeof(RoutingAttemptView))]
    [InlineData(typeof(BudgetReservationView))]
    [InlineData(typeof(PolicyExecutionView))]
    public void EachViewIsSealedWithNoPublicConstructorAndNoPublicInstanceMember(Type view)
    {
        Assert.True(view.IsSealed);
        Assert.Empty(view.GetConstructors());
        Assert.Empty(view.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void TheExtensionViewReadsTheResolvedValues()
    {
        var context = NewContext(cancellation: TestContext.Current.CancellationToken);
        var owner = NewOwner();
        var endpoint = NewEndpoint(owner);
        var revision = new ConfigurationRevision("rev-9");
        context.ResolveOwner(owner);
        context.ResolveEndpoint(endpoint);
        context.ResolveConfigurationRevision(revision);

        var extension = context.ForExtension();

        Assert.Same(owner, extension.Owner);
        Assert.Same(endpoint, extension.Endpoint);
        Assert.Same(revision, extension.ConfigurationRevision);
    }

    // US4 scenario 3.
    [Fact]
    public void TheExtensionViewIsReadOnlyAndCannotResolve()
    {
        var view = typeof(ExtensionContextView);

        Assert.Empty(view.GetConstructors());
        Assert.All(view.GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(view.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), method => !method.IsSpecialName);
        Assert.Equal(
            ["CancellationToken", "ConfigurationRevision", "Correlation", "Deadline", "Endpoint", "Owner"],
            view.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheContextHasNoStaticState()
    {
        Type[] types = [typeof(GatewayExecutionContext), typeof(ExtensionContextView), typeof(CorrelationContext)];

        // Constants are not state. Any other static field, mutable or not, would be (FR-018).
        Assert.All(types, type => Assert.DoesNotContain(type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic), field => !field.IsLiteral));
    }

    [Fact]
    public void AMissingCorrelationOrTimeProviderIsRejected()
    {
        var correlation = new CorrelationContext(RequestCorrelationId.New(), ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom());

        Assert.Equal("core.value.invalid", Assert.Throws<GatewayException>(() => new GatewayExecutionContext(null!, Now, TimeProvider.System, default)).Code.Id);
        Assert.Equal("core.value.invalid", Assert.Throws<GatewayException>(() => new GatewayExecutionContext(correlation, Now, null!, default)).Code.Id);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Current;
    }

    private sealed class TestResolver : OwnershipResolver
    {
        public static ScopedResourceReference Resolve(ResourceId id, OwnerScope owner) => Attest(id, owner);
    }
}
