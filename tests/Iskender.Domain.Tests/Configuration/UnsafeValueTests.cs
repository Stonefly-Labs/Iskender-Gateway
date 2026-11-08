using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>User Story 2: plaintext secrets, malformed placeholders, and development-only values are rejected.</summary>
public class UnsafeValueTests
{
    private static ConfigurationValidationReport Json(string password) =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), ConfigurationDocumentSource.FromString(
            "t.json", ConfigurationDocumentFormat.Json,
            "{\"iskender\":{\"persistence\":{\"host\":\"db\",\"pool\":{\"name\":\"main\"},\"password\":" + password + "}}}")));

    public static TheoryData<string> MalformedReferences() =>
        [.. FixtureLoader.Fixtures("invalid/config.secret.reference_malformed"), "invalid/secret-ref-traversal.yaml"];

    [Theory]
    [InlineData("invalid/secret-plaintext.yaml")]
    [InlineData("invalid/config.secret.plaintext/plaintext-password.json")]
    [InlineData("invalid/config.secret.plaintext/object-without-placeholder.json")]
    public void Scenario1APlainValueInASecretFieldIsPlaintextAndTheFixNamesAPlaceholder(string fixture)
    {
        var error = Assert.Single(FixtureLoader.AssertMatchesSidecar(fixture).Errors);

        Assert.Contains("secretRef placeholder", error.Fix, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MalformedReferences))]
    public void Scenario2AMalformedPlaceholderIsReferenceMalformed(string fixture)
    {
        Assert.Equal("config.secret.reference_malformed", Assert.Single(FixtureLoader.AssertMatchesSidecar(fixture).Errors).Code.Id);
    }

    [Theory]
    [InlineData("{\"secretRef\":\"/db\"}")]
    [InlineData("{\"secretRef\":\"db/\"}")]
    [InlineData("{\"secretRef\":\"db//primary\"}")]
    [InlineData("{\"secretRef\":\"..\"}")]
    [InlineData("{\"secretRef\":\"\"}")]
    [InlineData("{\"secretRef\":\"db primary\"}")]
    [InlineData("{\"secretRef\":true}")]
    [InlineData("{\"secretRef\":{\"name\":\"db\"}}")]
    [InlineData("{\"secretRef\":[\"db\"]}")]
    public void EveryOtherMalformedNameIsReferenceMalformed(string placeholder)
    {
        Assert.Equal("config.secret.reference_malformed", Assert.Single(Json(placeholder).Errors).Code.Id);
    }

    [Fact]
    public void ANameOfExactly256CharactersIsAccepted()
    {
        var report = Json("{\"secretRef\":\"" + new string('a', 256) + "\"}");

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void Scenario3APlaceholderInAFieldNotDeclaredSecretIsATypeMismatch()
    {
        var error = Assert.Single(FixtureLoader.AssertMatchesSidecar("invalid/secret-in-plain-field.yaml").Errors);

        Assert.Contains("secretRef placeholder", error.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSnapshotHoldsOnlyTheReferenceName()
    {
        var reference = Json("{\"secretRef\":\"db/primary\"}").Snapshot!.GetSection(TestSchemas.Persistence).GetSecretReference("password");

        Assert.Equal(new SecretReference("db/primary"), reference);
        Assert.Equal("db/primary", reference!.Name);
        Assert.Equal("secretRef:db/primary", reference.ToString());
    }

    [Fact]
    public void ChangingOnlyAPlaceholderNameChangesTheRevision()
    {
        var first = Json("{\"secretRef\":\"db/primary\"}").Snapshot!.Revision;

        Assert.NotEqual(first, Json("{\"secretRef\":\"db/replica\"}").Snapshot!.Revision);
        Assert.Equal(first, Json("{ \"secretRef\" : \"db/primary\" }").Snapshot!.Revision);
    }
}
