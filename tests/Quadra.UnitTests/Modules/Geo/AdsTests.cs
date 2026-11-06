using FluentAssertions;
using NetTopologySuite.Geometries;
using Quadra.Modules.Geo.Ads;

namespace Quadra.UnitTests.Modules.Geo;

/// <summary>Self-service ads (DECISIONS #102): price, where an ad is in its period, and what a request may carry.</summary>
public sealed class AdsTests
{
    private static readonly AdsOptions Options = new();

    [Theory]
    [InlineData(AdPlacements.Home, 10, 1, 5900)]
    [InlineData(AdPlacements.Home, 10, 4, 23600)]
    [InlineData(AdPlacements.Map, 5, 1, 1700)]   // 29 × 0.6 = 17,40 → R$ 17
    [InlineData(AdPlacements.Map, 20, 2, 10400)] // 29 × 1.8 × 2 = 104,40 → R$ 104
    [InlineData(AdPlacements.Opening, 10, 1, 6900)]
    public void Price_is_the_weekly_price_times_radius_and_weeks_in_whole_reais(string placement, int radiusKm, int weeks, int expected) =>
        AdPricing.PriceCents(Options, placement, radiusKm, weeks).Should().Be(expected);

    [Fact]
    public void An_unknown_placement_or_radius_has_no_price()
    {
        AdPricing.PriceCents(Options, "Explore", 10, 1).Should().BeNull();
        AdPricing.PriceCents(Options, AdPlacements.Home, 7, 1).Should().BeNull();
        AdPricing.PriceCents(new AdsOptions { PricePerWeekCents = new() { [AdPlacements.Home] = 0 } }, AdPlacements.Home, 10, 1)
            .Should().BeNull("a placement priced at zero is off sale");
    }

    [Fact]
    public void An_approved_ad_is_scheduled_then_live_then_ended()
    {
        var start = new DateOnly(2026, 11, 10);
        var ad = new Ad { Status = AdStatuses.Approved, StartsOn = start, Weeks = 1, EndsOn = start.AddDays(7), Location = new Point(0, 0) };

        ad.Phase(start.AddDays(-1)).Should().Be(AdPhases.Scheduled);
        ad.Phase(start).Should().Be(AdPhases.Live);
        ad.Phase(start.AddDays(6)).Should().Be(AdPhases.Live);
        ad.Phase(start.AddDays(7)).Should().Be(AdPhases.Ended);
    }

    [Theory]
    [InlineData(AdStatuses.AwaitingPayment)]
    [InlineData(AdStatuses.InReview)]
    [InlineData(AdStatuses.Rejected)]
    [InlineData(AdStatuses.Cancelled)]
    public void An_ad_not_approved_shows_its_status(string status)
    {
        var start = new DateOnly(2026, 11, 10);
        new Ad { Status = status, StartsOn = start, Weeks = 1, EndsOn = start.AddDays(7) }.Phase(start).Should().Be(status);
    }

    private static CreateAdRequest Valid() => new(
        AdPlacements.Home, -23.5, -46.6, 10, new DateOnly(2026, 11, 10), 1, "Arena Sol", "Day use R$ 30", null, AdLinkKinds.None, null);

    [Fact]
    public void A_complete_request_is_valid() =>
        new CreateAdRequestValidator().Validate(Valid()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(AdLinkKinds.Url, "https://arenasol.com.br", true)]
    [InlineData(AdLinkKinds.Url, "http://arenasol.com.br", false)]
    [InlineData(AdLinkKinds.Url, "javascript:alert(1)", false)]
    [InlineData(AdLinkKinds.WhatsApp, "5511999998888", true)]
    [InlineData(AdLinkKinds.WhatsApp, "+55 11 99999", false)]
    [InlineData(AdLinkKinds.Arena, "not-a-guid", false)]
    [InlineData(AdLinkKinds.None, "https://x.com", false)]
    [InlineData("Phone", "123", false)]
    public void Links_are_checked_by_kind(string kind, string value, bool valid) =>
        new CreateAdRequestValidator().Validate(Valid() with { LinkKind = kind, LinkValue = value }).IsValid.Should().Be(valid);

    [Fact]
    public void Placement_radius_weeks_and_title_are_checked()
    {
        var validator = new CreateAdRequestValidator();
        validator.Validate(Valid() with { Placement = "Explore" }).IsValid.Should().BeFalse();
        validator.Validate(Valid() with { RadiusKm = 15 }).IsValid.Should().BeFalse();
        validator.Validate(Valid() with { Weeks = 3 }).IsValid.Should().BeFalse();
        validator.Validate(Valid() with { Title = "ab" }).IsValid.Should().BeFalse();
        validator.Validate(Valid() with { Title = new string('a', Ad.TitleMaxLength + 1) }).IsValid.Should().BeFalse();
        validator.Validate(Valid() with { Body = new string('a', Ad.BodyMaxLength + 1) }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Refusing_needs_a_reason()
    {
        new ReviewAdRequestValidator().Validate(new ReviewAdRequest(false, null)).IsValid.Should().BeFalse();
        new ReviewAdRequestValidator().Validate(new ReviewAdRequest(false, "Foto fora das regras")).IsValid.Should().BeTrue();
        new ReviewAdRequestValidator().Validate(new ReviewAdRequest(true, null)).IsValid.Should().BeTrue();
    }
}
