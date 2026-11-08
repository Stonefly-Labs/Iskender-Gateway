using System.Globalization;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Core.Contracts.Tests;

public class ErrorPartsTests
{
    [Fact]
    public void EnumValuesAreExplicit()
    {
        Assert.Equal([0, 1], Enum.GetValues<OutputVisibility>().Select(value => (int)value));
        Assert.Equal([0, 1], Enum.GetValues<RedactionStatus>().Select(value => (int)value));
    }

    [Fact]
    public void RetryAdviceAcceptsZeroAndPositiveDelaysAndNone()
    {
        Assert.Null(new RetryAdvice().RetryAfter);
        Assert.Equal(TimeSpan.Zero, new RetryAdvice(TimeSpan.Zero).RetryAfter);
        Assert.Equal(TimeSpan.FromSeconds(3), new RetryAdvice(TimeSpan.FromSeconds(3)).RetryAfter);
        Assert.True(new RetryAdvice(retriable: true).Retriable);
    }

    [Fact]
    public void RetryAdviceRejectsANegativeDelay()
    {
        var exception = Assert.Throws<GatewayException>(() => new RetryAdvice(TimeSpan.FromTicks(-1)));

        Assert.Equal("core.value.invalid", exception.Code.Id);
    }

    [Fact]
    public void ProviderMetadataAcceptsValuesAtTheBounds()
    {
        var requestId = new ProviderNativeId("openai", "req_123");
        var metadata = new ProviderErrorMetadata(new string('a', 64), requestId, new string('Z', 64));

        Assert.Equal(64, metadata.Provider.Length);
        Assert.Same(requestId, metadata.ProviderRequestId);
        Assert.Equal(64, metadata.ProviderErrorCode!.Length);
        Assert.Null(new ProviderErrorMetadata("azure.openai_v1-x").ProviderErrorCode);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("open ai", null)]
    [InlineData("openai/v1", null)]
    [InlineData("openai\n", null)]
    [InlineData("openai", "rate limit")]
    [InlineData("openai", "code:1")]
    public void ProviderMetadataRejectsValuesOutsideTheLimits(string? provider, string? errorCode)
    {
        var exception = Assert.Throws<GatewayException>(() => new ProviderErrorMetadata(provider!, null, errorCode));

        Assert.Equal("core.value.invalid", exception.Code.Id);
    }

    [Fact]
    public void ProviderMetadataRejectsOverlongValues()
    {
        Assert.Equal("core.value.invalid", Assert.Throws<GatewayException>(() => new ProviderErrorMetadata(new string('a', 65))).Code.Id);
        Assert.Equal("core.value.invalid", Assert.Throws<GatewayException>(() => new ProviderErrorMetadata("a", null, new string('a', 65))).Code.Id);
    }

    [Fact]
    public void DiagnosticsRenderAsWithheld()
    {
        var diagnostics = new ErrorDiagnostics("api_key=sk-secret", [new("tenant", "Contoso")]);

        Assert.Equal("[withheld]", diagnostics.ToString());
        Assert.Equal("[withheld]", string.Format(CultureInfo.InvariantCulture, "{0}", diagnostics));
        Assert.False(diagnostics.IsEmpty);
    }

    [Fact]
    public void DiagnosticsWithNothingAreEmpty()
    {
        Assert.True(new ErrorDiagnostics().IsEmpty);
        Assert.True(new ErrorDiagnostics("", []).IsEmpty);
        Assert.False(new ErrorDiagnostics(values: [new("k", "v")]).IsEmpty);
    }
}
