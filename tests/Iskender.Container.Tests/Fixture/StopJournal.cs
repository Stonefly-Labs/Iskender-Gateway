using System.Collections.Concurrent;
using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Iskender.Infrastructure.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Fixture;

/// <summary>
/// Records the steps of a stop sequence in the order they happen: a slow traffic request, a composition start-up of module
/// <c>fixture-stop</c>, and a container singleton's disposal (container contract, Lifecycle rules 1 to 3).
/// </summary>
internal sealed class StopJournal
{
    public const string SlowPath = "/slow";

    public const string RequestCompleted = "request completed";

    public const string RequestCancelled = "request cancelled";

    public const string StartupStopped = "start-up stopped";

    public const string StartupCancelled = "start-up cancelled";

    public const string StartupFinished = "start-up finished";

    public const string Disposed = "container disposed";

    private const string Module = "fixture-stop";

    private readonly ConcurrentQueue<string> _entries = new();

    private readonly TaskCompletionSource _requestEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _startupEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource<IServiceProvider> _startupServices = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Released by the test to let the slow request answer.</summary>
    public TaskCompletionSource ReleaseRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Released by the test to let a start-up that ignores cancellation finish.</summary>
    public TaskCompletionSource ReleaseStartup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<string> Entries => [.. _entries];

    public Task RequestEntered => _requestEntered.Task;

    public Task StartupEntered => _startupEntered.Task;

    /// <summary>The container, from inside a start-up, before the host's start has finished.</summary>
    public Task<IServiceProvider> StartupServices => _startupServices.Task;

    /// <summary>The index of <paramref name="entry"/>, or -1.</summary>
    public int IndexOf(string entry) => Entries.ToList().IndexOf(entry);

    /// <summary>Adds module <c>fixture-stop</c>, its start-up, and the sentinel singleton. <paramref name="blocking"/> chooses a start-up that waits.</summary>
    public void AddTo(IServiceCollection services, StartupBehavior blocking = StartupBehavior.None)
    {
        CompositionRegistration.Add(services, new CompositionModule(Module, [], []));
        services.AddSingleton<ICompositionStartup>(provider => new Startup(this, provider, blocking));
        services.AddSingleton(_ => new Sentinel(this));
    }

    /// <summary>Maps <see cref="SlowPath"/>, which answers once <see cref="ReleaseRequest"/> is released, or records its cancellation.</summary>
    public void MapSlow(WebApplication app) =>
        app.MapGet(SlowPath, async (HttpContext context) =>
        {
            _requestEntered.TrySetResult();
            try
            {
                await ReleaseRequest.Task.WaitAsync(context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                _entries.Enqueue(RequestCancelled);
                throw;
            }

            _entries.Enqueue(RequestCompleted);
            return "done";
        });

    public enum StartupBehavior
    {
        /// <summary>Starts at once.</summary>
        None,

        /// <summary>Waits until the host's start is cancelled.</summary>
        UntilCancelled,

        /// <summary>Ignores cancellation and waits for <see cref="ReleaseStartup"/>.</summary>
        UntilReleased,
    }

    // Resolved by the start-up, so the container builds it and disposes it last.
    private sealed class Sentinel(StopJournal journal) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            journal._entries.Enqueue(Disposed);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Startup(StopJournal journal, IServiceProvider services, StartupBehavior behavior) : ICompositionStartup
    {
        public string ModuleName => Module;

        public async ValueTask StartAsync(ConfigurationSnapshot snapshot, CancellationToken cancellationToken)
        {
            _ = services.GetRequiredService<Sentinel>();
            journal._startupServices.TrySetResult(services);
            journal._startupEntered.TrySetResult();
            switch (behavior)
            {
                case StartupBehavior.UntilCancelled:
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        journal._entries.Enqueue(StartupCancelled);
                        throw;
                    }

                    break;
                case StartupBehavior.UntilReleased:
                    await journal.ReleaseStartup.Task;
                    journal._entries.Enqueue(StartupFinished);
                    break;
            }
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            journal._entries.Enqueue(StartupStopped);
            return ValueTask.CompletedTask;
        }
    }
}
