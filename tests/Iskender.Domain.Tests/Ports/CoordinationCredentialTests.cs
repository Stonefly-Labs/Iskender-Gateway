using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class CoordinationCredentialTests
{
    private const string Canary = "canary-5b1e8d4f-password";

    [Fact]
    public void ACanaryPasswordNeverAppearsInFormatting()
    {
        var credential = new CoordinationCredential(Canary);

        Assert.Equal(Canary, credential.Password);
        Assert.Equal("[redacted]", credential.ToString());
        Assert.DoesNotContain(Canary, $"{credential}", StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", credential), StringComparison.Ordinal);
    }

    [Fact]
    public void ACredentialOffersNoEquality()
    {
        Assert.NotEqual(new CoordinationCredential(Canary), new CoordinationCredential(Canary));
        Assert.Equal(typeof(object), typeof(CoordinationCredential).GetMethod(nameof(Equals), [typeof(object)])!.DeclaringType);
    }

    [Fact]
    public void ARequestCarriesTheReference()
    {
        var reference = new SecretReference("redis/coordination");

        Assert.Same(reference, new CoordinationCredentialRequest(reference).Reference);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CoordinationCredentialRequest(null!)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CoordinationCredential(null!)).Code);
    }
}
