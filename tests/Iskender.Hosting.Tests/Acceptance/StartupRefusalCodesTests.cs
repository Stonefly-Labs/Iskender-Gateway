using Iskender.Core.Contracts.Errors;
using Iskender.Hosting.Testing;
using Iskender.Hosting.Tests.Fixtures;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>
/// SC-003: each of ISK-0003's 15 operator-caused <c>config.*</c> codes, from one of ISK-0003's fixture documents, stops
/// the host's start with <c>hosting.configuration_rejected</c>, whose diagnostics name that code. One row sets process
/// environment variables, so the class runs in <see cref="ProcessEnvironment"/>.
/// </summary>
[Collection(ProcessEnvironment.Name)]
public class StartupRefusalCodesTests
{
    private static readonly Dictionary<string, Row> Rows = new(StringComparer.Ordinal)
    {
        ["config.document.duplicate_key"] = new("invalid/config.document.duplicate_key/repeated-key.json"),
        ["config.document.malformed"] = new("invalid/config.document.malformed/trailing-comma.json"),
        ["config.document.too_large"] = new("invalid/config.document.too_large/string.json"),
        ["config.document.unsupported_construct"] = new("invalid/config.document.unsupported_construct/anchor.yaml"),
        ["config.environment.invalid"] = new("invalid/config.environment.invalid/underscore.yaml", EnvironmentName: "pre_prod"),
        ["config.field.invalid_format"] = new("invalid/config.field.invalid_format/leading-zero-duration.json"),
        ["config.field.out_of_range"] = new("invalid/config.field.out_of_range/above-maximum.json"),
        ["config.field.required"] = new("invalid/config.field.required/missing-host.json"),
        ["config.field.type_mismatch"] = new("invalid/config.field.type_mismatch/string-for-integer.json"),
        ["config.field.unknown"] = new("invalid/config.field.unknown/unknown-field.json"),
        ["config.namespace.unknown"] = new("invalid/config.namespace.unknown/unknown-section.json"),
        ["config.overlay.conflict"] = new(
            "invalid/config.overlay.conflict/two-casings.json",
            Variables: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ISKENDER__PERSISTENCE__HOST"] = "a.example",
                ["iskender__Persistence__host"] = "b.example",
            }),
        ["config.secret.plaintext"] = new("invalid/config.secret.plaintext/plaintext-password.json"),
        ["config.secret.reference_malformed"] = new("invalid/config.secret.reference_malformed/extra-member.json"),
        ["config.value.unsafe_for_environment"] = new("invalid/config.value.unsafe_for_environment/trace-without-environment.json"),
    };

    public static TheoryData<string> OperatorCodes() => [.. InvalidRequestConfigCodes()];

    [Fact]
    public void ThereAreExactlyFifteenOperatorCausedConfigurationCodesAndEachHasARow()
    {
        var codes = InvalidRequestConfigCodes();

        Assert.Equal(15, codes.Count);
        Assert.Equal(codes.Order(StringComparer.Ordinal), Rows.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(OperatorCodes))]
    public async Task EachCodeStopsTheHostsStart(string code)
    {
        var row = Rows[code];
        using var variables = ProcessEnvironment.Set(row.Variables ?? new Dictionary<string, string>());

        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Services.AddIskender(options =>
                {
                    options.BaseDocument = IskenderConfigurationDocument.FromFile(TestSchemaModule.Fixture(row.Fixture), row.Fixture.EndsWith(".json", StringComparison.Ordinal) ? IskenderDocumentFormat.Json : IskenderDocumentFormat.Yaml);
                    options.EnvironmentName = row.EnvironmentName;
                    options.ReadEnvironmentVariables = row.Variables is not null;
                });
                builder.Services.AddTestSchema();
            },
            ct: TestContext.Current.CancellationToken));

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        Assert.Contains(code, failure.Error!.Diagnostics!.Text, StringComparison.Ordinal);
    }

    private static List<string> InvalidRequestConfigCodes() =>
        [.. ErrorCodes.All.Where(code => code.Category == ErrorCategory.InvalidRequest && code.Id.StartsWith("config.", StringComparison.Ordinal)).Select(code => code.Id)];

    private sealed record Row(string Fixture, string? EnvironmentName = null, IReadOnlyDictionary<string, string>? Variables = null);
}
