using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Stt.Core.Abstractions;
using Stt.Core.Diagnostics;
using Stt.Core.Models;

namespace Stt.Infrastructure.OpenAi;

public sealed class OpenAiRealtimeTranscriptionClient : IRealtimeTranscriptionClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const int RealtimePcmSampleRate = 24_000;
    private const int FinalCommitMinimumAudioBytes = 4_800;
    private const int SilenceDurationMs = 500;

    private readonly HttpClient _httpClient;
    private readonly Func<OpenAiTranscriptionOptions> _optionsAccessor;

    public OpenAiRealtimeTranscriptionClient(
        HttpClient httpClient,
        Func<OpenAiTranscriptionOptions> optionsAccessor)
    {
        _httpClient = httpClient;
        _optionsAccessor = optionsAccessor;
    }

    public void ValidateConfiguration()
    {
        var options = _optionsAccessor();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "Set an OpenAI API key in Settings before recording.");
        }
    }

    public async Task<IRealtimeTranscriptionSession> CreateSessionAsync(CancellationToken cancellationToken)
    {
        var options = _optionsAccessor();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "Set an OpenAI API key in Settings before recording.");
        }

        var clientSecret = await CreateClientSecretAsync(options, cancellationToken).ConfigureAwait(false);
        var session = new OpenAiRealtimeTranscriptionSession(clientSecret);
        await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    private async Task<string> CreateClientSecretAsync(
        OpenAiTranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        var docsAttempt = await TryCreateClientSecretAsync(
                BuildDocsAlignedSessionPayload(options),
                options.ApiKey!,
                cancellationToken)
            .ConfigureAwait(false);

        if (docsAttempt.IsSuccess)
        {
            WhisperTrace.Log(
                "RealtimeClient",
                $"Created realtime transcription session via docs-aligned REST payload. Rate={RealtimePcmSampleRate}Hz SilenceDurationMs={SilenceDurationMs} Language={FormatOptionalValue(options.TranscriptionLanguage)} PromptConfigured={HasConfiguredPrompt(options)}.");
            return docsAttempt.ClientSecret!;
        }

        if (!ShouldFallbackToLegacyShape(docsAttempt.StatusCode))
        {
            throw new InvalidOperationException(
                docsAttempt.ErrorMessage ?? "OpenAI realtime transcription request failed.");
        }

        WhisperTrace.Log(
            "RealtimeClient",
            $"Docs-aligned realtime transcription session create failed ({(int)docsAttempt.StatusCode!}). Falling back to legacy request shape. Error={docsAttempt.ErrorMessage}");

        var legacyAttempt = await TryCreateClientSecretAsync(
                BuildLegacySessionPayload(options),
                options.ApiKey!,
                cancellationToken)
            .ConfigureAwait(false);

        if (!legacyAttempt.IsSuccess)
        {
            throw new InvalidOperationException(
                legacyAttempt.ErrorMessage
                ?? docsAttempt.ErrorMessage
                ?? "OpenAI realtime transcription request failed.");
        }

        WhisperTrace.Log(
            "RealtimeClient",
            $"Created realtime transcription session via legacy REST payload after docs-aligned fallback. Rate={RealtimePcmSampleRate}Hz SilenceDurationMs={SilenceDurationMs} Language={FormatOptionalValue(options.TranscriptionLanguage)} PromptConfigured={HasConfiguredPrompt(options)}.");
        return legacyAttempt.ClientSecret!;
    }

    private async Task<CreateSessionAttemptResult> TryCreateClientSecretAsync(
        object payload,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "v1/realtime/transcription_sessions");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, SerializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responsePayload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return new CreateSessionAttemptResult(
                IsSuccess: false,
                ClientSecret: null,
                ErrorMessage: BuildErrorMessage(responsePayload, response.ReasonPhrase),
                StatusCode: response.StatusCode);
        }

        using var document = JsonDocument.Parse(responsePayload);
        if (!document.RootElement.TryGetProperty("client_secret", out var clientSecret)
            || !clientSecret.TryGetProperty("value", out var valueElement)
            || string.IsNullOrWhiteSpace(valueElement.GetString()))
        {
            return new CreateSessionAttemptResult(
                IsSuccess: false,
                ClientSecret: null,
                ErrorMessage: "OpenAI did not return a realtime transcription client secret.",
                StatusCode: response.StatusCode);
        }

        return new CreateSessionAttemptResult(
            IsSuccess: true,
            ClientSecret: valueElement.GetString(),
            ErrorMessage: null,
            StatusCode: response.StatusCode);
    }

    private static object BuildDocsAlignedSessionPayload(OpenAiTranscriptionOptions options)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "transcription",
            ["audio"] = new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["format"] = new Dictionary<string, object?>
                    {
                        ["type"] = "audio/pcm",
                        ["rate"] = RealtimePcmSampleRate
                    },
                    ["noise_reduction"] = new Dictionary<string, object?>
                    {
                        ["type"] = "near_field"
                    },
                    ["transcription"] = BuildTranscriptionConfiguration(options),
                    ["turn_detection"] = BuildTurnDetectionConfiguration()
                }
            },
            ["include"] = new[]
            {
                "item.input_audio_transcription.logprobs"
            }
        };
    }

    private static object BuildLegacySessionPayload(OpenAiTranscriptionOptions options)
    {
        return new Dictionary<string, object?>
        {
            ["input_audio_format"] = "pcm16",
            ["input_audio_noise_reduction"] = new Dictionary<string, object?>
            {
                ["type"] = "near_field"
            },
            ["input_audio_transcription"] = BuildTranscriptionConfiguration(options),
            ["turn_detection"] = BuildTurnDetectionConfiguration()
        };
    }

    private static Dictionary<string, object?> BuildTranscriptionConfiguration(OpenAiTranscriptionOptions options)
    {
        var configuration = new Dictionary<string, object?>
        {
            ["model"] = options.TranscriptionModel
        };

        if (!string.IsNullOrWhiteSpace(options.TranscriptionLanguage))
        {
            configuration["language"] = options.TranscriptionLanguage.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.TranscriptionPrompt))
        {
            configuration["prompt"] = options.TranscriptionPrompt.Trim();
        }

        return configuration;
    }

    private static Dictionary<string, object?> BuildTurnDetectionConfiguration()
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "server_vad",
            ["threshold"] = 0.5,
            ["prefix_padding_ms"] = 300,
            ["silence_duration_ms"] = SilenceDurationMs
        };
    }

    private static bool ShouldFallbackToLegacyShape(HttpStatusCode? statusCode)
    {
        return statusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;
    }

    private static string FormatOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "auto"
            : value.Trim();
    }

    private static bool HasConfiguredPrompt(OpenAiTranscriptionOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.TranscriptionPrompt);
    }

    private static string BuildErrorMessage(string payload, string? fallbackReason)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var messageProperty)
                && messageProperty.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(messageProperty.GetString()))
            {
                return messageProperty.GetString()!;
            }
        }
        catch
        {
            // Fall back to the HTTP status reason if JSON parsing fails.
        }

        return string.IsNullOrWhiteSpace(fallbackReason)
            ? "OpenAI realtime transcription request failed."
            : $"OpenAI realtime transcription request failed: {fallbackReason}";
    }

    private sealed class OpenAiRealtimeTranscriptionSession : IRealtimeTranscriptionSession
    {
        private static readonly TimeSpan FinalizationTimeout = TimeSpan.FromSeconds(20);

        private readonly string _clientSecret;
        private readonly ClientWebSocket _webSocket = new();
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private readonly CancellationTokenSource _receiveLoopCancellation = new();
        private readonly object _syncRoot = new();
        private readonly List<string> _itemOrder = [];
        private readonly Dictionary<string, TranscriptItemState> _items = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource<bool> _sessionReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<TranscriptResult> _completionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _receiveLoopTask;
        private bool _completionRequested;
        private bool _isDisposed;
        private int _committedEventCount;
        private int _completedEventCount;
        private int _deltaEventCount;
        private int _pendingCommitRequests;
        private int _pendingTranscriptItems;
        private long _totalAppendedAudioBytes;
        private bool _finalCommitRequested;

        public OpenAiRealtimeTranscriptionSession(string clientSecret)
        {
            _clientSecret = clientSecret;
        }

        public event EventHandler<TranscriptUpdatedEventArgs>? TranscriptUpdated;

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            var uri = new Uri("wss://api.openai.com/v1/realtime");

            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_clientSecret}");
            _webSocket.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");
            await _webSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
            WhisperTrace.Log("RealtimeClient", "WebSocket connected to realtime transcription session.");

            _receiveLoopTask = Task.Run(ReceiveLoopAsync);
            await _sessionReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            WhisperTrace.Log("RealtimeClient", "Realtime transcription session is ready.");
        }

        public Task AppendAudioAsync(ReadOnlyMemory<byte> audioBytes, CancellationToken cancellationToken)
        {
            if (audioBytes.IsEmpty)
            {
                return Task.CompletedTask;
            }

            lock (_syncRoot)
            {
                _totalAppendedAudioBytes += audioBytes.Length;
            }

            var payload = new
            {
                type = "input_audio_buffer.append",
                audio = Convert.ToBase64String(audioBytes.Span)
            };

            return SendEventAsync(payload, cancellationToken);
        }

        public async Task<TranscriptResult> CompleteAsync(CancellationToken cancellationToken)
        {
            var shouldRequestFinalCommit = false;

            lock (_syncRoot)
            {
                _completionRequested = true;
                shouldRequestFinalCommit = _totalAppendedAudioBytes >= FinalCommitMinimumAudioBytes;

                if (!shouldRequestFinalCommit)
                {
                    TryCompleteIfReadyLocked();
                }
            }

            if (shouldRequestFinalCommit)
            {
                await RequestFinalCommitAsync(cancellationToken).ConfigureAwait(false);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(FinalizationTimeout);

            var result = await _completionSource.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            WhisperTrace.Log(
                "RealtimeClient",
                $"Completion finished. Commits={_committedEventCount} Deltas={_deltaEventCount} Completed={_completedEventCount} Length={result.Text.Length}.");
            return result;
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _receiveLoopCancellation.Cancel();

            try
            {
                if (_webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await _webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing transcription session.",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                // Best effort shutdown.
            }

            if (_receiveLoopTask is not null)
            {
                try
                {
                    await _receiveLoopTask.ConfigureAwait(false);
                }
                catch
                {
                    // Best effort shutdown.
                }
            }

            _receiveLoopCancellation.Dispose();
            _sendGate.Dispose();
            _webSocket.Dispose();
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8_192];
            var messageBuffer = new ArrayBufferWriter<byte>();

            try
            {
                while (!_receiveLoopCancellation.IsCancellationRequested)
                {
                    messageBuffer.Clear();

                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket
                            .ReceiveAsync(buffer, _receiveLoopCancellation.Token)
                            .ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            throw new InvalidOperationException("OpenAI realtime connection closed unexpectedly.");
                        }

                        messageBuffer.Write(buffer.AsSpan(0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    var payload = Encoding.UTF8.GetString(messageBuffer.WrittenSpan);
                    ProcessServerEvent(payload);
                }
            }
            catch (OperationCanceledException) when (_receiveLoopCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        private void ProcessServerEvent(string payload)
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var eventType = root.TryGetProperty("type", out var typeProperty)
                ? typeProperty.GetString()
                : null;

            switch (eventType)
            {
                case "transcription_session.created":
                case "transcription_session.updated":
                    _sessionReady.TrySetResult(true);
                    break;
                case "input_audio_buffer.committed":
                    HandleCommittedEvent(root);
                    break;
                case "conversation.item.input_audio_transcription.delta":
                    HandleTranscriptDelta(root);
                    break;
                case "conversation.item.input_audio_transcription.completed":
                    HandleTranscriptCompleted(root);
                    break;
                case "error":
                    if (!TryHandleNonCriticalError(root))
                    {
                        Fail(new InvalidOperationException(BuildErrorMessage(root)));
                    }
                    break;
                default:
                    break;
            }
        }

        private void HandleCommittedEvent(JsonElement root)
        {
            if (!TryGetString(root, "item_id", out var itemId) || string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            TryGetString(root, "previous_item_id", out var previousItemId);

            lock (_syncRoot)
            {
                _committedEventCount++;

                if (!_items.TryGetValue(itemId, out var state))
                {
                    state = new TranscriptItemState();
                    _items[itemId] = state;
                }

                if (!state.IsCommitted)
                {
                    state.IsCommitted = true;
                    _pendingTranscriptItems++;
                    InsertItemOrder(itemId, previousItemId);
                }

                if (_pendingCommitRequests > 0)
                {
                    _pendingCommitRequests--;
                    _finalCommitRequested = false;
                }

                TryCompleteIfReadyLocked();
            }

            WhisperTrace.Log("RealtimeClient", $"Committed audio buffer for item {itemId}. Count={_committedEventCount}.");
        }

        private void HandleTranscriptDelta(JsonElement root)
        {
            if (!TryGetString(root, "item_id", out var itemId) || string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            if (!TryGetString(root, "delta", out var delta) || string.IsNullOrEmpty(delta))
            {
                return;
            }

            string aggregateText;

            lock (_syncRoot)
            {
                _deltaEventCount++;
                var state = GetOrCreateItemState(itemId);
                state.PartialTranscript.Append(delta);
                aggregateText = BuildAggregateTranscriptLocked();
            }

            WhisperTrace.Log(
                "RealtimeClient",
                $"Transcript delta received for item {itemId}. DeltaCount={_deltaEventCount} AggregateLength={aggregateText.Length}.");
            PublishTranscriptUpdated(aggregateText);
        }

        private void HandleTranscriptCompleted(JsonElement root)
        {
            if (!TryGetString(root, "item_id", out var itemId) || string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            TryGetString(root, "transcript", out var transcript);
            transcript = transcript?.Trim() ?? string.Empty;
            var logProbSummary = SummarizeLogProbs(root);

            string aggregateText;

            lock (_syncRoot)
            {
                _completedEventCount++;
                var state = GetOrCreateItemState(itemId);
                state.CompletedTranscript = transcript;

                if (!state.IsCompleted)
                {
                    state.IsCompleted = true;
                    if (_pendingTranscriptItems > 0)
                    {
                        _pendingTranscriptItems--;
                    }
                }

                aggregateText = BuildAggregateTranscriptLocked();
                TryCompleteIfReadyLocked();
            }

            WhisperTrace.Log(
                "RealtimeClient",
                $"Transcript item completed for {itemId}. CompletedCount={_completedEventCount} AggregateLength={aggregateText.Length}{logProbSummary}");
            PublishTranscriptUpdated(aggregateText);
        }

        private TranscriptItemState GetOrCreateItemState(string itemId)
        {
            if (!_items.TryGetValue(itemId, out var state))
            {
                state = new TranscriptItemState();
                _items[itemId] = state;

                if (!_itemOrder.Contains(itemId, StringComparer.Ordinal))
                {
                    _itemOrder.Add(itemId);
                }
            }

            return state;
        }

        private void InsertItemOrder(string itemId, string? previousItemId)
        {
            var existingIndex = _itemOrder.FindIndex(id =>
                string.Equals(id, itemId, StringComparison.Ordinal));

            if (existingIndex >= 0)
            {
                _itemOrder.RemoveAt(existingIndex);
            }

            if (!string.IsNullOrWhiteSpace(previousItemId))
            {
                var previousIndex = _itemOrder.FindIndex(id =>
                    string.Equals(id, previousItemId, StringComparison.Ordinal));

                if (previousIndex >= 0)
                {
                    _itemOrder.Insert(previousIndex + 1, itemId);
                    return;
                }
            }

            _itemOrder.Add(itemId);
        }

        private string BuildAggregateTranscriptLocked()
        {
            var segments = new List<string>(_itemOrder.Count);

            foreach (var itemId in _itemOrder)
            {
                if (!_items.TryGetValue(itemId, out var state))
                {
                    continue;
                }

                var text = !string.IsNullOrWhiteSpace(state.CompletedTranscript)
                    ? state.CompletedTranscript
                    : state.PartialTranscript.ToString();

                text = text.Trim();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    segments.Add(text);
                }
            }

            return string.Join(" ", segments).Trim();
        }

        private void TryCompleteIfReadyLocked()
        {
            if (!_completionRequested || _pendingCommitRequests > 0 || _pendingTranscriptItems > 0)
            {
                return;
            }

            var transcript = BuildAggregateTranscriptLocked();
            if (string.IsNullOrWhiteSpace(transcript))
            {
                _completionSource.TrySetException(
                    new InvalidOperationException("OpenAI returned an empty transcript."));
                return;
            }

            _completionSource.TrySetResult(new TranscriptResult(transcript, DateTimeOffset.UtcNow));
        }

        private async Task RequestFinalCommitAsync(CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                if (_finalCommitRequested)
                {
                    return;
                }

                _finalCommitRequested = true;
                _pendingCommitRequests++;
            }

            try
            {
                WhisperTrace.Log("RealtimeClient", "Requesting final realtime input buffer commit.");
                await SendEventAsync(
                    new
                    {
                        type = "input_audio_buffer.commit"
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_syncRoot)
                {
                    _finalCommitRequested = false;
                    if (_pendingCommitRequests > 0)
                    {
                        _pendingCommitRequests--;
                    }

                    TryCompleteIfReadyLocked();
                }

                throw;
            }
        }

        private void PublishTranscriptUpdated(string transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return;
            }

            TranscriptUpdated?.Invoke(this, new TranscriptUpdatedEventArgs(transcript));
        }

        private async Task SendEventAsync(object payload, CancellationToken cancellationToken)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);

            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (_webSocket.State != WebSocketState.Open)
                {
                    throw new InvalidOperationException("OpenAI realtime connection is not open.");
                }

                await _webSocket
                    .SendAsync(
                        bytes,
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
        }

        private void Fail(Exception exception)
        {
            WhisperTrace.Log("RealtimeClient", $"Realtime client failure: {exception.Message}");
            _sessionReady.TrySetException(exception);
            _completionSource.TrySetException(exception);
        }

        private bool TryHandleNonCriticalError(JsonElement root)
        {
            var message = BuildErrorMessage(root);
            if (!message.Contains("buffer too small", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            lock (_syncRoot)
            {
                if (!_completionRequested || !_finalCommitRequested)
                {
                    return false;
                }

                _finalCommitRequested = false;

                if (_pendingCommitRequests > 0)
                {
                    _pendingCommitRequests--;
                }

                WhisperTrace.Log("RealtimeClient", $"Ignoring empty final commit error: {message}");
                TryCompleteIfReadyLocked();
            }

            return true;
        }

        private static bool TryGetString(JsonElement element, string propertyName, out string? value)
        {
            if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString();
                return true;
            }

            value = null;
            return false;
        }

        private static string BuildErrorMessage(JsonElement root)
        {
            if (root.TryGetProperty("error", out var error))
            {
                if (TryGetString(error, "message", out var message) && !string.IsNullOrWhiteSpace(message))
                {
                    return message!;
                }
            }

            return "OpenAI realtime transcription request failed.";
        }

        private static string SummarizeLogProbs(JsonElement root)
        {
            if (!root.TryGetProperty("logprobs", out var logProbs)
                || logProbs.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            var tokenCount = 0;
            var totalLogProb = 0d;

            foreach (var token in logProbs.EnumerateArray())
            {
                if (!token.TryGetProperty("logprob", out var logProbElement)
                    || !logProbElement.TryGetDouble(out var logProb))
                {
                    continue;
                }

                tokenCount++;
                totalLogProb += logProb;
            }

            return tokenCount == 0
                ? string.Empty
                : $" AvgLogProb={(totalLogProb / tokenCount):F2} Tokens={tokenCount}.";
        }

        private sealed class TranscriptItemState
        {
            public StringBuilder PartialTranscript { get; } = new();
            public string? CompletedTranscript { get; set; }
            public bool IsCommitted { get; set; }
            public bool IsCompleted { get; set; }
        }
    }

    private sealed record CreateSessionAttemptResult(
        bool IsSuccess,
        string? ClientSecret,
        string? ErrorMessage,
        HttpStatusCode? StatusCode);
}
