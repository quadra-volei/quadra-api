using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// SMS login and the Places proxy are rate limited (audit SEC-07): per phone and per IP on
/// <c>login/sms-otp</c>, per account on <c>places/*</c>.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class RateLimitAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public RateLimitAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task R8_sms_otp_initiate_is_rate_limited()
    {
        var client = _fx.Factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 30; i++)
        {
            var r = await client.PostAsJsonAsync(
                "/api/v1/auth/login/sms-otp", new { step = "initiate", phoneNumber = "+5511987659999" }, Ct);
            statuses.Add(r.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests, "30 SMS requests in a row for one number should be throttled");
    }

    [Fact]
    public async Task R8_otp_verify_is_rate_limited()
    {
        var client = _fx.Factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 30; i++)
        {
            var r = await client.PostAsJsonAsync(
                "/api/v1/auth/login/sms-otp",
                new { step = "verify", phoneNumber = "+5511987658888", code = (100000 + i).ToString() }, Ct);
            statuses.Add(r.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests, "30 wrong codes in a row should be throttled by the API itself");
    }

    // After the lockout even the right code is refused: guessing the code is no longer possible.
    [Fact]
    public async Task R8_locked_out_number_is_refused_even_with_the_right_code()
    {
        var client = _fx.Factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync(
                "/api/v1/auth/login/sms-otp", new { step = "verify", phoneNumber = "+5511987657777", code = "000000" }, Ct);
        }

        var right = await client.PostAsJsonAsync(
            "/api/v1/auth/login/sms-otp",
            new { step = "verify", phoneNumber = "+5511987657777", code = Modules.Auth.TestJwtFactory.FakeOtpCode }, Ct);
        right.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task R8_sms_otp_is_rate_limited_per_ip_across_numbers()
    {
        using var host = _fx.Create("Testing", new Dictionary<string, string?>
        {
            ["Auth:RateLimits:SmsOtpPerIpPerHour"] = "10",
        });
        var client = host.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 15; i++)
        {
            var r = await client.PostAsJsonAsync(
                "/api/v1/auth/login/sms-otp", new { step = "initiate", phoneNumber = $"+55119876{i:D5}" }, Ct);
            statuses.Add(r.StatusCode);
        }

        statuses.Take(10).Should().AllSatisfy(s => s.Should().Be(HttpStatusCode.OK));
        statuses.Skip(10).Should().AllSatisfy(s => s.Should().Be(HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task R8_places_autocomplete_is_rate_limited()
    {
        var user = await _fx.LoginAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 100).Select(i =>
            user.Client.GetAsync($"/api/v1/places/autocomplete?q=rua+{i}&sessionToken=s{i}", Ct)));
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.TooManyRequests,
            $"100 autocomplete calls from one account in a burst: {Codes(responses)}");

        // Another account still gets through: the limit is per account, not global.
        var other = await _fx.LoginAsync();
        (await other.Client.GetAsync("/api/v1/places/autocomplete?q=rua+x&sessionToken=t1", Ct))
            .StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    private static string Codes(IEnumerable<HttpResponseMessage> responses) =>
        string.Join(",", responses.GroupBy(r => (int)r.StatusCode).Select(g => $"{g.Key}x{g.Count()}"));
}
