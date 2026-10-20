using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Configuration;
using Quadra.Modules.Auth.PhoneVerification;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for the two <see cref="IPhoneVerificationService"/> implementations: the fixed-code
/// fake and the Twilio Verify client (exercised against a stubbed HTTP handler — no network).
/// </summary>
public sealed class PhoneVerificationServiceTests
{
    private const string Phone = "+5511999990000";

    // ---------------- fake ----------------

    private static FakePhoneVerificationService CreateFake(string? onlyPhoneNumber = null) =>
        new(AuthTestSupport.Monitor(new PhoneVerificationOptions
        {
            Provider = PhoneVerificationOptions.ProviderFake,
            Fake = new FakePhoneVerificationOptions { Code = "654321", PhoneNumber = onlyPhoneNumber },
        }));

    /// <summary>
    /// Covers FA.3: "fake implementation with a fixed, configurable code".
    /// </summary>
    [Theory]
    [InlineData("654321", PhoneVerificationResult.Approved)]
    [InlineData("123456", PhoneVerificationResult.InvalidCode)]
    public async Task Fake_approves_only_the_configured_code(string code, PhoneVerificationResult expected)
    {
        var sut = CreateFake();

        await sut.StartAsync(Phone, TestContext.Current.CancellationToken);
        var result = await sut.CheckAsync(Phone, code, TestContext.Current.CancellationToken);

        result.Should().Be(expected);
    }

    /// <summary>
    /// Covers FA.3: "fake implementation with a fixed, configurable number" — when a number is
    /// configured, every other number is refused.
    /// </summary>
    [Fact]
    public async Task Fake_with_configured_number_rejects_other_numbers()
    {
        var sut = CreateFake(onlyPhoneNumber: Phone);

        await sut.StartAsync(Phone, TestContext.Current.CancellationToken);

        var start = async () => await sut.StartAsync("+5511888880000", TestContext.Current.CancellationToken);
        var check = async () => await sut.CheckAsync("+5511888880000", "654321", TestContext.Current.CancellationToken);

        await start.Should().ThrowAsync<PhoneNumberRejectedException>();
        await check.Should().ThrowAsync<PhoneNumberRejectedException>();
    }

    // ---------------- Twilio Verify ----------------

    private static (TwilioVerifyPhoneVerificationService Sut, StubHandler Handler) CreateTwilio(
        HttpStatusCode statusCode,
        string body)
    {
        var handler = new StubHandler(statusCode, body);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(TwilioVerifyPhoneVerificationService.HttpClientName)
            .Returns(_ => new HttpClient(handler, disposeHandler: false));

        var options = AuthTestSupport.Monitor(new PhoneVerificationOptions
        {
            Provider = PhoneVerificationOptions.ProviderTwilio,
            Twilio = new TwilioVerifyOptions
            {
                AccountSid = "ACtest",
                AuthToken = "secret",
                VerifyServiceSid = "VAtest",
            },
        });

        var sut = new TwilioVerifyPhoneVerificationService(
            factory,
            options,
            NullLogger<TwilioVerifyPhoneVerificationService>.Instance);
        return (sut, handler);
    }

    /// <summary>
    /// Covers FA.3: "start a Twilio Verify verification" — POSTs To + Channel=sms to the service's
    /// Verifications resource with HTTP Basic credentials.
    /// </summary>
    [Fact]
    public async Task Twilio_start_posts_an_sms_verification()
    {
        var (sut, handler) = CreateTwilio(HttpStatusCode.Created, """{"status":"pending"}""");

        await sut.StartAsync(Phone, TestContext.Current.CancellationToken);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.Uri.Should().Be("https://verify.twilio.com/v2/Services/VAtest/Verifications");
        handler.Body.Should().Be("To=%2B5511999990000&Channel=sms");
        handler.Authorization.Should().Be(
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ACtest:secret")));
    }

    /// <summary>
    /// Covers FA.3: "check the code" — an approved status approves; a still-pending status means
    /// the code was wrong.
    /// </summary>
    [Theory]
    [InlineData("approved", PhoneVerificationResult.Approved)]
    [InlineData("pending", PhoneVerificationResult.InvalidCode)]
    public async Task Twilio_check_maps_the_verification_status(string status, PhoneVerificationResult expected)
    {
        var (sut, handler) = CreateTwilio(HttpStatusCode.OK, $$"""{"status":"{{status}}"}""");

        var result = await sut.CheckAsync(Phone, "123456", TestContext.Current.CancellationToken);

        result.Should().Be(expected);
        handler.Uri.Should().Be("https://verify.twilio.com/v2/Services/VAtest/VerificationCheck");
        handler.Body.Should().Be("To=%2B5511999990000&Code=123456");
    }

    /// <summary>
    /// Covers FA.3: Twilio answers 404 once a verification expired or was already approved.
    /// </summary>
    [Fact]
    public async Task Twilio_check_maps_not_found_to_expired()
    {
        var (sut, _) = CreateTwilio(HttpStatusCode.NotFound, """{"code":20404,"status":404}""");

        var result = await sut.CheckAsync(Phone, "123456", TestContext.Current.CancellationToken);

        result.Should().Be(PhoneVerificationResult.Expired);
    }

    /// <summary>
    /// Covers FA.3: rate limits (HTTP 429 or Verify error 60203 "max send attempts") → throttled.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, """{"code":20429}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"code":60203}""")]
    public async Task Twilio_start_maps_rate_limits_to_throttled(HttpStatusCode statusCode, string body)
    {
        var (sut, _) = CreateTwilio(statusCode, body);

        var act = async () => await sut.StartAsync(Phone, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<PhoneVerificationThrottledException>();
    }

    /// <summary>
    /// Covers FA.3: an invalid / unsupported phone number → rejected (422).
    /// </summary>
    [Fact]
    public async Task Twilio_start_maps_invalid_number_to_rejected()
    {
        var (sut, _) = CreateTwilio(HttpStatusCode.BadRequest, """{"code":60200,"message":"Invalid parameter `To`"}""");

        var act = async () => await sut.StartAsync(Phone, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<PhoneNumberRejectedException>();
    }

    /// <summary>
    /// Covers FA.3: bad credentials or an unexpected/non-JSON error → unavailable (502).
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"code":20003}""")]
    [InlineData(HttpStatusCode.InternalServerError, "<html>oops</html>")]
    public async Task Twilio_maps_other_failures_to_unavailable(HttpStatusCode statusCode, string body)
    {
        var (sut, _) = CreateTwilio(statusCode, body);

        var act = async () => await sut.CheckAsync(Phone, "123456", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<PhoneVerificationUnavailableException>();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StubHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public HttpMethod? Method { get; private set; }

        public string? Uri { get; private set; }

        public string? Body { get; private set; }

        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
