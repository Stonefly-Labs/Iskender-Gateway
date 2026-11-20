using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class WorkHandlerTypesTests
{
    private static readonly WorkKind Kind = new("config", "publish");

    public static TheoryData<int, int, int?, TimeSpan?, bool> Registrations => new()
    {
        { 1, 1, null, null, true },
        { 1, 1_000, 1, TimeSpan.FromSeconds(1), true },
        { 3, 3, 100, TimeSpan.FromHours(1), true },
        { 0, 1, null, null, false },
        { 1, 1_001, null, null, false },
        { 2, 1, null, null, false },
        { 1, 1, 0, null, false },
        { 1, 1, 101, null, false },
        { 1, 1, null, TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1), false },
        { 1, 1, null, TimeSpan.FromHours(1) + TimeSpan.FromTicks(1), false },
    };

    [Theory]
    [MemberData(nameof(Registrations))]
    public void RegistrationBoundsAreChecked(int lowest, int highest, int? maximumAttempts, TimeSpan? timeout, bool accepted)
    {
        WorkHandlerRegistration Build() => new(Kind, lowest, highest, maximumAttempts, timeout, hasExternalEffects: false, typeof(NoopHandler));

        if (accepted)
        {
            var registration = Build();
            Assert.Equal((lowest, highest, maximumAttempts, timeout), (registration.LowestPayloadVersion, registration.HighestPayloadVersion, registration.MaximumAttempts, registration.Timeout));
        }
        else
        {
            Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(Build).Code);
        }
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(IWorkHandler))]
    [InlineData(typeof(AbstractHandler))]
    [InlineData(null)]
    public void TheHandlerTypeMustBeAConcreteHandler(Type? handlerType)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new WorkHandlerRegistration(Kind, 1, 1, null, null, false, handlerType!)).Code);
    }

    [Fact]
    public void ARegistrationRequiresAKind()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new WorkHandlerRegistration(null!, 1, 1, null, null, false, typeof(NoopHandler))).Code);
    }

    [Fact]
    public void ADeliveryShowsOnlyItsKindAndAttempt()
    {
        var owner = TestScopes.Organization();
        var id = Guid.CreateVersion7();
        var delivery = new WorkDelivery(id, owner, Kind, 2, "canary-payload"u8.ToArray(), "canary-key", attempt: 3);

        var text = delivery.ToString();

        Assert.Contains("config.publish", text, StringComparison.Ordinal);
        Assert.Contains("3", text, StringComparison.Ordinal);
        Assert.DoesNotContain("canary", text, StringComparison.Ordinal);
        Assert.DoesNotContain(id.ToString(), text, StringComparison.Ordinal);
        Assert.DoesNotContain(owner.Organization.ToString(), text, StringComparison.Ordinal);
        Assert.Equal((id, owner, Kind, 2, "canary-key", 3), (delivery.ItemId, delivery.Owner, delivery.Kind, delivery.PayloadVersion, delivery.IdempotencyKey, delivery.Attempt));
    }

    [Fact]
    public void ADeliveryRejectsAnAttemptBelowOne()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new WorkDelivery(Guid.CreateVersion7(), TestScopes.Organization(), Kind, 1, ReadOnlyMemory<byte>.Empty, "key", attempt: 0)).Code);
    }

    [Fact]
    public void NoHandlerPortSignatureNamesADriverOrAspNetCoreType()
    {
        Type[] ports = [typeof(IWorkOutbox), typeof(IWorkHandler), typeof(WorkDelivery), typeof(WorkHandlerRegistration), typeof(IRecurringJob), typeof(RecurringJobContext)];

        var named = ports
            .SelectMany(port => port.GetMethods().SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
                .Concat(port.GetProperties().Select(property => property.PropertyType)))
            .Select(type => type.Namespace ?? string.Empty);

        Assert.DoesNotContain(named, ns => ns.StartsWith("Npgsql", StringComparison.Ordinal) || ns.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    internal sealed class NoopHandler : IWorkHandler
    {
        public ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    internal abstract class AbstractHandler : IWorkHandler
    {
        public abstract ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken);
    }
}
