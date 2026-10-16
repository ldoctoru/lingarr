using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Models;
using Lingarr.Contracts.Plugins;
using Lingarr.Contracts.Settings;
using Lingarr.Contracts.Translation;
using Lingarr.Plugin.Cloudflare.Models;
using Microsoft.Extensions.Logging;

namespace Lingarr.Plugin.Cloudflare;

[PluginProvider("cloudflare")]
public sealed class CloudflareTranslator : ITranslationService, IConnectionTestable, IDisposable
{
    public const string DefaultModel = "@cf/meta/m2m100-1.2b";
    private const string M2M100Model = "@cf/meta/m2m100-1.2b";

    // ISO 639-1 codes supported by m2m100 (https://huggingface.co/facebook/m2m100_1.2B).
    private static readonly string[] M2M100Languages =
    [
        "af", "am", "ar", "ast", "az", "ba", "be", "bg", "bn", "br", "bs", "ca", "ceb", "cs", "cy", "da",
        "de", "el", "en", "es", "et", "fa", "ff", "fi", "fr", "fy", "ga", "gd", "gl", "gu", "ha", "he",
        "hi", "hr", "ht", "hu", "hy", "id", "ig", "ilo", "is", "it", "ja", "jv", "ka", "kk", "km", "kn",
        "ko", "lb", "lg", "ln", "lo", "lt", "lv", "mg", "mk", "ml", "mn", "mr", "ms", "my", "ne", "nl",
        "no", "ns", "oc", "or", "pa", "pl", "ps", "pt", "ro", "ru", "sd", "si", "sk", "sl", "so", "sq",
        "sr", "ss", "su", "sv", "sw", "ta", "th", "tl", "tn", "tr", "uk", "ur", "uz", "vi", "wo", "xh",
        "yi", "yo", "zh", "zu"
    ];

    private readonly ISettingsAccess _settings;
    private readonly ILogger<CloudflareTranslator> _logger;
    private readonly HttpClient _httpClient = new();

    private string? _model;

    public CloudflareTranslator(ISettingsAccess settings, ILogger<CloudflareTranslator> logger)
    {
        _settings = settings;
        _logger = logger;
        // Timeouts are applied per request because HttpClient.Timeout cannot change after first use.
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string? ModelName => _model;

    private sealed record CloudflareConfig(
        string AccountId,
        string ApiToken,
        string Model,
        TranslationHttpSettings Http);

    /// <summary>
    /// Reads the configuration on every call so changes made in the settings UI apply
    /// without restarting Lingarr.
    /// </summary>
    private async Task<CloudflareConfig> LoadConfigAsync()
    {
        var accountId = await _settings.GetSettingAsync("cloudflare_account_id");
        var apiToken = await _settings.GetEncryptedSettingAsync("cloudflare_api_token");

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(apiToken))
        {
            throw new InvalidOperationException(
                "Cloudflare Workers AI plugin requires both an account ID and an API token.");
        }

        var configuredModel = await _settings.GetSettingAsync("cloudflare_model");
        var model = !string.IsNullOrWhiteSpace(configuredModel) ? configuredModel : DefaultModel;
        _model = model;

        return new CloudflareConfig(accountId.Trim(), apiToken.Trim(), model, await _settings.GetHttpSettingsAsync());
    }

    public async Task<List<SourceLanguage>> GetLanguages()
    {
        var configuredModel = await _settings.GetSettingAsync("cloudflare_model");
        var model = !string.IsNullOrWhiteSpace(configuredModel) ? configuredModel : DefaultModel;

        // Only m2m100's language set is listed; other models fall back to free-form codes.
        if (!string.Equals(model, M2M100Model, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return M2M100Languages
            .Select(code => new SourceLanguage
            {
                Name = code,
                Code = code,
                Targets = M2M100Languages.Where(target => target != code).ToList()
            })
            .ToList();
    }

    public async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        List<string>? contextLinesBefore,
        List<string>? contextLinesAfter,
        CancellationToken cancellationToken)
    {
        var config = await LoadConfigAsync();

        var delay = config.Http.RetryDelay;

        for (var attempt = 1; attempt <= config.Http.MaxRetries; attempt++)
        {
            try
            {
                return await TranslateWithCloudflare(config, text, sourceLanguage, targetLanguage, cancellationToken);
            }
            catch (CloudflareRetryableException exception)
            {
                if (attempt == config.Http.MaxRetries)
                {
                    _logger.LogError(
                        exception,
                        "Cloudflare Workers AI retries exhausted ({StatusCode}).",
                        exception.StatusCode);
                    throw new TranslationException(
                        $"Retry limit reached after {exception.StatusCode}.",
                        exception);
                }

                // Prefer the server's Retry-After hint when it is longer than our own backoff.
                var wait = exception.RetryAfter is { } retryAfter && retryAfter > delay ? retryAfter : delay;

                _logger.LogWarning(
                    "Cloudflare Workers AI received {StatusCode}. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    exception.StatusCode,
                    wait,
                    attempt,
                    config.Http.MaxRetries);

                await Task.Delay(wait, cancellationToken);
                delay = TimeSpan.FromTicks(delay.Ticks * config.Http.RetryDelayMultiplier);
            }
        }

        throw new TranslationException("Cloudflare Workers AI failed after maximum retry attempts.");
    }

    public async Task<PluginConnectionResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        CloudflareConfig config;
        try
        {
            config = await LoadConfigAsync();
        }
        catch (InvalidOperationException exception)
        {
            return PluginConnectionResult.Fail(exception.Message);
        }

        try
        {
            // A one-word translation verifies the account ID, token permissions and model in one call.
            var translated = await TranslateWithCloudflare(config, "Hello", "en", "fr", cancellationToken);
            return PluginConnectionResult.Ok($"Connected to {config.Model} (\"Hello\" → \"{translated}\").");
        }
        catch (CloudflareRetryableException exception)
        {
            return PluginConnectionResult.Fail(
                $"Cloudflare is rate limiting or unavailable ({exception.StatusCode}); credentials could not be verified.");
        }
        catch (TranslationException exception)
        {
            return PluginConnectionResult.Fail(exception.Message);
        }
    }

    private async Task<string> TranslateWithCloudflare(
        CloudflareConfig config,
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.cloudflare.com/client/v4/accounts/{config.AccountId}/ai/run/{config.Model}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", config.ApiToken) },
            Content = JsonContent.Create(new CloudflareRequest
            {
                Text = text,
                SourceLang = sourceLanguage,
                TargetLang = targetLanguage
            })
        };

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(config.Http.Timeout);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException($"Cloudflare Workers AI request timed out after {config.Http.Timeout}.");
        }

        using var _ = response;

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable)
            {
                throw new CloudflareRetryableException(response.StatusCode, GetRetryAfter(response));
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new TranslationException(
                $"Cloudflare Workers AI request failed with status {response.StatusCode}: {body}");
        }

        var cloudflareResponse = await response.Content.ReadFromJsonAsync<CloudflareResponse>(cancellationToken);
        if (cloudflareResponse is null || !cloudflareResponse.Success)
        {
            var firstApiError = cloudflareResponse?.Errors.FirstOrDefault();
            var errorDetail = firstApiError is not null
                ? $"{firstApiError.Code}: {firstApiError.Message}"
                : "unknown error";
            throw new TranslationException($"Cloudflare Workers AI request failed ({errorDetail}).");
        }

        var translatedText = cloudflareResponse.Result?.TranslatedText;
        if (string.IsNullOrEmpty(translatedText))
        {
            throw new TranslationException("Cloudflare Workers AI returned an empty translation.");
        }

        return translatedText;
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : null;
        }

        return null;
    }

    public Task<ModelsResponse> GetModels()
    {
        return Task.FromResult(new ModelsResponse
        {
            Options =
            [
                new LabelValue { Label = "m2m100 (1.2B)", Value = "@cf/meta/m2m100-1.2b" }
            ]
        });
    }

    public Task<LanguagePair?> GetLanguagePair(
        string requestedSource,
        string requestedTarget,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<LanguagePair?>(new LanguagePair
        {
            Source = requestedSource,
            Target = requestedTarget,
            Tier = MatchTier.Exact
        });
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed class CloudflareRetryableException(HttpStatusCode statusCode, TimeSpan? retryAfter)
        : Exception($"Cloudflare Workers AI returned {statusCode}")
    {
        public HttpStatusCode StatusCode { get; } = statusCode;
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }
}
