using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Quadra.Modules.Gamification.Billing;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Season pass bought on Google Play (DECISIONS #109): the paid track opens only after Google confirms
/// a paid purchase made for the caller's account; a token cannot be reused by another account, a
/// refund closes the track again, and nothing opens while billing is off.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class SeasonPassPurchaseAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private const string Product = "passe_t1";

    private readonly AuditWebApplicationFactory _fx;
    private readonly IGooglePlayPurchases _play = Substitute.For<IGooglePlayPurchases>();
    private readonly WebApplicationFactory<Program> _host;

    public SeasonPassPurchaseAuditTests(AuditWebApplicationFactory fx)
    {
        _fx = fx;
        _host = fx.Create("Testing", new Dictionary<string, string?>
        {
            ["SeasonPass:PremiumForEveryone"] = "false",
            ["SeasonPass:Billing:ServiceAccountJson"] = "{}",
            ["SeasonPass:Billing:ProductIds:T1"] = Product,
            ["SeasonPass:Billing:VoidedSweepMinutes"] = "0",
        }).WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IGooglePlayPurchases>();
            s.AddSingleton(_play);
        }));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void GoogleSays(string token, Guid buyer, int state = 0, int acknowledged = 0) =>
        _play.GetAsync(Product, token, Arg.Any<CancellationToken>())
            .Returns(new PlayProductPurchase(state, acknowledged, "GPA.1234-5678", "1795000000000", SeasonPassPurchases.AccountTag(buyer), null));

    [Fact]
    public async Task The_pass_opens_only_after_Google_confirms_and_is_acknowledged()
    {
        var me = await _fx.LoginAsync(_host);
        var before = await Read(await me.Client.GetAsync("/api/v1/season-pass", Ct));
        before.GetProperty("premium").GetBoolean().Should().BeFalse();
        before.GetProperty("productId").GetString().Should().Be(Product);
        before.GetProperty("purchaseAccountTag").GetString().Should().Be(SeasonPassPurchases.AccountTag(me.Id));

        GoogleSays("tok-ok-1", me.Id);
        var bought = await me.Client.PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = Product, purchaseToken = "tok-ok-1" }, Ct);
        bought.StatusCode.Should().Be(HttpStatusCode.OK);
        await _play.Received(1).AcknowledgeAsync(Product, "tok-ok-1", Arg.Any<CancellationToken>());

        var after = await Read(await me.Client.GetAsync("/api/v1/season-pass", Ct));
        after.GetProperty("premium").GetBoolean().Should().BeTrue();
        after.GetProperty("productId").ValueKind.Should().Be(JsonValueKind.Null, "nothing left to buy");

        // Sending the same purchase again (retry, app restarted) answers the same.
        (await me.Client.PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = Product, purchaseToken = "tok-ok-1" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Another_account_cannot_claim_a_purchase()
    {
        var buyer = await _fx.LoginAsync(_host);
        var thief = await _fx.LoginAsync(_host);
        GoogleSays("tok-shared", buyer.Id);

        // Made for the buyer's account: refused for anyone else, even before the buyer sends it.
        (await thief.Client.PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = Product, purchaseToken = "tok-shared" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await buyer.Client.PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = Product, purchaseToken = "tok-shared" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await thief.Client.PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = Product, purchaseToken = "tok-shared" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await Read(await thief.Client.GetAsync("/api/v1/season-pass", Ct))).GetProperty("premium").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_cancelled_or_pending_purchases_open_nothing()
    {
        var me = await _fx.LoginAsync(_host);
        _play.GetAsync(Product, "tok-fake", Arg.Any<CancellationToken>()).Returns((PlayProductPurchase?)null);
        GoogleSays("tok-cancelled", me.Id, state: 1);
        GoogleSays("tok-pending", me.Id, state: 2);

        (await me.Client.PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = Product, purchaseToken = "tok-fake" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await me.Client.PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = Product, purchaseToken = "tok-cancelled" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await me.Client.PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = Product, purchaseToken = "tok-pending" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await me.Client.PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = "outro_produto", purchaseToken = "tok-pending" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await me.Client.PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = Product, purchaseToken = "<script>" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await Read(await me.Client.GetAsync("/api/v1/season-pass", Ct))).GetProperty("premium").GetBoolean().Should().BeFalse();
        await _play.DidNotReceive().AcknowledgeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_refunded_purchase_closes_the_pass()
    {
        var me = await _fx.LoginAsync(_host);
        GoogleSays("tok-refund", me.Id, acknowledged: 1);
        (await me.Client.PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = Product, purchaseToken = "tok-refund" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        _play.ListVoidedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new PlayVoidedPurchase("tok-refund", "GPA.1234-5678", "1795000000000")]);

        using (var scope = _host.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<SeasonPassPurchases>().SweepVoidedAsync(Ct)).Should().Be(1);
        }

        (await Read(await me.Client.GetAsync("/api/v1/season-pass", Ct))).GetProperty("premium").GetBoolean().Should().BeFalse();
        (await me.Client.PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = Product, purchaseToken = "tok-refund" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "a refunded purchase cannot be sent again");
    }

    [Fact]
    public async Task With_billing_off_nothing_is_on_sale()
    {
        var off = _fx.Create("Testing", new Dictionary<string, string?> { ["SeasonPass:PremiumForEveryone"] = "false" });
        var me = await _fx.LoginAsync(off);

        (await Read(await me.Client.GetAsync("/api/v1/season-pass", Ct))).GetProperty("productId").ValueKind.Should().Be(JsonValueKind.Null);
        (await me.Client.PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = Product, purchaseToken = "tok-x" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Anonymous_callers_cannot_buy()
    {
        (await _host.CreateClient().PostAsJsonAsync("/api/v1/season-pass/purchases", new { productId = Product, purchaseToken = "tok" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
