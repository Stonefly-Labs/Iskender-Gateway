using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Identity.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Identity;
using Iskender.Management;
using Iskender.Management.Bootstrap;
using Iskender.Management.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CapturedLog = Iskender.Hosting.Testing.CapturedLog;

namespace Iskender.Identity.Tests.Bootstrap;

/// <summary>
/// The <c>bootstrap</c> command against the operator fixture (ISK-0010 T028, US2.5, FR-003, FR-007, research R6 and R14,
/// container contract Bootstrap command): <c>issue</c> on an uninitialized installation prints exactly one line, a token
/// that redeems; every refusal exits with its own code and prints nothing to standard output; and neither standard error
/// nor any event holds the token.
/// </summary>
public class BootstrapCommandTests(OperatorFixture operators) : IClassFixture<OperatorFixture>, IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (operators.Postgres.UnavailableReason is null)
        {
            await operators.ResetAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task IssueOnAnUninitializedInstallationPrintsOneLineThatRedeems()
    {
        operators.RequirePostgreSql();
        var run = new Run(operators);

        var outcome = await run.CommandAsync(BootstrapOperation.Issue);

        Assert.Equal(new BootstrapOutcome(0, null), outcome);
        var text = run.SingleLine();
        Assert.True(OperatorTokenText.TryParse(text, out var token));
        Assert.Equal(OperatorTokenPurpose.Bootstrap, token.Purpose);
        var redeemed = await new PostgreSqlOperatorRedemption(operators.Factory, new PostgreSqlAuditRecorder(), OperatorFixture.Configuration().Lifetimes)
            .RedeemAsync(OperatorTokenPurpose.Bootstrap, token, OperatorFixture.Context(), Token);
        Assert.True(redeemed.Succeeded);
        var issued = run.Log.Only(9004);
        Assert.Equal(("bootstrap", "0"), (issued.Value("Purpose"), issued.Value("Superseded")));
        Assert.Equal(1L, await operators.AuditCountAsync("action = 'bootstrap_token.issued' AND actor = 'operator_command' AND result = 'succeeded'"));
        run.AssertNoTokenOutsideStandardOutput(text);
    }

    [Fact]
    public async Task ASecondIssueSupersedesTheFirst()
    {
        operators.RequirePostgreSql();
        Assert.Equal(0, (await new Run(operators).CommandAsync(BootstrapOperation.Issue)).ExitCode);
        var second = new Run(operators);

        Assert.Equal(0, (await second.CommandAsync(BootstrapOperation.Issue)).ExitCode);

        Assert.Equal("1", second.Log.Only(9004).Value("Superseded"));
    }

    public static TheoryData<string, int, string> Refusals() => new()
    {
        { "issue-initialized", 8, "operator.already_initialized" },
        { "recover-uninitialized", 9, "operator.not_initialized" },
        { "unreachable", 5, "persistence.unavailable" },
        { "no-identity", 3, "hosting.registration_missing" },
        { "rejected", 2, "hosting.configuration_rejected" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task EachRefusalExitsWithItsOwnCodeAndPrintsNoToken(string refusal, int exitCode, string code)
    {
        operators.RequirePostgreSql();
        if (refusal == "issue-initialized")
        {
            await operators.InitializeInstallationAsync();
        }

        var before = await operators.RowsAsync("isk_identity.tokens");
        var run = new Run(operators)
        {
            Options = refusal switch
            {
                "unreachable" => new OperatorHostOptions { Unreachable = true },
                "rejected" => new OperatorHostOptions { OperatorFields = "\"bootstrap\":{\"attemptsPerMinute\":0}" },
                _ => new OperatorHostOptions(),
            },
            WithIdentity = refusal != "no-identity",
        };

        var outcome = await run.CommandAsync(refusal == "recover-uninitialized" ? BootstrapOperation.Recover : BootstrapOperation.Issue);

        Assert.Equal(exitCode, outcome.ExitCode);
        Assert.Equal(code, outcome.Code?.Id);
        Assert.Equal("failed", outcome.Outcome);
        Assert.Equal("", run.StandardOutput.ToString());
        Assert.Empty(run.Log.ById(9004));
        Assert.Equal(before, await operators.RowsAsync("isk_identity.tokens"));
    }

    [Fact]
    public async Task RecoverOnAnInitializedInstallationPrintsARecoveryToken()
    {
        operators.RequirePostgreSql();
        await operators.InitializeInstallationAsync();
        var run = new Run(operators);

        Assert.Equal(new BootstrapOutcome(0, null), await run.CommandAsync(BootstrapOperation.Recover));

        Assert.True(OperatorTokenText.TryParse(run.SingleLine(), out var token));
        Assert.Equal(OperatorTokenPurpose.Recovery, token.Purpose);
        Assert.Equal("recovery", run.Log.Only(9004).Value("Purpose"));
    }

    [Fact]
    public async Task AnUnexpectedFailureExitsOneAndWritesOnlyItsTypeName()
    {
        var run = new Run(operators);

        var outcome = await BootstrapCommand.RunAsync(
            () => throw new InvalidOperationException("detail that must not be written"), BootstrapOperation.Issue, run.Loggers, run.StandardOutput, run.StandardError, Token);

        Assert.Equal(new BootstrapOutcome(1, null), outcome);
        Assert.Equal("InvalidOperationException" + Environment.NewLine, run.StandardError.ToString());
        Assert.Equal("", run.StandardOutput.ToString());
    }

    [Fact]
    public async Task ANullArgumentOrUndefinedOperationFailsWithValueInvalid()
    {
        var run = new Run(operators);

        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () =>
            await BootstrapCommand.RunAsync(null!, BootstrapOperation.Issue, run.Loggers, run.StandardOutput, run.StandardError, Token))).Code);
        Assert.Same(ErrorCodes.ValueInvalid, (await Assert.ThrowsAsync<GatewayException>(async () =>
            await BootstrapCommand.RunAsync(run.Build, (BootstrapOperation)7, run.Loggers, run.StandardOutput, run.StandardError, Token))).Code);
    }

    /// <summary>One <c>bootstrap</c> run: the serving control plane's registrations, a captured log, and captured output.</summary>
    internal sealed class Run(OperatorFixture operators)
    {
        public OperatorHostOptions Options { get; init; } = new();

        public bool WithIdentity { get; init; } = true;

        public CapturedLog Log { get; } = new();

        public ILoggerFactory Loggers => field ??= LoggerFactory.Create(logging => logging.AddProvider(Log));

        public StringWriter StandardOutput { get; } = new();

        public StringWriter StandardError { get; } = new();

        public ValueTask<BootstrapOutcome> CommandAsync(BootstrapOperation operation) =>
            BootstrapCommand.RunAsync(Build, operation, Loggers, StandardOutput, StandardError, Token);

        public ServiceProvider Build()
        {
            var document = OperatorHost.Document(operators, Options);
            var services = new ServiceCollection();
            services.AddSingleton(Loggers);
            services.AddIskenderControlPlane(root =>
            {
                root.BaseDocument = CompositionDocument.FromText("base.json", ConfigurationDocumentFormat.Json, document);
                root.EnvironmentName = "development";
            });
            services.AddIskenderPostgreSqlPersistence();
            services.AddIskenderPostgreSqlWork();
            if (WithIdentity)
            {
                services.AddIskenderPostgreSqlIdentity();
            }

            services.AddIskenderOperatorSecurity();
            services.AddSingleton<IDatabaseCredentialProvider>(TestCredentialProvider.For(operators.Postgres));
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = WithIdentity });
        }

        /// <summary>Standard output's one line, which ends in a newline.</summary>
        public string SingleLine()
        {
            var output = StandardOutput.ToString();
            Assert.EndsWith("\n", output, StringComparison.Ordinal);
            return Assert.Single(output[..^1].Split('\n'));
        }

        public void AssertNoTokenOutsideStandardOutput(string token)
        {
            var secret = OperatorFixture.Secret(token);
            Assert.DoesNotContain(secret, StandardError.ToString(), StringComparison.Ordinal);
            Assert.All(Log.Entries, entry =>
            {
                Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
                Assert.All(entry.Values.Values, value => Assert.DoesNotContain(secret, value?.ToString() ?? "", StringComparison.Ordinal));
            });
        }
    }
}
