namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>One clean copy shared by a test class's tests, made on first use after the tool check.</summary>
public sealed class SharedCheckout : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private LocalCheckout? _checkout;

    internal async Task<LocalCheckout> GetAsync()
    {
        RequireLocalTools.RequireBashAndGit();
        await _gate.WaitAsync();
        try
        {
            return _checkout ??= await LocalCheckout.CreateAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_checkout is not null)
        {
            await _checkout.DisposeAsync();
        }

        _gate.Dispose();
    }
}
