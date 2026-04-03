using System.Threading;

namespace Stt.Core.Models;

public sealed class PendingTranscription : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<TranscriptResult>> _transcribeAsync;
    private readonly Func<CancellationToken, Task> _discardAsync;
    private int _state;

    public PendingTranscription(
        Func<CancellationToken, Task<TranscriptResult>> transcribeAsync,
        Func<CancellationToken, Task> discardAsync)
    {
        _transcribeAsync = transcribeAsync;
        _discardAsync = discardAsync;
    }

    public async Task<TranscriptResult> TranscribeAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            throw new InvalidOperationException("This pending transcription has already been completed.");
        }

        return await _transcribeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DiscardAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) != 0)
        {
            return;
        }

        await _discardAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
        {
            await _discardAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
