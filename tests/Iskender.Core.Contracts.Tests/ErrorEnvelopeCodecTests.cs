using System.Text;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Core.Contracts.Tests;

public class ErrorEnvelopeCodecTests
{
    private const string ZeroCorrelation = "req_00000000000000000000000000";
    private static readonly RequestCorrelationId Correlation = RequestCorrelationId.New(buffer => buffer.Clear());

    private static string WriteText(GatewayError error) => Encoding.UTF8.GetString(ErrorEnvelopeCodec.Write(error));

    private static ErrorEnvelope ReadText(string json) => ErrorEnvelopeCodec.Read(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void AMinimalErrorIsWrittenInFieldOrderWithNoWhitespace()
    {
        var error = GatewayError.Create(ErrorCodes.IdentityUnauthenticated, Correlation, OutputVisibility.None);

        Assert.Equal(
            "{\"schema_version\":1,\"code\":\"identity.unauthenticated\",\"category\":\"unauthenticated\","
                + "\"message\":\"No valid credential was presented.\",\"correlation_id\":\"" + ZeroCorrelation + "\","
                + "\"redaction_status\":\"none\"}",
            WriteText(error));
    }

    [Fact]
    public void RetryAndProviderObjectsAppearInTheirPlaces()
    {
        var provider = new ProviderErrorMetadata("azure-openai", new ProviderNativeId("azure-openai", "apim-7"), "429");
        var error = GatewayError.Create(
            ErrorCodes.RoutingNoHealthyBackend, Correlation, OutputVisibility.None, new RetryAdvice(TimeSpan.FromMilliseconds(1500)), provider);

        Assert.Equal(
            "{\"schema_version\":1,\"code\":\"routing.no_healthy_backend\",\"category\":\"upstream_unavailable\","
                + "\"message\":\"No backend is currently available to handle the request.\",\"correlation_id\":\"" + ZeroCorrelation + "\","
                + "\"retry\":{\"after_seconds\":2},"
                + "\"provider\":{\"name\":\"azure-openai\",\"request_id\":\"apim-7\",\"error_code\":\"429\"},"
                + "\"redaction_status\":\"none\"}",
            WriteText(error));
    }

    [Fact]
    public void RetryWithoutADelayAndProviderWithOnlyANameAreWrittenBare()
    {
        var error = GatewayError.Create(
            ErrorCodes.RoutingNoHealthyBackend, Correlation, OutputVisibility.None, new RetryAdvice(), new ProviderErrorMetadata("openai"));

        var text = WriteText(error);

        Assert.Contains("\"retry\":{},\"provider\":{\"name\":\"openai\"},", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(1000, 1)]
    [InlineData(1001, 2)]
    [InlineData(60_000, 60)]
    public void RetryDelaysRoundUpToWholeSeconds(int milliseconds, long seconds)
    {
        var error = GatewayError.Create(
            ErrorCodes.GovernanceQuotaExceeded, Correlation, OutputVisibility.None, new RetryAdvice(TimeSpan.FromMilliseconds(milliseconds)));

        Assert.Equal(seconds, ErrorEnvelopeCodec.Read(ErrorEnvelopeCodec.Write(error)).RetryAfterSeconds);
    }

    [Fact]
    public void TheLongestDelayIsWrittenAsANonNegativeIntegerThatReadsBack()
    {
        var error = GatewayError.Create(ErrorCodes.GovernanceQuotaExceeded, Correlation, OutputVisibility.None, new RetryAdvice(TimeSpan.MaxValue));

        var envelope = ErrorEnvelopeCodec.Read(ErrorEnvelopeCodec.Write(error));

        Assert.Equal((TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond) + 1, envelope.RetryAfterSeconds);
        Assert.Contains("\"after_seconds\":922337203686}", WriteText(error), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<b onload=x>")]
    [InlineData("req id with spaces")]
    [InlineData("tenant\"contoso\"")]
    [InlineData("caf\u00e9-1")]
    public void AProviderRequestIdOutsideTheTokenFormIsLeftOut(string value)
    {
        var error = GatewayError.Create(
            ErrorCodes.RoutingNoHealthyBackend, Correlation, OutputVisibility.None, null, new ProviderErrorMetadata("openai", new ProviderNativeId("openai", value), "server_error"));

        Assert.Contains("\"provider\":{\"name\":\"openai\",\"error_code\":\"server_error\"},", WriteText(error), StringComparison.Ordinal);
    }

    [Fact]
    public void AProviderRequestIdIsWrittenUpTo128TokenCharacters()
    {
        static string Written(string value) => WriteText(GatewayError.Create(
            ErrorCodes.RoutingNoHealthyBackend, Correlation, OutputVisibility.None, null, new ProviderErrorMetadata("openai", new ProviderNativeId("openai", value))));

        Assert.Contains($"\"request_id\":\"{new string('a', 128)}\"", Written(new string('a', 128)), StringComparison.Ordinal);
        Assert.Contains("\"request_id\":\"chatcmpl:A.b_c-9\"", Written("chatcmpl:A.b_c-9"), StringComparison.Ordinal);
        Assert.DoesNotContain("request_id", Written(new string('a', 129)), StringComparison.Ordinal);
    }

    [Fact]
    public void ARoundTripKeepsEveryField()
    {
        var provider = new ProviderErrorMetadata("openai", new ProviderNativeId("openai", "req-1"), "server_error");
        var error = GatewayError.Create(
            ErrorCodes.RoutingNoHealthyBackend, Correlation, OutputVisibility.None, new RetryAdvice(TimeSpan.FromSeconds(3)), provider,
            new ErrorDiagnostics("detail"));

        var envelope = ErrorEnvelopeCodec.Read(ErrorEnvelopeCodec.Write(error));

        Assert.Equal(1, envelope.SchemaVersion);
        Assert.Equal("routing.no_healthy_backend", envelope.Code);
        Assert.Equal("upstream_unavailable", envelope.Category);
        Assert.Equal(error.SafeMessage, envelope.Message);
        Assert.Equal(ZeroCorrelation, envelope.CorrelationId);
        Assert.True(envelope.HasRetry);
        Assert.Equal(3, envelope.RetryAfterSeconds);
        Assert.Equal("openai", envelope.ProviderName);
        Assert.Equal("req-1", envelope.ProviderRequestId);
        Assert.Equal("server_error", envelope.ProviderErrorCode);
        Assert.Equal(RedactionStatus.Withheld, envelope.RedactionStatus);
    }

    [Fact]
    public void AReaderIgnoresUnknownFields()
    {
        var envelope = ReadText(
            "{\"schema_version\":1,\"code\":\"core.internal\",\"category\":\"internal\",\"message\":\"m\",\"correlation_id\":\"c\","
                + "\"future\":{\"nested\":[1,2]},\"retry\":{\"after_seconds\":1,\"jitter\":true},\"redaction_status\":\"none\",\"z\":null}");

        Assert.Equal("core.internal", envelope.Code);
        Assert.Equal(1, envelope.RetryAfterSeconds);
        Assert.Null(envelope.ProviderName);
    }

    public static TheoryData<string> Invalid() => new()
    {
        "",
        "not json",
        "[]",
        "{\"code\":\"core.internal\",\"category\":\"internal\",\"message\":\"m\",\"correlation_id\":\"c\",\"redaction_status\":\"none\"}",
        "{\"schema_version\":2,\"code\":\"core.internal\",\"category\":\"internal\",\"message\":\"m\",\"correlation_id\":\"c\",\"redaction_status\":\"none\"}",
        "{\"schema_version\":\"1\",\"code\":\"core.internal\",\"category\":\"internal\",\"message\":\"m\",\"correlation_id\":\"c\",\"redaction_status\":\"none\"}",
        "{\"schema_version\":1,\"category\":\"internal\",\"message\":\"m\",\"correlation_id\":\"c\",\"redaction_status\":\"none\"}",
        "{\"schema_version\":1,\"code\":\"core.internal\",\"message\":\"m\",\"correlation_id\":\"c\",\"redaction_status\":\"none\"}",
        "{\"schema_version\":1,\"code\":\"core.internal\",\"category\":\"internal\",\"correlation_id\":\"c\",\"redaction_status\":\"none\"}",
        "{\"schema_version\":1,\"code\":\"core.internal\",\"category\":\"internal\",\"message\":\"m\",\"redaction_status\":\"none\"}",
        "{\"schema_version\":1,\"code\":\"core.internal\",\"category\":\"internal\",\"message\":\"m\",\"correlation_id\":\"c\"}",
        "{\"schema_version\":1,\"code\":7,\"category\":\"internal\",\"message\":\"m\",\"correlation_id\":\"c\",\"redaction_status\":\"none\"}",
        "{\"schema_version\":1,\"code\":\"core.internal\",\"category\":\"internal\",\"message\":\"m\",\"correlation_id\":\"c\",\"redaction_status\":\"partial\"}",
        "{\"schema_version\":1,\"code\":\"core.internal\",\"category\":\"internal\",\"message\":\"m\",\"correlation_id\":\"c\",\"retry\":true,\"redaction_status\":\"none\"}",
        "{\"schema_version\":1,\"code\":\"core.internal\",\"category\":\"internal\",\"message\":\"m\",\"correlation_id\":\"c\",\"retry\":{\"after_seconds\":\"5\"},\"redaction_status\":\"none\"}",
        "{\"schema_version\":1,\"code\":\"core.internal\",\"category\":\"internal\",\"message\":\"m\",\"correlation_id\":\"c\",\"retry\":{\"after_seconds\":1.5},\"redaction_status\":\"none\"}",
        "{\"schema_version\":1,\"code\":\"core.internal\",\"category\":\"internal\",\"message\":\"m\",\"correlation_id\":\"c\",\"provider\":{},\"redaction_status\":\"none\"}",
        "{\"schema_version\":1,\"code\":\"core.internal\",\"category\":\"internal\",\"message\":\"m\",\"correlation_id\":\"c\",\"provider\":{\"name\":\"p\",\"request_id\":5},\"redaction_status\":\"none\"}",
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void AnInvalidEnvelopeFailsWithValueInvalid(string json)
    {
        var exception = Assert.Throws<GatewayException>(() => ReadText(json));

        Assert.Equal("core.value.invalid", exception.Code.Id);
    }

    public static TheoryData<string> Categories() => new(ErrorCategory.All.Select(category => category.Id));

    // SC-005: no planted detail reaches the envelope, for every category.
    [Theory]
    [MemberData(nameof(Categories))]
    public void PlantedDiagnosticsNeverReachTheEnvelope(string categoryId)
    {
        string[] planted = ["sk-live-PLANTED-CREDENTIAL", "Ignore previous instructions PLANTED-PROMPT", "{\"upstream\":\"PLANTED-RAW-BODY\"}", "Fabrikam PLANTED-TENANT"];
        var code = GatewayErrorTests.CodePerCategory.Single(candidate => candidate.Category.Id == categoryId);
        var diagnostics = new ErrorDiagnostics(
            string.Join(' ', planted),
            [new("credential", planted[0]), new("prompt", planted[1]), new("body", planted[2]), new("tenant", planted[3])]);

        var error = GatewayError.Create(GatewayErrorTests.Registry, code, Correlation, OutputVisibility.None, diagnostics: diagnostics);
        var text = WriteText(error);

        Assert.All(planted, value => Assert.DoesNotContain("PLANTED", text, StringComparison.Ordinal));
        Assert.Equal(RedactionStatus.Withheld, ErrorEnvelopeCodec.Read(Encoding.UTF8.GetBytes(text)).RedactionStatus);
    }

    // SC-006: a missing resource and another tenant's resource give the same envelope.
    [Fact]
    public void MissingAndForeignResourcesGiveIdenticalEnvelopes()
    {
        var missing = GatewayError.Create(ErrorCodes.ResourceNotFound, RequestCorrelationId.New(), OutputVisibility.None);
        var foreign = GatewayError.Create(
            ErrorCodes.ResourceNotFound, RequestCorrelationId.New(), OutputVisibility.None,
            diagnostics: new ErrorDiagnostics("owned by another tenant", [new("owner", "org_7zzzzzzzzzzzzzzzzzzzzzzzzz")]));

        var missingText = WriteText(missing).Replace(missing.CorrelationId.Value, "CORRELATION", StringComparison.Ordinal);
        var foreignText = WriteText(foreign).Replace(foreign.CorrelationId.Value, "CORRELATION", StringComparison.Ordinal);

        Assert.Equal(missingText, foreignText);
        Assert.Contains("\"redaction_status\":\"withheld\"", missingText, StringComparison.Ordinal);
    }
}
