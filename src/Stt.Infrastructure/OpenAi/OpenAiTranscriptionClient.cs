using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stt.Core.Abstractions;
using Stt.Core.Models;

namespace Stt.Infrastructure.OpenAi;

public sealed class OpenAiTranscriptionClient : ITranscriptionClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly Func<OpenAiTranscriptionOptions> _optionsAccessor;

    public OpenAiTranscriptionClient(
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

    public async Task<TranscriptResult> TranscribeAsync(
        CapturedAudioFile audioFile,
        CancellationToken cancellationToken)
    {
        var options = _optionsAccessor();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "Set an OpenAI API key in Settings before recording.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "v1/audio/transcriptions");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        using var requestContent = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(audioFile.FilePath);
        using var streamContent = new StreamContent(fileStream);

        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(audioFile.ContentType);

        requestContent.Add(new StringContent(options.TranscriptionModel), "model");
        requestContent.Add(streamContent, "file", audioFile.FileName);

        request.Content = requestContent;

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(BuildErrorMessage(payload, response.ReasonPhrase));
        }

        var result = JsonSerializer.Deserialize<OpenAiTranscriptionResponse>(payload, SerializerOptions);
        var transcriptText = result?.Text?.Trim();

        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            throw new InvalidOperationException("OpenAI returned an empty transcript.");
        }

        return new TranscriptResult(transcriptText, DateTimeOffset.UtcNow);
    }

    private static string BuildErrorMessage(string payload, string? fallbackReason)
    {
        try
        {
            var errorEnvelope = JsonSerializer.Deserialize<OpenAiErrorEnvelope>(payload, SerializerOptions);
            if (!string.IsNullOrWhiteSpace(errorEnvelope?.Error?.Message))
            {
                return errorEnvelope.Error.Message;
            }
        }
        catch
        {
            // Fall back to the HTTP status reason if JSON parsing fails.
        }

        return string.IsNullOrWhiteSpace(fallbackReason)
            ? "OpenAI transcription request failed."
            : $"OpenAI transcription request failed: {fallbackReason}";
    }

    private sealed record OpenAiTranscriptionResponse(
        [property: JsonPropertyName("text")] string? Text);

    private sealed record OpenAiErrorEnvelope(
        [property: JsonPropertyName("error")] OpenAiError? Error);

    private sealed record OpenAiError(
        [property: JsonPropertyName("message")] string? Message);
}
