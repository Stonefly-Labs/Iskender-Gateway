using System.Reflection;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Core.Contracts.Tests.Configuration;

public class ConfigurationValidationReportTests
{
    private static readonly ConfigurationSourceLocation Base = new("gateway.yaml", 12, 5, ConfigurationLayer.Base);

    private static ConfigurationFieldError Error(int line = 12) => new(
        ErrorCodes.ConfigFieldOutOfRange,
        "iskender.persistence.connectionTimeout",
        new ConfigurationSourceLocation("gateway.yaml", line, 5, ConfigurationLayer.Base),
        "must be an integer from 1 to 300",
        "set a value from 1 to 300.");

    [Fact]
    public void FieldErrorRendersTheDocumentedLine()
    {
        Assert.Equal(
            "config.field.out_of_range at iskender.persistence.connectionTimeout (gateway.yaml:12:5, base): must be an integer from 1 to 300. Fix: set a value from 1 to 300.",
            Error().ToString());
    }

    [Fact]
    public void FieldErrorRejectsMissingParts()
    {
        Assert.Throws<GatewayException>(() => new ConfigurationFieldError(null!, "p", Base, "x", "y"));
        Assert.Throws<GatewayException>(() => new ConfigurationFieldError(ErrorCodes.ConfigFieldUnknown, "", Base, "x", "y"));
        Assert.Throws<GatewayException>(() => new ConfigurationFieldError(ErrorCodes.ConfigFieldUnknown, "p", null!, "x", "y"));
        Assert.Throws<GatewayException>(() => new ConfigurationFieldError(ErrorCodes.ConfigFieldUnknown, "p", Base, "", "y"));
        Assert.Throws<GatewayException>(() => new ConfigurationFieldError(ErrorCodes.ConfigFieldUnknown, "p", Base, "x", ""));
    }

    [Fact]
    public void SuccessHoldsTheSnapshotAndItsRevisionAndNoErrors()
    {
        var snapshot = new TestSnapshot();
        var report = ConfigurationValidationReport.Success(snapshot);

        Assert.True(report.IsValid);
        Assert.Same(snapshot, report.Snapshot);
        Assert.Equal(snapshot.Revision, report.Revision);
        Assert.Empty(report.Errors);
        Assert.Equal(0, report.TotalErrorCount);
        Assert.Equal($"Configuration valid: revision {snapshot.Revision}.", report.ToString());
    }

    [Fact]
    public void SuccessRejectsANullSnapshot()
    {
        Assert.Equal(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ConfigurationValidationReport.Success(null!)).Code);
    }

    [Fact]
    public void FailureHoldsErrorsAndNoSnapshot()
    {
        var report = ConfigurationValidationReport.Failure([Error(1), Error(2)], 2);

        Assert.False(report.IsValid);
        Assert.Null(report.Snapshot);
        Assert.Null(report.Revision);
        Assert.Equal(2, report.Errors.Count);
        Assert.Equal(2, report.TotalErrorCount);
        Assert.Equal(
            Error(1) + "\n" + Error(2) + "\n2 configuration error(s); configuration rejected.",
            report.ToString());
    }

    [Fact]
    public void FailureRejectsNoErrorsTooManyErrorsANullErrorAndALowTotal()
    {
        Assert.Throws<GatewayException>(() => ConfigurationValidationReport.Failure([], 0));
        Assert.Throws<GatewayException>(() => ConfigurationValidationReport.Failure([], 5));
        Assert.Throws<GatewayException>(() => ConfigurationValidationReport.Failure(null!, 1));
        Assert.Throws<GatewayException>(() => ConfigurationValidationReport.Failure([Error(), null!], 2));
        Assert.Throws<GatewayException>(() => ConfigurationValidationReport.Failure([Error(), Error()], 1));
        Assert.Throws<GatewayException>(() => ConfigurationValidationReport.Failure(Enumerable.Range(1, 101).Select(Error), 101));
    }

    [Fact]
    public void FailureWithMoreThanAHundredErrorsRendersTheTruncationLine()
    {
        var report = ConfigurationValidationReport.Failure(Enumerable.Range(1, 100).Select(Error), 130);
        var lines = report.ToString().Split('\n');

        Assert.Equal(102, lines.Length);
        Assert.Equal("... 30 more error(s) not shown.", lines[100]);
        Assert.Equal("130 configuration error(s); configuration rejected.", lines[101]);
    }

    [Fact]
    public void ErrorsCannotBeChangedThroughACast()
    {
        var list = Assert.IsAssignableFrom<IList<ConfigurationFieldError>>(ConfigurationValidationReport.Failure([Error()], 1).Errors);

        Assert.True(list.IsReadOnly);
    }

    [Theory]
    [InlineData(typeof(ConfigurationSnapshot))]
    [InlineData(typeof(ConfigurationSection))]
    public void ViewsAreAbstractWithOnlyAProtectedConstructorAndNoSetter(Type type)
    {
        Assert.True(type.IsAbstract);
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.All(type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance), constructor => Assert.True(constructor.IsFamily));
        Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Theory]
    [InlineData(typeof(ConfigurationFieldError))]
    [InlineData(typeof(ConfigurationValidationReport))]
    public void ErrorAndReportAreSealedAndImmutable(Type type)
    {
        Assert.True(type.IsSealed);
        Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void ThePublicSurfaceTakesNoHostingDependencyInjectionFileOrTransportType()
    {
        string[] denied = ["Microsoft.AspNetCore", "Microsoft.Extensions", "System.IO", "System.Net", "System.Data"];
        var types = typeof(SectionSchema).Assembly.GetExportedTypes().Where(type => type.Namespace == typeof(SectionSchema).Namespace);
        var used = types.SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .SelectMany(member => member switch
            {
                PropertyInfo property => [property.PropertyType],
                MethodInfo method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType),
                ConstructorInfo constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType),
                FieldInfo field => [field.FieldType],
                _ => Array.Empty<Type>(),
            })
            .SelectMany(Flatten);

        Assert.All(used, type => Assert.DoesNotContain(denied, prefix => (type.Namespace ?? "").StartsWith(prefix, StringComparison.Ordinal)));
    }

    private static IEnumerable<Type> Flatten(Type type) =>
        type.IsGenericType ? type.GetGenericArguments().SelectMany(Flatten).Prepend(type) : type.HasElementType ? Flatten(type.GetElementType()!) : [type];

    /// <summary>A test-only snapshot (quickstart scenario 14).</summary>
    private sealed class TestSnapshot : ConfigurationSnapshot
    {
        public override ConfigurationRevision Revision { get; } = new("cfg-test");

        public override string? EnvironmentName => null;

        public override IReadOnlyList<SectionVersion> Namespaces => [];

        public override ConfigurationLayers Layers { get; } = new(null, null, 0);

        public override ConfigurationSection GetSection(SectionSchema schema) => throw new GatewayException(ErrorCodes.ConfigSectionInvalidAccess);
    }
}
