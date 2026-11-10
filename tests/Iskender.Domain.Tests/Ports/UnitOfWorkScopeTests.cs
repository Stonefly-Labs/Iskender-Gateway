using System.Reflection;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class UnitOfWorkScopeTests
{
    [Fact]
    public void ForResourceTakesTheAttestedOwnerWithNoDeadlineOrToken()
    {
        var owner = TestScopes.Project();

        var scope = UnitOfWorkScope.ForResource(TestScopes.Reference(owner));

        Assert.Same(owner, scope.Owner);
        Assert.Null(scope.Deadline);
        Assert.Equal(CancellationToken.None, scope.RequestCancellation);
    }

    [Fact]
    public void ForRequestTakesTheResolvedProjectOwnerDeadlineAndToken()
    {
        using var source = new CancellationTokenSource();
        var deadline = DateTimeOffset.UtcNow.AddMinutes(1);
        var context = TestScopes.Context(deadline, source.Token);
        var owner = TestScopes.Project();
        context.ResolveOwner(owner);

        var scope = UnitOfWorkScope.ForRequest(context);

        Assert.Same(owner, scope.Owner);
        Assert.Equal(deadline, scope.Deadline);
        Assert.Equal(source.Token, scope.RequestCancellation);
    }

    [Fact]
    public void ForRequestWithAnUnresolvedOwnerFailsWithScopeRequired()
    {
        var context = TestScopes.Context(DateTimeOffset.UtcNow.AddMinutes(1), TestContext.Current.CancellationToken);

        var failure = Assert.Throws<GatewayException>(() => UnitOfWorkScope.ForRequest(context));

        Assert.Same(ErrorCodes.PersistenceScopeRequired, failure.Code);
    }

    [Fact]
    public void ANullArgumentFailsWithScopeRequired()
    {
        Assert.Same(ErrorCodes.PersistenceScopeRequired, Assert.Throws<GatewayException>(() => UnitOfWorkScope.ForRequest(null!)).Code);
        Assert.Same(ErrorCodes.PersistenceScopeRequired, Assert.Throws<GatewayException>(() => UnitOfWorkScope.ForResource(null!)).Code);
    }

    [Fact]
    public void TheOperatorScopeIsTheOperatorOwner()
    {
        Assert.Same(OperatorScope.Instance, UnitOfWorkScope.Operator.Owner);
        Assert.Null(UnitOfWorkScope.Operator.Deadline);
    }

    [Fact]
    public void TheScopeHasNoPublicConstructorNoOwnerScopeFactoryAndNoValueEquality()
    {
        var type = typeof(UnitOfWorkScope);

        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain(
            type.GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.GetParameters().Any(parameter => typeof(OwnerScope).IsAssignableFrom(parameter.ParameterType)));
        Assert.Equal(typeof(object), type.GetMethod(nameof(Equals), [typeof(object)])!.DeclaringType);
        Assert.NotEqual(
            UnitOfWorkScope.ForResource(TestScopes.Reference(OperatorScope.Instance)),
            UnitOfWorkScope.ForResource(TestScopes.Reference(OperatorScope.Instance)));
    }
}
