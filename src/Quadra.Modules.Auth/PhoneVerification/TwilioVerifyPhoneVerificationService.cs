using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Configuration;

namespace Quadra.Modules.Auth.PhoneVerification;

/// <summary>
/// <see cref="IPhoneVerificationService"/> backed by the Twilio Verify v2 REST API, called
/// through <see cref="HttpClient"/> (no Twilio SDK). Translates Twilio responses into the
/// application-layer exceptions so handlers and the controller never see provider details.
/// </summary>
public sealed class TwilioVerifyPhoneVerificationService : IPhoneVerificationService
{
    public const string HttpClientName = "twilio-verify";

    private const string BaseUrl = "https://verify.twilio.com/v2/Services/";
    private const string StatusApproved = "approved";

    // https://www.twilio.com/docs/api/errors — Verify error codes this service distinguishes.
    private const int ErrorInvalidParameter = 60200;
    private const int ErrorMaxCheckAttempts = 60202;
    private const int ErrorMaxSendAttempts = 60203;
    private const int ErrorLandlineNotSupported = 60205;
    private const int ErrorInvalidPhoneNumber = 21211;
    private const int ErrorSmsNotSupportedInRegion = 60410;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<PhoneVerificationOptions> _options;
    private readonly ILogger<TwilioVerifyPhoneVerificationService> _logger;

    public TwilioVerifyPhoneVerificationService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<PhoneVerificationOptions> options,
        ILogger<TwilioVerifyPhoneVerificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        using var response = await SendAsync(
            "Verifications",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["To"] = phoneNumber,
                ["Channel"] = "sms",
            },
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorCode = await ReadErrorCodeAsync(response, cancellationToken);
        throw TranslateFailure("start", response.StatusCode, errorCode);
    }

    public async Task<PhoneVerificationResult> CheckAsync(
        string phoneNumber,
        string code,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        using var response = await SendAsync(
            "VerificationCheck",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["To"] = phoneNumber,
                ["Code"] = code,
            },
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var status = await ReadStringPropertyAsync(response, "status", cancellationToken);
            return string.Equals(status, StatusApproved, StringComparison.OrdinalIgnoreCase)
                ? PhoneVerificationResult.Approved
                : PhoneVerificationResult.InvalidCode;
        }

        // Twilio deletes a verification once it is approved, expires, or exhausts its attempts;
        // checking a code after that returns 404.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return PhoneVerificationResult.Expired;
        }

        var errorCode = await ReadErrorCodeAsync(response, cancellationToken);
        throw TranslateFailure("check", response.StatusCode, errorCode);
    }

    private async Task<HttpResponseMessage> SendAsync(
        string resource,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        var twilio = _options.CurrentValue.Twilio;
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{twilio.AccountSid}:{twilio.AuthToken}"));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BaseUrl}{Uri.EscapeDataString(twilio.VerifyServiceSid)}/{resource}")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var client = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PhoneVerificationUnavailableException("Twilio Verify could not be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PhoneVerificationUnavailableException("Twilio Verify timed out.", ex);
        }
    }

    private Exception TranslateFailure(string operation, HttpStatusCode statusCode, int? errorCode)
    {
        if (statusCode == HttpStatusCode.TooManyRequests
            || errorCode is ErrorMaxSendAttempts or ErrorMaxCheckAttempts)
        {
            return new PhoneVerificationThrottledException(
                "Too many verification attempts for this phone number. Try again later.");
        }

        if (errorCode is ErrorInvalidParameter
            or ErrorLandlineNotSupported
            or ErrorInvalidPhoneNumber
            or ErrorSmsNotSupportedInRegion)
        {
            return new PhoneNumberRejectedException("The phone number cannot receive an SMS code.");
        }

        // Includes 401/403 (bad credentials or service SID): a deployment problem, not the client's.
        _logger.LogError(
            "Twilio Verify {Operation} failed. {StatusCode} {TwilioErrorCode}",
            operation,
            (int)statusCode,
            errorCode);

        return new PhoneVerificationUnavailableException("The SMS provider returned an unexpected error.");
    }

    private static async Task<int?> ReadErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = await ReadJsonAsync(response, cancellationToken);
        return document is not null
            && document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("code", out var code)
            && code.ValueKind == JsonValueKind.Number
            && code.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static async Task<string?> ReadStringPropertyAsync(
        HttpResponseMessage response,
        string propertyName,
        CancellationToken cancellationToken)
    {
        using var document = await ReadJsonAsync(response, cancellationToken);
        return document is not null
            && document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static async Task<JsonDocument?> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
