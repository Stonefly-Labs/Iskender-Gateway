using System.Net;
using Iskender.Core.Contracts.Errors;
using Iskender.Hosting.Requests;
using Iskender.Hosting.Testing;
using Iskender.Hosting.Tests.Composition;
using Iskender.Hosting.Tests.Fixtures;
using Iskender.Samples.EmbeddedHost;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>
/// User Story 3, acceptance scenarios 1 to 5, and SC-004: the host's own routes, middleware, configuration, and options
/// behave as they would without Iskender (hosting contract, Coexistence).
/// </summary>
public class CoexistenceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>US3-1 and SC-004: <c>/hello</c> answers the same with and without Iskender.</summary>
    [Fact]
    public async Task TheHostsOwnRouteAnswersTheSameWithAndWithoutIskender()
    {
        var with = await ResponseAsync(withIskender: true, "/hello");
        var without = await ResponseAsync(withIskender: false, "/hello");

        Assert.Equal(HttpStatusCode.OK, with.Status);
        Assert.Equal(without.Status, with.Status);
        Assert.Equal(without.Body, with.Body);
        Assert.Equal(without.Headers, with.Headers);
    }

    /// <summary>The comparison's control: without Iskender, the sample has neither its group nor its registrations.</summary>
    [Fact]
    public async Task WithoutIskenderTheSampleHasNoGroupAndNoIskenderService()
    {
        await using var host = await EmbeddedHost.FromSampleAsync(builder => WithoutIskender(builder), ct: Token);

        using var response = await host.Client.GetAsync(new Uri("/iskender/anything", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Token));
        Assert.False(host.Services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(RequestContextHolder)));
        Assert.DoesNotContain(host.Logs.Entries, entry => entry.Category.StartsWith("Iskender.", StringComparison.Ordinal));
    }

    /// <summary>US3-2: a path under the prefix gets Iskender's envelope, after the host's middleware.</summary>
    [Fact]
    public async Task APathUnderThePrefixGetsTheNotFoundEnvelopeAndTheHostsHeader()
    {
        await using var host = await EmbeddedHost.FromSampleAsync(ct: Token);

        using var response = await host.Client.GetAsync(new Uri("/iskender/anything", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.ToString());
        var envelope = ErrorEnvelopeCodec.Read(await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal(ErrorCodes.ResourceNotFound.Id, envelope.Code);
        Assert.Matches("^req_[0-9a-hjkmnp-tv-z]{26}$", envelope.CorrelationId);
        Assert.Equal([SampleHeaderMiddleware.HeaderValue], response.Headers.GetValues(SampleHeaderMiddleware.HeaderName));
    }

    /// <summary>US3-3: a path outside the prefix gets the host's own 404, and no Iskender code runs for it.</summary>
    [Fact]
    public async Task APathOutsideThePrefixGetsTheHostsOwnNotFoundAndRunsNoIskenderCode()
    {
        var bootstraps = 0;
        await using var host = await EmbeddedHost.FromSampleAsync(builder => CountBootstraps(builder, () => Interlocked.Increment(ref bootstraps)), ct: Token);
        var before = IskenderEntries(host);

        using var response = await host.Client.GetAsync(new Uri("/missing", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Equal(0, Volatile.Read(ref bootstraps));
        Assert.Equal(before, IskenderEntries(host));

        // The control: a request under the prefix does run the bootstrap.
        using var under = await host.Client.GetAsync(new Uri("/iskender/anything", UriKind.Relative), Token);
        Assert.Equal(1, Volatile.Read(ref bootstraps));
    }

    /// <summary>US3-4: start changes no host configuration key and no host options type's configuration.</summary>
    [Fact]
    public async Task StartChangesNoHostConfigurationAndNoHostOptionsConfiguration()
    {
        IServiceCollection? services = null;
        await using var host = await EmbeddedHost.FromSampleAsync(builder => services = builder.Services, start: false, ct: Token);
        var optionsTypes = OptionsTypes(services!);
        var configurationBefore = Configuration(host.Services);
        var optionsBefore = OptionsConfiguration(host.Services, optionsTypes);

        await host.StartAsync(Token);
        using var response = await host.Client.GetAsync(new Uri("/iskender/anything", UriKind.Relative), Token);

        Assert.NotEmpty(configurationBefore);
        Assert.NotEmpty(optionsTypes);
        Assert.Equal(configurationBefore, Configuration(host.Services));
        Assert.Equal(optionsBefore, OptionsConfiguration(host.Services, optionsTypes));
    }

    /// <summary>US3-5 and FR-021: the Iskender registrations add none of the services a host owns at its edge.</summary>
    [Fact]
    public void TheIskenderRegistrationsAddNoHostEdgeService()
    {
        var added = HostServiceIsolationTests.Compose(new ServiceCollection());

        Assert.NotEmpty(added);
        Assert.Empty(added.Where(IsHostEdgeService).Select(descriptor => descriptor.ServiceType.FullName));
    }

    /// <summary>US3-5 on the sample: the services Iskender adds to a real host include no host edge service either.</summary>
    [Fact]
    public async Task TheSamplesIskenderServicesIncludeNoHostEdgeService()
    {
        IServiceCollection? with = null;
        IServiceCollection? without = null;
        await (await EmbeddedHost.FromSampleAsync(builder => with = builder.Services, start: false, ct: Token)).DisposeAsync();
        await (await EmbeddedHost.FromSampleAsync(builder => without = WithoutIskender(builder), start: false, ct: Token)).DisposeAsync();

        Assert.True(with!.Count > without!.Count);
        Assert.Equal(EdgeServices(without), EdgeServices(with));
    }

    /// <summary>FR-002: the code a request under the prefix runs resolves none of the host's configuration or environment.</summary>
    [Fact]
    public async Task ARequestUnderThePrefixResolvesNoHostConfigurationOrEnvironment()
    {
        var services = HostServiceIsolationTests.HostLike();
        var iskender = HostServiceIsolationTests.Compose(services);
        await using var real = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var recorder = new RecordingProvider(real, iskender);
        var hosted = recorder.GetServices<IHostedService>().ToList();
        foreach (var service in hosted.OfType<IHostedLifecycleService>())
        {
            await service.StartingAsync(Token);
        }

        foreach (var service in hosted)
        {
            await service.StartAsync(Token);
        }

        var atStart = recorder.Resolved.Count;
        var http = new DefaultHttpContext { RequestServices = recorder };
        http.Request.Path = "/iskender/anything";
        http.Response.Body = new MemoryStream();

        await new RequestBootstrap().InvokeAsync(EndpointFilterInvocationContext.Create(http), async invocation =>
        {
            await NotFoundEndpoint.HandleAsync(invocation.HttpContext);
            return null;
        });

        foreach (var service in Enumerable.Reverse(hosted))
        {
            await service.StopAsync(Token);
        }

        Assert.Equal(StatusCodes.Status404NotFound, http.Response.StatusCode);
        Assert.True(recorder.Resolved.Count > atStart);
        Assert.DoesNotContain(recorder.Resolved, HostServiceIsolationTests.HostServices.Contains);
    }

    private static IServiceCollection WithoutIskender(WebApplicationBuilder builder)
    {
        builder.Host.Properties[SampleApplication.WithoutIskender] = true;
        return builder.Services;
    }

    private static async Task<(HttpStatusCode Status, string Body, List<string> Headers)> ResponseAsync(bool withIskender, string path)
    {
        await using var host = await EmbeddedHost.FromSampleAsync(
            builder =>
            {
                if (!withIskender)
                {
                    WithoutIskender(builder);
                }
            },
            ct: Token);
        using var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative), Token);

        var headers = response.Headers.Concat(response.Content.Headers)
            .Where(header => !string.Equals(header.Key, "Date", StringComparison.OrdinalIgnoreCase))
            .Select(header => $"{header.Key}: {string.Join(',', header.Value)}")
            .Order(StringComparer.Ordinal)
            .ToList();
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Token), headers);
    }

    // Replaces the request holder's registration with one that counts each request scope's holder, which only the
    // bootstrap resolves.
    private static void CountBootstraps(WebApplicationBuilder builder, Action counted)
    {
        builder.Services.Remove(builder.Services.Single(descriptor => descriptor.ServiceType == typeof(RequestContextHolder)));
        builder.Services.AddScoped(_ =>
        {
            counted();
            return new RequestContextHolder();
        });
    }

    private static int IskenderEntries(EmbeddedHost host) => host.Logs.Entries.Count(entry => entry.Category.StartsWith("Iskender.", StringComparison.Ordinal));

    private static List<string> Configuration(IServiceProvider services) =>
        [.. services.GetRequiredService<IConfiguration>().AsEnumerable().Select(pair => $"{pair.Key}={pair.Value}").Order(StringComparer.Ordinal)];

    // Every closed options type that some registration configures or post-configures.
    private static List<Type> OptionsTypes(IServiceCollection services) =>
        [.. services
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.IsConstructedGenericType && !type.ContainsGenericParameters
                && (type.GetGenericTypeDefinition() == typeof(IConfigureOptions<>) || type.GetGenericTypeDefinition() == typeof(IPostConfigureOptions<>)))
            .Select(type => type.GetGenericArguments()[0])
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    // For each options type, the implementation types of its resolved configure and post-configure services, in order.
    private static List<string> OptionsConfiguration(IServiceProvider services, List<Type> optionsTypes) =>
        [.. optionsTypes.SelectMany(type => new[] { typeof(IConfigureOptions<>), typeof(IPostConfigureOptions<>) }
            .Select(kind => $"{kind.Name}<{type.FullName}>: {string.Join(',', services.GetServices(kind.MakeGenericType(type)).Select(service => service!.GetType().FullName))}"))];

    private static List<string> EdgeServices(IServiceCollection services) =>
        [.. services.Where(IsHostEdgeService).Select(descriptor => $"{descriptor.ServiceType.FullName} {descriptor.ImplementationType?.FullName ?? descriptor.ImplementationInstance?.GetType().FullName ?? descriptor.ImplementationFactory?.Method.DeclaringType?.FullName}")];

    // A service a host owns at its edge: a startup filter, authentication, CORS, an exception handler, a logging
    // provider, a telemetry exporter, or the configuration of an options type.
    private static bool IsHostEdgeService(ServiceDescriptor descriptor)
    {
        var type = descriptor.ServiceType;
        var implementation = descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType;
        Type[] edge =
        [
            typeof(IStartupFilter), typeof(IAuthenticationSchemeProvider), typeof(IAuthenticationService), typeof(IAuthenticationHandler),
            typeof(IAuthenticationHandlerProvider), typeof(ICorsService), typeof(ICorsPolicyProvider), typeof(IExceptionHandler), typeof(ILoggerProvider),
        ];
        return edge.Any(edgeType => edgeType.IsAssignableFrom(type) || (implementation is not null && edgeType.IsAssignableFrom(implementation)))
            || type.FullName?.Contains("Exporter", StringComparison.Ordinal) == true
            || type.Assembly.GetName().Name?.StartsWith("OpenTelemetry", StringComparison.Ordinal) == true
            || (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IConfigureOptions<>) || type.GetGenericTypeDefinition() == typeof(IPostConfigureOptions<>)));
    }
}
