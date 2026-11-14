using Iskender.Hosting;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Samples.EmbeddedHost;

/// <summary>
/// Builds the sample application without running it, so a test can replace its server with the in-memory one (research
/// R5). The application keeps its own service, middleware, configuration, and <c>/hello</c> route beside Iskender, which
/// it maps under <c>/iskender</c>. Owned by ISK-0006.
/// </summary>
public static class SampleApplication
{
    /// <summary>The base document's file name, read from the content root.</summary>
    public const string ConfigurationFile = "iskender.yaml";

    /// <summary>
    /// A host property that <c>configure</c> may set to build the sample without Iskender's registrations and group, so a
    /// test can compare the sample's own behavior with and without Iskender. Not for an embedding host.
    /// </summary>
    internal const string WithoutIskender = "Iskender.Samples.EmbeddedHost.WithoutIskender";

    /// <summary>Builds the application. <paramref name="configure"/> runs after the sample's registrations and before <c>Build()</c>.</summary>
    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<GreetingService>();
        builder.Services.AddSingleton<SampleHeaderMiddleware>();
        var first = builder.Services.Count;
        builder.Services.AddIskender(options => options.BaseDocument = IskenderConfigurationDocument.FromFile(
            Path.Combine(builder.Environment.ContentRootPath, ConfigurationFile),
            IskenderDocumentFormat.Yaml));
        builder.Services.AddIskenderCoordination();
        var iskender = builder.Services.Skip(first).ToList();
        configure?.Invoke(builder);
        var withIskender = !builder.Host.Properties.ContainsKey(WithoutIskender);
        if (!withIskender)
        {
            foreach (var descriptor in iskender)
            {
                builder.Services.Remove(descriptor);
            }
        }

        var app = builder.Build();
        app.UseMiddleware<SampleHeaderMiddleware>();
        app.MapGet("/hello", (GreetingService greetings) => greetings.Greeting);
        if (withIskender)
        {
            app.MapIskender("/iskender");
        }

        return app;
    }
}
