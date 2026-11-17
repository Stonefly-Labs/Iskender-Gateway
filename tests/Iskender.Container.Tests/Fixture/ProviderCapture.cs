using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Iskender.Container.Tests.Fixture;

/// <summary>Hands a test the container a running entry point composed, so it can call a port as a feature would.</summary>
internal sealed class ProviderCapture
{
    private readonly TaskCompletionSource<IServiceProvider> _provider = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IServiceProvider> Provider => _provider.Task;

    /// <summary>Registers a hosted service that records the container when the host starts.</summary>
    public void AddTo(IServiceCollection services) =>
        services.AddSingleton<IHostedService>(provider => new Recorder(provider, _provider));

    private sealed class Recorder(IServiceProvider provider, TaskCompletionSource<IServiceProvider> target) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            target.TrySetResult(provider);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
