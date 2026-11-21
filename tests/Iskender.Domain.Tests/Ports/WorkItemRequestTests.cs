using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class WorkItemRequestTests
{
    private static readonly WorkKind Kind = new("config", "publish");

    private static readonly OwnerScope Owner = TestScopes.Organization();

    [Theory]
    [InlineData("a")]
    [InlineData("AZaz09._:-")]
    [InlineData("k-128")]
    public void ValidKeysAreAccepted(string key)
    {
        key = key == "k-128" ? new string('k', 128) : key;

        Assert.Equal(key, new WorkItemRequest(Owner, Kind, 1, new byte[] { 1 }, key).IdempotencyKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("k-129")]
    [InlineData("has space")]
    [InlineData("slash/")]
    [InlineData("é")]
    [InlineData("line\n")]
    public void InvalidKeysAreRejected(string? key)
    {
        key = key == "k-129" ? new string('k', 129) : key;

        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new WorkItemRequest(Owner, Kind, 1, ReadOnlyMemory<byte>.Empty, key!)).Code);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1_000, true)]
    [InlineData(1_001, false)]
    public void PayloadVersionsAreOneToOneThousand(int version, bool accepted)
    {
        if (accepted)
        {
            Assert.Equal(version, new WorkItemRequest(Owner, Kind, version, ReadOnlyMemory<byte>.Empty, "key").PayloadVersion);
        }
        else
        {
            Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new WorkItemRequest(Owner, Kind, version, ReadOnlyMemory<byte>.Empty, "key")).Code);
        }
    }

    public static TheoryData<TimeSpan, bool> Delays => new()
    {
        { TimeSpan.FromTicks(-1), false },
        { TimeSpan.Zero, true },
        { TimeSpan.FromDays(30), true },
        { TimeSpan.FromDays(30) + TimeSpan.FromTicks(1), false },
    };

    [Theory]
    [MemberData(nameof(Delays))]
    public void DelaysAreZeroToThirtyDays(TimeSpan delay, bool accepted)
    {
        if (accepted)
        {
            Assert.Equal(delay, new WorkItemRequest(Owner, Kind, 1, ReadOnlyMemory<byte>.Empty, "key", delay).Delay);
        }
        else
        {
            Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new WorkItemRequest(Owner, Kind, 1, ReadOnlyMemory<byte>.Empty, "key", delay)).Code);
        }
    }

    [Fact]
    public void TheDelayDefaultsToZero()
    {
        Assert.Equal(TimeSpan.Zero, new WorkItemRequest(Owner, Kind, 1, ReadOnlyMemory<byte>.Empty, "key").Delay);
    }

    [Fact]
    public void AnOwnerAndAKindAreRequired()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new WorkItemRequest(null!, Kind, 1, ReadOnlyMemory<byte>.Empty, "key")).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new WorkItemRequest(Owner, null!, 1, ReadOnlyMemory<byte>.Empty, "key")).Code);
    }

    [Fact]
    public void ToStringShowsTheKindAndNoKeyPayloadOrScope()
    {
        var owner = TestScopes.Organization();
        var request = new WorkItemRequest(owner, Kind, 1, "canary-payload"u8.ToArray(), "canary-key");

        var text = request.ToString();

        Assert.Contains("config.publish", text, StringComparison.Ordinal);
        Assert.DoesNotContain("canary", text, StringComparison.Ordinal);
        Assert.DoesNotContain(owner.Organization.ToString(), text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnqueueResultCarriesItsIdAndWhetherItWasCreated()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal((id, false), (new WorkEnqueueResult(id, Created: false).ItemId, new WorkEnqueueResult(id, Created: false).Created));
    }

    [Fact]
    public void AnEnqueueResultShowsOnlyWhetherItWasCreated()
    {
        var id = Guid.CreateVersion7();

        var text = new WorkEnqueueResult(id, Created: true).ToString();

        Assert.Contains("True", text, StringComparison.Ordinal);
        Assert.DoesNotContain(id.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(id.ToString("N"), text, StringComparison.OrdinalIgnoreCase);
    }
}
