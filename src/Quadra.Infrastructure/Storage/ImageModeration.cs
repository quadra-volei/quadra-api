using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Quadra.Infrastructure.Storage;

/// <summary>
/// Adult-content check of an uploaded photo (DECISIONS #76). Runs before the photo's key is
/// saved, so a refused photo is never shown to anyone. Off by default.
/// </summary>
public interface IImageModerator
{
    /// <summary>
    /// True when the image at <paramref name="imageUrl"/> (a short-lived read URL) may be shown.
    /// False when it is refused or could not be checked (the upload is then refused: fail closed).
    /// </summary>
    Task<bool> IsAllowedAsync(string imageUrl, CancellationToken cancellationToken);
}

/// <summary>Bound from <c>Moderation:Images</c>.</summary>
public sealed class ImageModerationOptions
{
    public const string SectionName = "Moderation:Images";
    public const string None = "None";
    public const string GoogleVision = "GoogleVision";

    /// <summary><c>None</c> (default: every photo passes) or <c>GoogleVision</c> (SafeSearch).</summary>
    public string Provider { get; set; } = None;

    /// <summary>Google Cloud Vision API key. A secret — Render dashboard only.</summary>
    public string? GoogleVisionApiKey { get; set; }

    public bool UsesGoogleVision =>
        string.Equals(Provider, GoogleVision, StringComparison.OrdinalIgnoreCase);
}

/// <summary>No check: every photo passes. The default until a provider is configured.</summary>
public sealed class NoImageModerator : IImageModerator
{
    public Task<bool> IsAllowedAsync(string imageUrl, CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>
/// Google Cloud Vision SafeSearch: refuses adult content rated LIKELY or VERY_LIKELY and racy or
/// violent content rated VERY_LIKELY. Vision fetches the image from the read URL itself.
/// </summary>
public sealed class GoogleVisionImageModerator : IImageModerator
{
    private const string Endpoint = "https://vision.googleapis.com/v1/images:annotate";

    // ponytail: one shared client (no IHttpClientFactory in Infrastructure); move to a typed
    // client if this ever needs handlers or per-call configuration.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly HttpClient _http;
    private readonly ImageModerationOptions _options;
    private readonly ILogger<GoogleVisionImageModerator> _logger;

    public GoogleVisionImageModerator(
        IOptions<ImageModerationOptions> options,
        ILogger<GoogleVisionImageModerator> logger,
        HttpClient? http = null)
    {
        _http = http ?? Http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> IsAllowedAsync(string imageUrl, CancellationToken cancellationToken)
    {
        var body = new
        {
            requests = new[]
            {
                new
                {
                    image = new { source = new { imageUri = imageUrl } },
                    features = new[] { new { type = "SAFE_SEARCH_DETECTION" } },
                },
            },
        };

        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"{Endpoint}?key={Uri.EscapeDataString(_options.GoogleVisionApiKey ?? string.Empty)}", body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Image moderation unavailable: Vision answered {Status}.", (int)response.StatusCode);
                return false;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return IsAllowed(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Image moderation unavailable.");
            return false;
        }
    }

    /// <summary>Reads a Vision <c>images:annotate</c> answer; anything unreadable is refused.</summary>
    public static bool IsAllowed(JsonElement response)
    {
        if (!response.TryGetProperty("responses", out var responses)
            || responses.GetArrayLength() == 0
            || !responses[0].TryGetProperty("safeSearchAnnotation", out var safe))
        {
            return false;
        }

        static string Rating(JsonElement safe, string name) =>
            safe.TryGetProperty(name, out var value) ? value.GetString() ?? "UNKNOWN" : "UNKNOWN";

        var adult = Rating(safe, "adult");
        return adult is not ("LIKELY" or "VERY_LIKELY")
            && Rating(safe, "racy") != "VERY_LIKELY"
            && Rating(safe, "violence") != "VERY_LIKELY";
    }
}
