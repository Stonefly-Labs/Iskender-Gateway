using System.Diagnostics;
using System.Security.Cryptography;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Core.Contracts.Tests;

public class CorrelationContextTests
{
    private static readonly ActivityTraceId Trace = ActivityTraceId.CreateRandom();
    private static readonly ActivitySpanId Span = ActivitySpanId.CreateRandom();

    [Fact]
    public void TheRequestIdIsReqAndTwentySixBase32Characters()
    {
        var id = RequestCorrelationId.New();

        Assert.Matches("^req_[0-7][0-9a-hjkmnp-tv-z]{25}$", id.Value);
        Assert.Equal(id.Value, id.ToString());
        Assert.NotEqual(id, RequestCorrelationId.New());
    }

    [Fact]
    public void TheRequestIdIsNeverAResourceId()
    {
        var id = RequestCorrelationId.New();

        Assert.All(ResourceTypes.All, type =>
            Assert.Equal("core.resource_id.unknown_type", Assert.Throws<GatewayException>(() => ResourceId.Parse(id.Value, type)).Code.Id));
        Assert.DoesNotContain(
            typeof(RequestCorrelationId).GetMethods(),
            method => method.ReturnType == typeof(ResourceId) || method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ResourceId)));
    }

    [Fact]
    public void AFailingRandomSourceFailsTheRequestId()
    {
        var exception = Assert.Throws<GatewayException>(() => RequestCorrelationId.New(_ => throw new CryptographicException()));

        Assert.Equal("core.random_source_failed", exception.Code.Id);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("client-trace:42.A_b")]
    public void AValidCallerValueIsKept(string value)
    {
        var context = new CorrelationContext(RequestCorrelationId.New(), Trace, Span, value);

        Assert.Equal(value, context.CallerValue);
        Assert.False(context.CallerValueDropped);
    }

    [Fact]
    public void ACallerValueOfOneHundredTwentyEightCharactersIsKept()
    {
        var value = new string('x', 128);

        Assert.Equal(value, new CorrelationContext(RequestCorrelationId.New(), Trace, Span, value).CallerValue);
    }

    public static TheoryData<string> Dropped() => new()
    {
        "",
        new string('x', 129),
        "abc\u0001def",
        "abc\ndef",
        "abc\r\ndef",
        "abc def",
        "{user}",
        "café",
    };

    [Theory]
    [MemberData(nameof(Dropped))]
    public void ACallerValueOutsideTheBoundedFormIsDroppedWhole(string value)
    {
        var requestId = RequestCorrelationId.New();

        var context = new CorrelationContext(requestId, Trace, Span, value);

        Assert.Null(context.CallerValue);
        Assert.True(context.CallerValueDropped);
        Assert.Same(requestId, context.RequestId);
    }

    [Fact]
    public void NoCallerValueIsNotADrop()
    {
        var context = new CorrelationContext(RequestCorrelationId.New(), Trace, Span);

        Assert.Null(context.CallerValue);
        Assert.False(context.CallerValueDropped);
        Assert.Equal(Trace, context.TraceId);
        Assert.Equal(Span, context.SpanId);
    }

    [Fact]
    public void AMissingRequestIdFails()
    {
        Assert.Equal("core.value.invalid", Assert.Throws<GatewayException>(() => new CorrelationContext(null!, Trace, Span)).Code.Id);
    }
}
