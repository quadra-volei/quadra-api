using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Quadra.Modules.Gamification.Billing;

namespace Quadra.UnitTests.Modules.Gamification;

/// <summary>The service-account JWT the API presents to Google for a Play Developer API token (DECISIONS #109).</summary>
public sealed class GooglePlayAssertionTests
{
    [Fact]
    public void The_assertion_is_an_RS256_JWT_signed_by_the_service_account_for_the_androidpublisher_scope()
    {
        using var rsa = RSA.Create(2048);
        var key = new GooglePlayPurchasesClient.ServiceAccountKey("play@quadra.iam.gserviceaccount.com", rsa.ExportPkcs8PrivateKeyPem(), null);
        var now = DateTimeOffset.FromUnixTimeSeconds(1_795_000_000);

        var parts = GooglePlayPurchasesClient.SignedAssertion(key, now).Split('.');

        parts.Should().HaveCount(3);
        var claims = JsonDocument.Parse(Decode(parts[1])).RootElement;
        claims.GetProperty("iss").GetString().Should().Be("play@quadra.iam.gserviceaccount.com");
        claims.GetProperty("scope").GetString().Should().Be("https://www.googleapis.com/auth/androidpublisher");
        claims.GetProperty("aud").GetString().Should().Be("https://oauth2.googleapis.com/token");
        claims.GetProperty("exp").GetInt64().Should().BeGreaterThan(claims.GetProperty("iat").GetInt64());
        JsonDocument.Parse(Decode(parts[0])).RootElement.GetProperty("alg").GetString().Should().Be("RS256");
        rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .Should().BeTrue();
    }

    [Fact]
    public void The_account_tag_is_stable_per_account_and_does_not_reveal_the_id()
    {
        var id = Guid.NewGuid();

        SeasonPassPurchases.AccountTag(id).Should().Be(SeasonPassPurchases.AccountTag(id))
            .And.NotBe(SeasonPassPurchases.AccountTag(Guid.NewGuid()))
            .And.NotContain(id.ToString("N"))
            .And.HaveLength(64, "Play accepts at most 64 characters");
    }

    [Theory]
    [InlineData("passe_t1", true)]
    [InlineData("passe.temporada.1", true)]
    [InlineData("Passe-T1", false)]
    [InlineData("", false)]
    public void Product_ids_follow_the_Play_Console_rules(string id, bool valid)
    {
        new PlayBillingOptions { ProductIds = { ["T1"] = id } }.IsValid().Should().Be(valid);
    }

    private static byte[] Decode(string part)
    {
        var s = part.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
    }
}
