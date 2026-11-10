using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class DatabaseCredentialTests
{
    private const string Canary = "canary-7f3a9c2e-password";

    [Fact]
    public void ACanaryPasswordNeverAppearsInFormatting()
    {
        var credential = new DatabaseCredential(Canary);

        Assert.Equal(Canary, credential.Password);
        Assert.Equal("[redacted]", credential.ToString());
        Assert.DoesNotContain(Canary, $"{credential}", StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", credential), StringComparison.Ordinal);
    }

    [Fact]
    public void ACredentialOffersNoEquality()
    {
        Assert.NotEqual(new DatabaseCredential(Canary), new DatabaseCredential(Canary));
        Assert.Equal(typeof(object), typeof(DatabaseCredential).GetMethod(nameof(Equals), [typeof(object)])!.DeclaringType);
    }

    [Fact]
    public void ARequestCarriesTheReferenceAndPurpose()
    {
        var reference = new SecretReference("db/runtime");
        var request = new DatabaseCredentialRequest(reference, DatabaseCredentialPurpose.Migration);

        Assert.Same(reference, request.Reference);
        Assert.Equal(DatabaseCredentialPurpose.Migration, request.Purpose);
        Assert.Throws<GatewayException>(() => new DatabaseCredentialRequest(null!, DatabaseCredentialPurpose.Runtime));
        Assert.Throws<GatewayException>(() => new DatabaseCredentialRequest(reference, (DatabaseCredentialPurpose)9));
        Assert.Throws<GatewayException>(() => new DatabaseCredential(null!));
    }
}
