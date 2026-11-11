using System.Reflection;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class CoordinationScopeTests
{
    [Fact]
    public void ForResourceTakesTheAttestedOwnerWithNoDeadlineOrToken()
    {
        var owner = TestScopes.Project();

        var scope = CoordinationScope.ForResource(TestScopes.Reference(owner));

        Assert.Same(owner, scope.Owner);
        Assert.Same(owner, scope.UnitScope.Owner);
        Assert.Null(scope.Deadline);
        Assert.Equal(CancellationToken.None, scope.RequestCancellation);
    }

    [Fact]
    public void ForRequestTakesTheResolvedOwnerDeadlineAndToken()
    {
        using var source = new CancellationTokenSource();
        var deadline = DateTimeOffset.UtcNow.AddMinutes(1);
        var context = TestScopes.Context(deadline, source.Token);
        var owner = TestScopes.Organization();
        context.ResolveOwner(owner);

        var scope = CoordinationScope.ForRequest(context);

        Assert.Same(owner, scope.Owner);
        Assert.Equal(deadline, scope.Deadline);
        Assert.Equal(source.Token, scope.RequestCancellation);
    }

    [Fact]
    public void ForRequestWithAnUnresolvedOwnerFailsWithCoordinationScopeRequired()
    {
        var context = TestScopes.Context(DateTimeOffset.UtcNow.AddMinutes(1), TestContext.Current.CancellationToken);

        var failure = Assert.Throws<GatewayException>(() => CoordinationScope.ForRequest(context));

        Assert.Same(ErrorCodes.CoordinationScopeRequired, failure.Code);
    }

    [Fact]
    public void ANullArgumentFailsWithCoordinationScopeRequired()
    {
        Assert.Same(ErrorCodes.CoordinationScopeRequired, Assert.Throws<GatewayException>(() => CoordinationScope.ForRequest(null!)).Code);
        Assert.Same(ErrorCodes.CoordinationScopeRequired, Assert.Throws<GatewayException>(() => CoordinationScope.ForResource(null!)).Code);
        Assert.Same(ErrorCodes.CoordinationScopeRequired, Assert.Throws<GatewayException>(() => CoordinationScope.From(null!)).Code);
    }

    [Fact]
    public void FromPreservesTheWrappedUnitScope()
    {
        var unitScope = UnitOfWorkScope.ForResource(TestScopes.Reference(TestScopes.Project()));

        var scope = CoordinationScope.From(unitScope);

        Assert.Same(unitScope, scope.UnitScope);
        Assert.Same(unitScope.Owner, scope.Owner);
    }

    [Fact]
    public void TheOperatorScopeWrapsTheOperatorUnitScope()
    {
        Assert.Same(UnitOfWorkScope.Operator, CoordinationScope.Operator.UnitScope);
        Assert.Same(OperatorScope.Instance, CoordinationScope.Operator.Owner);
        Assert.Null(CoordinationScope.Operator.Deadline);
    }

    [Fact]
    public void TheScopeHasNoPublicConstructorNoOwnerScopeFactoryAndNoValueEquality()
    {
        var type = typeof(CoordinationScope);

        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain(
            type.GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.GetParameters().Any(parameter => typeof(OwnerScope).IsAssignableFrom(parameter.ParameterType)));
        Assert.Equal(typeof(object), type.GetMethod(nameof(Equals), [typeof(object)])!.DeclaringType);
        var owner = TestScopes.Organization();
        Assert.NotEqual(CoordinationScope.ForResource(TestScopes.Reference(owner)), CoordinationScope.ForResource(TestScopes.Reference(owner)));
    }
}
