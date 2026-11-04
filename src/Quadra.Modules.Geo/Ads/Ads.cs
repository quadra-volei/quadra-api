using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Quadra.Infrastructure.Storage;
using Quadra.Modules.Geo.Arenas;
using Quadra.Modules.Geo.Persistence;
using Quadra.Shared.Authorization;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Geo.Ads;

// Self-service ads (DECISIONS #102; plan in planejamento/meta-de-base-e-anuncios.md): any player
// builds an ad in the app (where it shows, around which point, for how many weeks), the price comes
// from the server, and the ad only goes on the air once it is paid and the team approved it. The
// players around the ad's point see it; each view and tap is counted once per player and day.
// Payment through the store (Google Play Billing) is not wired yet: until it is, the team marks an
// ad as paid in the admin routes. Lives in Geo because an ad is targeted by distance and its
// audience estimate reads the public games around a point, which only Geo may read.

/// <summary>Where an ad shows in the app.</summary>
public static class AdPlacements
{
    /// <summary>Banner on the home screen.</summary>
    public const string Home = "Home";

    /// <summary>Card in the corner of the map.</summary>
    public const string Map = "Map";

    /// <summary>Card shown when the app opens, at most once a week per player.</summary>
    public const string Opening = "Opening";

    public static readonly string[] All = [Home, Map, Opening];
}

/// <summary>Where a tap on the ad leads.</summary>
public static class AdLinkKinds
{
    public const string None = "None";
    public const string Arena = "Arena";
    public const string WhatsApp = "WhatsApp";
    public const string Url = "Url";

    public static readonly string[] All = [None, Arena, WhatsApp, Url];
}

/// <summary>The stored state of an ad. Scheduled, live and ended are derived from the dates.</summary>
public static class AdStatuses
{
    public const string AwaitingPayment = "AwaitingPayment";
    public const string InReview = "InReview";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
}

/// <summary>What the advertiser sees: the stored state plus where an approved ad is in its period.</summary>
public static class AdPhases
{
    public const string AwaitingPayment = AdStatuses.AwaitingPayment;
    public const string InReview = AdStatuses.InReview;
    public const string Scheduled = "Scheduled";
    public const string Live = "Live";
    public const string Ended = "Ended";
    public const string Rejected = AdStatuses.Rejected;
    public const string Cancelled = AdStatuses.Cancelled;
}

public static class AdViewKinds
{
    public const string Impression = "Impression";
    public const string Click = "Click";
}

/// <summary>One ad. Created by a player, paid, reviewed by the team, shown during its weeks.</summary>
public sealed class Ad
{
    public const int TitleMaxLength = 40;
    public const int BodyMaxLength = 120;
    public const int LinkMaxLength = 300;
    public const int ReasonMaxLength = 300;

    public Guid Id { get; init; }

    /// <summary>Who bought it; null once that account was deleted (a paid ad is kept for the books).</summary>
    public Guid? AdvertiserId { get; set; }

    public string Placement { get; init; } = null!;

    public string Title { get; init; } = null!;

    public string? Body { get; init; }

    public string? PhotoObjectKey { get; init; }

    public string LinkKind { get; init; } = AdLinkKinds.None;

    public string? LinkValue { get; init; }

    /// <summary>The centre of the area the ad shows in (geography, X = longitude).</summary>
    public Point Location { get; init; } = null!;

    public int RadiusKm { get; init; }

    /// <summary>First day on the air, Brasília date.</summary>
    public DateOnly StartsOn { get; init; }

    public int Weeks { get; init; }

    /// <summary>The price the server quoted when the ad was created, in cents of real.</summary>
    public int PriceCents { get; init; }

    public string Status { get; set; } = AdStatuses.AwaitingPayment;

    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>How it was paid: a store order id, or <c>manual:&lt;admin id&gt;</c> plus the team's note.</summary>
    public string? PaymentReference { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public Guid? ReviewedBy { get; set; }

    public string? RejectionReason { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The day after the last one on the air (<see cref="StartsOn"/> plus the weeks).</summary>
    public DateOnly EndsOn { get; init; }

    public string Phase(DateOnly today) => Status switch
    {
        AdStatuses.Approved when today < StartsOn => AdPhases.Scheduled,
        AdStatuses.Approved when today >= EndsOn => AdPhases.Ended,
        AdStatuses.Approved => AdPhases.Live,
        _ => Status,
    };
}

/// <summary>One player saw (or tapped) one ad on one day. Counted once per player, ad, day and kind.</summary>
public sealed class AdView
{
    public Guid AdId { get; init; }

    public Guid UserId { get; init; }

    public DateOnly Day { get; init; }

    public string Kind { get; init; } = null!;
}

public sealed class AdConfiguration : IEntityTypeConfiguration<Ad>
{
    public void Configure(EntityTypeBuilder<Ad> builder)
    {
        builder.ToTable("ads", t =>
        {
            t.HasCheckConstraint("ck_ads_placement", $"placement IN ('{AdPlacements.Home}', '{AdPlacements.Map}', '{AdPlacements.Opening}')");
            t.HasCheckConstraint(
                "ck_ads_status",
                $"status IN ('{AdStatuses.AwaitingPayment}', '{AdStatuses.InReview}', '{AdStatuses.Approved}', '{AdStatuses.Rejected}', '{AdStatuses.Cancelled}')");
            t.HasCheckConstraint(
                "ck_ads_link_kind",
                $"link_kind IN ('{AdLinkKinds.None}', '{AdLinkKinds.Arena}', '{AdLinkKinds.WhatsApp}', '{AdLinkKinds.Url}')");

            // Nothing goes on the air unpaid: an approved ad always has a payment.
            t.HasCheckConstraint("ck_ads_paid", $"status NOT IN ('{AdStatuses.InReview}', '{AdStatuses.Approved}') OR paid_at IS NOT NULL");
            t.HasCheckConstraint("ck_ads_weeks", "weeks BETWEEN 1 AND 12");
            t.HasCheckConstraint("ck_ads_radius", "radius_km BETWEEN 1 AND 50");
            t.HasCheckConstraint("ck_ads_price", "price_cents > 0");
            t.HasCheckConstraint("ck_ads_ends_on", "ends_on = starts_on + 7 * weeks");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.AdvertiserId).HasColumnName("advertiser_id");
        builder.Property(a => a.Placement).HasColumnName("placement").HasMaxLength(16).IsRequired();
        builder.Property(a => a.Title).HasColumnName("title").HasMaxLength(Ad.TitleMaxLength).IsRequired();
        builder.Property(a => a.Body).HasColumnName("body").HasMaxLength(Ad.BodyMaxLength);
        builder.Property(a => a.PhotoObjectKey).HasColumnName("photo_object_key").HasMaxLength(256);
        builder.Property(a => a.LinkKind).HasColumnName("link_kind").HasMaxLength(16).IsRequired();
        builder.Property(a => a.LinkValue).HasColumnName("link_value").HasMaxLength(Ad.LinkMaxLength);
        builder.Property(a => a.Location).HasColumnName("location").HasColumnType("geography (point, 4326)").IsRequired();
        builder.Property(a => a.RadiusKm).HasColumnName("radius_km");
        builder.Property(a => a.StartsOn).HasColumnName("starts_on");
        builder.Property(a => a.Weeks).HasColumnName("weeks");
        builder.Property(a => a.PriceCents).HasColumnName("price_cents");
        builder.Property(a => a.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(a => a.PaidAt).HasColumnName("paid_at");
        builder.Property(a => a.PaymentReference).HasColumnName("payment_reference").HasMaxLength(200);
        builder.Property(a => a.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(a => a.ReviewedBy).HasColumnName("reviewed_by");
        builder.Property(a => a.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(Ad.ReasonMaxLength);
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.EndsOn).HasColumnName("ends_on");

        builder.HasIndex(a => a.AdvertiserId).HasDatabaseName("ix_ads_advertiser_id");
        builder.HasIndex(a => new { a.Status, a.Placement, a.StartsOn }).HasDatabaseName("ix_ads_status_placement_starts");
        builder.HasIndex(a => a.Location).HasMethod("gist").HasDatabaseName("ix_ads_location");
    }
}

public sealed class AdViewConfiguration : IEntityTypeConfiguration<AdView>
{
    public void Configure(EntityTypeBuilder<AdView> builder)
    {
        builder.ToTable("ad_views", t =>
            t.HasCheckConstraint("ck_ad_views_kind", $"kind IN ('{AdViewKinds.Impression}', '{AdViewKinds.Click}')"));

        builder.HasKey(v => new { v.AdId, v.UserId, v.Day, v.Kind });
        builder.Property(v => v.AdId).HasColumnName("ad_id");
        builder.Property(v => v.UserId).HasColumnName("user_id");
        builder.Property(v => v.Day).HasColumnName("day");
        builder.Property(v => v.Kind).HasColumnName("kind").HasMaxLength(16);
        builder.HasIndex(v => v.UserId).HasDatabaseName("ix_ad_views_user_id");

        builder.HasOne<Ad>().WithMany().HasForeignKey(v => v.AdId).OnDelete(DeleteBehavior.Cascade);
    }
}

// ── Options ────────────────────────────────────────────────────────────────

/// <summary>Ad prices (Render: <c>Ads__PricePerWeekCents__Home=5900</c> …).</summary>
public sealed class AdsOptions
{
    public const string SectionName = "Ads";

    /// <summary>Price of one week with a 10 km radius, in cents, per placement.</summary>
    public Dictionary<string, int> PricePerWeekCents { get; set; } = new()
    {
        [AdPlacements.Home] = 5900,
        [AdPlacements.Map] = 2900,
        [AdPlacements.Opening] = 6900,
    };

    /// <summary>Ads a player may have waiting (unpaid or in review) at once.</summary>
    public int MaxPendingPerAdvertiser { get; set; } = 5;
}

/// <summary>Prices and the choices the advertiser has. Pure, so the app and the tests agree.</summary>
public static class AdPricing
{
    /// <summary>The radius choices and how each one weighs on the price (10 km is the base).</summary>
    public static readonly IReadOnlyDictionary<int, decimal> RadiusFactors = new Dictionary<int, decimal>
    {
        [5] = 0.6m,
        [10] = 1m,
        [20] = 1.8m,
    };

    public static readonly int[] WeekOptions = [1, 2, 4];

    /// <summary>How far ahead an ad may start.</summary>
    public const int MaxDaysAhead = 90;

    /// <summary>Price in cents, rounded to whole reais; null for a placement without a price.</summary>
    public static int? PriceCents(AdsOptions options, string placement, int radiusKm, int weeks)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.PricePerWeekCents.TryGetValue(placement, out var perWeek) || perWeek <= 0
            || !RadiusFactors.TryGetValue(radiusKm, out var factor))
        {
            return null;
        }

        return (int)(Math.Round(perWeek * factor * weeks / 100m, MidpointRounding.AwayFromZero) * 100);
    }
}

// ── Contracts ──────────────────────────────────────────────────────────────

public sealed record AdOfferQuery(double Lat, double Lon, int RadiusKm = 10);

public sealed record AdPlacementPriceResponse(string Placement, int PricePerWeekCents);

/// <summary>What the area really has, counted from the public games of the last 30 days.</summary>
public sealed record AdAudienceResponse(int PublicGames, int PlayerPresences, int Arenas);

/// <summary>
/// The offer for one area. <c>PaymentAvailable</c> is false while store payment is not wired: the app
/// says the ad is reserved and the team confirms the payment.
/// </summary>
public sealed record AdOfferResponse(
    IReadOnlyList<AdPlacementPriceResponse> Placements,
    IReadOnlyList<int> RadiusOptions,
    IReadOnlyList<int> WeekOptions,
    AdAudienceResponse Audience,
    bool PaymentAvailable);

public sealed record CreateAdRequest(
    string? Placement,
    double Lat,
    double Lon,
    int RadiusKm,
    DateOnly StartsOn,
    int Weeks,
    string? Title,
    string? Body,
    string? PhotoObjectKey,
    string? LinkKind,
    string? LinkValue);

public sealed record AdResponse(
    Guid Id,
    string Placement,
    string Title,
    string? Body,
    string? PhotoUrl,
    string LinkKind,
    string? LinkValue,
    double Latitude,
    double Longitude,
    int RadiusKm,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int Weeks,
    int PriceCents,
    string Phase,
    string? RejectionReason,
    int Impressions,
    int Clicks,
    DateTimeOffset CreatedAt);

/// <summary>An ad as the players around it see it.</summary>
public sealed record ServedAdResponse(
    Guid Id,
    string Placement,
    string Title,
    string? Body,
    string? PhotoUrl,
    string LinkKind,
    string? LinkValue);

public sealed record ServeAdQuery(string? Placement, double Lat, double Lon);

public sealed record AdViewRequest(string? Kind);

public sealed record AdminAdResponse(Guid? AdvertiserId, string? PaymentReference, AdResponse Ad);

public sealed record MarkAdPaidRequest(string? Note);

public sealed record ReviewAdRequest(bool Approve, string? Reason);

// ── Validation ─────────────────────────────────────────────────────────────

public sealed class AdOfferQueryValidator : AbstractValidator<AdOfferQuery>
{
    public AdOfferQueryValidator()
    {
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lon).InclusiveBetween(-180, 180);
        RuleFor(x => x.RadiusKm).Must(AdPricing.RadiusFactors.ContainsKey).WithMessage("RadiusKm must be 5, 10 or 20.");
    }
}

public sealed class ServeAdQueryValidator : AbstractValidator<ServeAdQuery>
{
    public ServeAdQueryValidator()
    {
        RuleFor(x => x.Placement).Must(p => p is not null && AdPlacements.All.Contains(p)).WithMessage("Placement must be Home, Map or Opening.");
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lon).InclusiveBetween(-180, 180);
    }
}

public sealed class CreateAdRequestValidator : AbstractValidator<CreateAdRequest>
{
    public CreateAdRequestValidator()
    {
        RuleFor(x => x.Placement).Must(p => p is not null && AdPlacements.All.Contains(p)).WithMessage("Placement must be Home, Map or Opening.");
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lon).InclusiveBetween(-180, 180);
        RuleFor(x => x.RadiusKm).Must(AdPricing.RadiusFactors.ContainsKey).WithMessage("RadiusKm must be 5, 10 or 20.");
        RuleFor(x => x.Weeks).Must(AdPricing.WeekOptions.Contains).WithMessage("Weeks must be 1, 2 or 4.");
        RuleFor(x => x.Title).NotEmpty().MinimumLength(3).MaximumLength(Ad.TitleMaxLength);
        RuleFor(x => x.Body).MaximumLength(Ad.BodyMaxLength);
        RuleFor(x => x.PhotoObjectKey).MaximumLength(256);
        RuleFor(x => x.LinkKind).Must(k => k is null || AdLinkKinds.All.Contains(k)).WithMessage("LinkKind must be None, Arena, WhatsApp or Url.");

        When(x => x.LinkKind == AdLinkKinds.Arena, () =>
            RuleFor(x => x.LinkValue).Must(v => Guid.TryParse(v, out _)).WithMessage("An Arena link needs the arena id."));
        When(x => x.LinkKind == AdLinkKinds.WhatsApp, () =>
            RuleFor(x => x.LinkValue).Matches("^[0-9]{10,13}$").WithMessage("A WhatsApp link needs 10 to 13 digits (with the country code)."));
        When(x => x.LinkKind == AdLinkKinds.Url, () =>
            RuleFor(x => x.LinkValue).NotEmpty().MaximumLength(Ad.LinkMaxLength).Must(IsHttpsUrl).WithMessage("A link must be an https:// address."));
        When(x => x.LinkKind is null or AdLinkKinds.None, () =>
            RuleFor(x => x.LinkValue).Empty().WithMessage("No link kind, no link."));
    }

    private static bool IsHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host);
}

public sealed class ReviewAdRequestValidator : AbstractValidator<ReviewAdRequest>
{
    public ReviewAdRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(Ad.ReasonMaxLength);
        When(x => !x.Approve, () => RuleFor(x => x.Reason).NotEmpty().WithMessage("Say why the ad was refused."));
    }
}

public sealed class MarkAdPaidRequestValidator : AbstractValidator<MarkAdPaidRequest>
{
    public MarkAdPaidRequestValidator()
    {
        RuleFor(x => x.Note).MaximumLength(150);
    }
}

// ── Handler ────────────────────────────────────────────────────────────────

public sealed class AdRequestException : Exception
{
    public AdRequestException(string message)
        : base(message)
    {
    }
}

public sealed class AdConflictException : Exception
{
    public AdConflictException(string message)
        : base(message)
    {
    }
}

public sealed class AdsHandler
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly TimeSpan AudienceWindow = TimeSpan.FromDays(30);
    private const int MaxAudienceGames = 2000;

    private readonly GeoDbContext _db;
    private readonly ArenaReader _arenas;
    private readonly IMatchAvailabilityReader _availability;
    private readonly IProfilePhotoStorage _photos;
    private readonly AdsOptions _options;
    private readonly TimeProvider _clock;

    public AdsHandler(
        GeoDbContext db,
        ArenaReader arenas,
        IMatchAvailabilityReader availability,
        IProfilePhotoStorage photos,
        IOptions<AdsOptions> options,
        TimeProvider clock)
    {
        _db = db;
        _arenas = arenas;
        _availability = availability;
        _photos = photos;
        _options = options.Value;
        _clock = clock;
    }

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), Brasilia).DateTime);

    private static Point At(double lat, double lon) => new(lon, lat) { SRID = 4326 };

    /// <summary>Prices for the area and what it really has: public games, presences and arenas.</summary>
    public async Task<AdOfferResponse> OfferAsync(AdOfferQuery query, CancellationToken cancellationToken)
    {
        var point = At(query.Lat, query.Lon);
        var meters = query.RadiusKm * 1000d;
        var since = _clock.GetUtcNow() - AudienceWindow;
        var now = _clock.GetUtcNow();

        var games = await _db.Matches.AsNoTracking()
            .Where(m => m.Visibility == "Open" && m.Status != "Cancelled"
                && m.DateTime >= since && m.DateTime <= now
                && m.Location.IsWithinDistance(point, meters))
            .Select(m => m.Id)
            .Take(MaxAudienceGames)
            .ToListAsync(cancellationToken);
        var presences = games.Count == 0
            ? 0
            : (await _availability.GetAvailabilityAsync(games, cancellationToken)).Values.Sum(a => a.ConfirmedCount);
        var arenas = await _db.Arenas.AsNoTracking()
            .CountAsync(a => a.Status == ArenaStatuses.Active && a.Location.IsWithinDistance(point, meters), cancellationToken);

        var placements = AdPlacements.All
            .Select(p => (p, AdPricing.PriceCents(_options, p, query.RadiusKm, 1)))
            .Where(x => x.Item2 is not null)
            .Select(x => new AdPlacementPriceResponse(x.p, x.Item2!.Value))
            .ToList();

        return new AdOfferResponse(
            placements,
            AdPricing.RadiusFactors.Keys.Order().ToList(),
            AdPricing.WeekOptions,
            new AdAudienceResponse(games.Count, presences, arenas),
            PaymentAvailable: false);
    }

    public async Task<AdResponse> CreateAsync(Guid callerId, CreateAdRequest request, CancellationToken cancellationToken)
    {
        var today = Today;
        if (request.StartsOn < today || request.StartsOn > today.AddDays(AdPricing.MaxDaysAhead))
        {
            throw new AdRequestException($"The ad must start between today and {AdPricing.MaxDaysAhead} days from now.");
        }

        var price = AdPricing.PriceCents(_options, request.Placement!, request.RadiusKm, request.Weeks)
            ?? throw new AdRequestException("This placement is not on sale.");

        var pending = await _db.Ads.CountAsync(
            a => a.AdvertiserId == callerId && (a.Status == AdStatuses.AwaitingPayment || a.Status == AdStatuses.InReview),
            cancellationToken);
        if (pending >= _options.MaxPendingPerAdvertiser)
        {
            throw new AdConflictException($"You already have {pending} ads waiting. Pay or cancel one first.");
        }

        var linkKind = request.LinkKind ?? AdLinkKinds.None;
        var linkValue = string.IsNullOrWhiteSpace(request.LinkValue) ? null : request.LinkValue.Trim();
        if (linkKind == AdLinkKinds.Arena)
        {
            var arenaId = await _arenas.FindActiveAsync(Guid.Parse(linkValue!), cancellationToken)
                ?? throw new AdRequestException("The arena does not exist or is not on the map.");
            linkValue = arenaId.ToString();
        }

        var photo = string.IsNullOrWhiteSpace(request.PhotoObjectKey) ? null : request.PhotoObjectKey;
        if (photo is not null
            && (!photo.StartsWith($"profiles/{callerId}/", StringComparison.Ordinal)
                || !await _photos.IsWithinLimitsAsync(photo, cancellationToken)))
        {
            throw new AdRequestException("The photo was not accepted.");
        }

        var ad = new Ad
        {
            Id = Guid.NewGuid(),
            AdvertiserId = callerId,
            Placement = request.Placement!,
            Title = request.Title!.Trim(),
            Body = string.IsNullOrWhiteSpace(request.Body) ? null : request.Body.Trim(),
            PhotoObjectKey = photo,
            LinkKind = linkKind,
            LinkValue = linkValue,
            Location = At(request.Lat, request.Lon),
            RadiusKm = request.RadiusKm,
            StartsOn = request.StartsOn,
            EndsOn = request.StartsOn.AddDays(7 * request.Weeks),
            Weeks = request.Weeks,
            PriceCents = price,
            Status = AdStatuses.AwaitingPayment,
            CreatedAt = _clock.GetUtcNow(),
        };
        _db.Ads.Add(ad);
        await _db.SaveChangesAsync(cancellationToken);
        return await ToResponseAsync(ad, today, new Dictionary<(Guid, string), int>(), cancellationToken);
    }

    public async Task<IReadOnlyList<AdResponse>> MineAsync(Guid callerId, CancellationToken cancellationToken)
    {
        var ads = await _db.Ads.AsNoTracking()
            .Where(a => a.AdvertiserId == callerId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return await ToResponsesAsync(ads, cancellationToken);
    }

    /// <summary>Cancels an ad not paid yet. False when it is not the caller's.</summary>
    public async Task<bool> CancelAsync(Guid callerId, Guid adId, CancellationToken cancellationToken)
    {
        var ad = await _db.Ads.SingleOrDefaultAsync(a => a.Id == adId && a.AdvertiserId == callerId, cancellationToken);
        if (ad is null)
        {
            return false;
        }

        if (ad.Status == AdStatuses.Cancelled)
        {
            return true;
        }

        if (ad.Status != AdStatuses.AwaitingPayment)
        {
            throw new AdConflictException("A paid ad cannot be cancelled in the app; talk to the team.");
        }

        ad.Status = AdStatuses.Cancelled;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>One ad on the air around the viewer for the placement, picked at random; null when none.</summary>
    public async Task<ServedAdResponse?> ServeAsync(ServeAdQuery query, CancellationToken cancellationToken)
    {
        var today = Today;
        var point = At(query.Lat, query.Lon);
        var ad = await LiveAds(today)
            .Where(a => a.Placement == query.Placement && a.Location.IsWithinDistance(point, a.RadiusKm * 1000d))
            .OrderBy(_ => EF.Functions.Random())
            .FirstOrDefaultAsync(cancellationToken);
        if (ad is null)
        {
            return null;
        }

        return new ServedAdResponse(
            ad.Id,
            ad.Placement,
            ad.Title,
            ad.Body,
            ad.PhotoObjectKey is null ? null : await _photos.GetReadUrlAsync(ad.PhotoObjectKey, cancellationToken),
            ad.LinkKind,
            ad.LinkValue);
    }

    /// <summary>Counts a view or a tap once per player, ad, day and kind. False when the ad is not on the air.</summary>
    public async Task<bool> RecordViewAsync(Guid callerId, Guid adId, string kind, CancellationToken cancellationToken)
    {
        var today = Today;
        if (!await LiveAds(today).AnyAsync(a => a.Id == adId, cancellationToken))
        {
            return false;
        }

        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO ad_views (ad_id, user_id, day, kind) VALUES ({adId}, {callerId}, {today}, {kind}) ON CONFLICT DO NOTHING",
            cancellationToken);
        return true;
    }

    // ── Team ──

    public async Task<IReadOnlyList<AdminAdResponse>> ListForTeamAsync(string? status, CancellationToken cancellationToken)
    {
        var query = _db.Ads.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(a => a.Status == status);
        }

        var ads = await query.OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync(cancellationToken);
        var responses = await ToResponsesAsync(ads, cancellationToken);
        return ads.Zip(responses, (ad, response) => new AdminAdResponse(ad.AdvertiserId, ad.PaymentReference, response)).ToList();
    }

    /// <summary>The team confirms a payment received (until store payment exists). Null when there is no such ad.</summary>
    public async Task<AdResponse?> MarkPaidAsync(Guid adminId, Guid adId, string? note, CancellationToken cancellationToken)
    {
        var ad = await _db.Ads.SingleOrDefaultAsync(a => a.Id == adId, cancellationToken);
        if (ad is null)
        {
            return null;
        }

        if (ad.Status != AdStatuses.AwaitingPayment)
        {
            throw new AdConflictException("Only an ad awaiting payment can be marked as paid.");
        }

        ad.Status = AdStatuses.InReview;
        ad.PaidAt = _clock.GetUtcNow();
        ad.PaymentReference = string.IsNullOrWhiteSpace(note) ? $"manual:{adminId}" : $"manual:{adminId}:{note.Trim()}";
        await _db.SaveChangesAsync(cancellationToken);
        return (await ToResponsesAsync([ad], cancellationToken))[0];
    }

    /// <summary>The team approves or refuses a paid ad. Null when there is no such ad.</summary>
    public async Task<AdResponse?> ReviewAsync(Guid adminId, Guid adId, ReviewAdRequest request, CancellationToken cancellationToken)
    {
        var ad = await _db.Ads.SingleOrDefaultAsync(a => a.Id == adId, cancellationToken);
        if (ad is null)
        {
            return null;
        }

        if (ad.Status != AdStatuses.InReview)
        {
            throw new AdConflictException("Only a paid ad waiting for review can be reviewed.");
        }

        ad.Status = request.Approve ? AdStatuses.Approved : AdStatuses.Rejected;
        ad.RejectionReason = request.Approve ? null : request.Reason!.Trim();
        ad.ReviewedAt = _clock.GetUtcNow();
        ad.ReviewedBy = adminId;
        await _db.SaveChangesAsync(cancellationToken);
        return (await ToResponsesAsync([ad], cancellationToken))[0];
    }

    private IQueryable<Ad> LiveAds(DateOnly today) =>
        _db.Ads.AsNoTracking()
            .Where(a => a.Status == AdStatuses.Approved && a.PaidAt != null
                && a.StartsOn <= today && a.EndsOn > today);

    private async Task<IReadOnlyList<AdResponse>> ToResponsesAsync(IReadOnlyList<Ad> ads, CancellationToken cancellationToken)
    {
        var ids = ads.Select(a => a.Id).ToList();
        var counts = ids.Count == 0
            ? new Dictionary<(Guid, string), int>()
            : (await _db.AdViews.AsNoTracking()
                .Where(v => ids.Contains(v.AdId))
                .GroupBy(v => new { v.AdId, v.Kind })
                .Select(g => new { g.Key.AdId, g.Key.Kind, Count = g.Count() })
                .ToListAsync(cancellationToken))
                .ToDictionary(x => (x.AdId, x.Kind), x => x.Count);

        var today = Today;
        var responses = new List<AdResponse>(ads.Count);
        foreach (var ad in ads)
        {
            responses.Add(await ToResponseAsync(ad, today, counts, cancellationToken));
        }

        return responses;
    }

    private async Task<AdResponse> ToResponseAsync(
        Ad ad,
        DateOnly today,
        IReadOnlyDictionary<(Guid, string), int> counts,
        CancellationToken cancellationToken) =>
        new(
            ad.Id,
            ad.Placement,
            ad.Title,
            ad.Body,
            ad.PhotoObjectKey is null ? null : await _photos.GetReadUrlAsync(ad.PhotoObjectKey, cancellationToken),
            ad.LinkKind,
            ad.LinkValue,
            ad.Location.Y,
            ad.Location.X,
            ad.RadiusKm,
            ad.StartsOn,
            ad.EndsOn,
            ad.Weeks,
            ad.PriceCents,
            ad.Phase(today),
            ad.RejectionReason,
            counts.GetValueOrDefault((ad.Id, AdViewKinds.Impression)),
            counts.GetValueOrDefault((ad.Id, AdViewKinds.Click)),
            ad.CreatedAt);
}

/// <summary>
/// Account export and deletion: the person's ads and views. A paid ad is a sale on the books, so it
/// stays with no advertiser; an unpaid one goes.
/// </summary>
public sealed class AdsPersonalData : IPersonalDataStore
{
    private readonly GeoDbContext _db;

    public AdsPersonalData(GeoDbContext db)
    {
        _db = db;
    }

    public string Section => "ads";

    public async Task<object> ExportAsync(Guid userId, CancellationToken cancellationToken) => new
    {
        Ads = (await _db.Ads.AsNoTracking()
            .Where(a => a.AdvertiserId == userId)
            .ToListAsync(cancellationToken))
            .Select(a => new
            {
                a.Id,
                a.Placement,
                a.Title,
                a.Body,
                a.LinkKind,
                a.LinkValue,
                Latitude = a.Location.Y,
                Longitude = a.Location.X,
                a.RadiusKm,
                a.StartsOn,
                a.Weeks,
                a.PriceCents,
                a.Status,
                a.PaidAt,
                a.CreatedAt,
            })
            .ToList(),
        AdViews = await _db.AdViews.AsNoTracking().CountAsync(v => v.UserId == userId, cancellationToken),
    };

    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _db.AdViews.Where(v => v.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.Ads.Where(a => a.AdvertiserId == userId && a.PaidAt == null).ExecuteDeleteAsync(cancellationToken);
        await _db.Ads.Where(a => a.AdvertiserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AdvertiserId, (Guid?)null), cancellationToken);
    }
}

// ── HTTP ───────────────────────────────────────────────────────────────────

[ApiController]
[Authorize]
public sealed class AdsController : ControllerBase
{
    private readonly AdsHandler _handler;

    public AdsController(AdsHandler handler)
    {
        _handler = handler;
    }

    /// <summary>GET /api/v1/ads/offer?lat=&amp;lon=&amp;radiusKm= — prices and the area's real numbers.</summary>
    [HttpGet("api/v1/ads/offer")]
    public async Task<ActionResult<AdOfferResponse>> Offer(
        [FromQuery] AdOfferQuery query,
        [FromServices] IValidator<AdOfferQuery> validator,
        CancellationToken cancellationToken)
    {
        if (await InvalidAsync(validator, query, cancellationToken) is { } problem)
        {
            return problem;
        }

        return Ok(await _handler.OfferAsync(query, cancellationToken));
    }

    /// <summary>POST /api/v1/ads — creates an ad awaiting payment, at the server's price.</summary>
    [HttpPost("api/v1/ads")]
    public async Task<ActionResult<AdResponse>> Create(
        [FromBody] CreateAdRequest request,
        [FromServices] IValidator<CreateAdRequest> validator,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        if (await InvalidAsync(validator, request, cancellationToken) is { } problem)
        {
            return problem;
        }

        try
        {
            var ad = await _handler.CreateAsync(callerId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, ad);
        }
        catch (AdRequestException ex)
        {
            return BadRequest(new ProblemDetails { Detail = ex.Message });
        }
        catch (AdConflictException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>GET /api/v1/ads/mine — the caller's ads with their views and taps.</summary>
    [HttpGet("api/v1/ads/mine")]
    public async Task<ActionResult<IReadOnlyList<AdResponse>>> Mine(CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        return Ok(await _handler.MineAsync(callerId, cancellationToken));
    }

    /// <summary>DELETE /api/v1/ads/{id} — cancels the caller's unpaid ad (404 for anyone else's).</summary>
    [HttpDelete("api/v1/ads/{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        try
        {
            return await _handler.CancelAsync(callerId, id, cancellationToken) ? NoContent() : NotFound();
        }
        catch (AdConflictException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>GET /api/v1/ads/serve?placement=&amp;lat=&amp;lon= — an ad on the air here, or 204.</summary>
    [HttpGet("api/v1/ads/serve")]
    public async Task<ActionResult<ServedAdResponse>> Serve(
        [FromQuery] ServeAdQuery query,
        [FromServices] IValidator<ServeAdQuery> validator,
        CancellationToken cancellationToken)
    {
        if (await InvalidAsync(validator, query, cancellationToken) is { } problem)
        {
            return problem;
        }

        var ad = await _handler.ServeAsync(query, cancellationToken);
        return ad is null ? NoContent() : Ok(ad);
    }

    /// <summary>POST /api/v1/ads/{id}/views — counts a view or a tap (once per player and day).</summary>
    [HttpPost("api/v1/ads/{id:guid}/views")]
    public async Task<IActionResult> View(Guid id, [FromBody] AdViewRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        if (request.Kind is not (AdViewKinds.Impression or AdViewKinds.Click))
        {
            ModelState.AddModelError(nameof(request.Kind), "Kind must be Impression or Click.");
            return ValidationProblem(ModelState);
        }

        return await _handler.RecordViewAsync(callerId, id, request.Kind, cancellationToken) ? NoContent() : NotFound();
    }

    /// <summary>GET /api/v1/admin/ads?status= — the team's list.</summary>
    [HttpGet("api/v1/admin/ads")]
    [Authorize(Policy = AuthPolicies.Admin)]
    public async Task<ActionResult<IReadOnlyList<AdminAdResponse>>> ListForTeam([FromQuery] string? status, CancellationToken cancellationToken) =>
        Ok(await _handler.ListForTeamAsync(status, cancellationToken));

    /// <summary>POST /api/v1/admin/ads/{id}/paid — the team confirms the payment.</summary>
    [HttpPost("api/v1/admin/ads/{id:guid}/paid")]
    [Authorize(Policy = AuthPolicies.Admin)]
    public async Task<ActionResult<AdResponse>> MarkPaid(
        Guid id,
        [FromBody] MarkAdPaidRequest request,
        [FromServices] IValidator<MarkAdPaidRequest> validator,
        CancellationToken cancellationToken)
    {
        if (await InvalidAsync(validator, request, cancellationToken) is { } problem)
        {
            return problem;
        }

        try
        {
            var ad = await _handler.MarkPaidAsync(CallerId(), id, request.Note, cancellationToken);
            return ad is null ? NotFound() : Ok(ad);
        }
        catch (AdConflictException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>POST /api/v1/admin/ads/{id}/review — the team approves or refuses a paid ad.</summary>
    [HttpPost("api/v1/admin/ads/{id:guid}/review")]
    [Authorize(Policy = AuthPolicies.Admin)]
    public async Task<ActionResult<AdResponse>> Review(
        Guid id,
        [FromBody] ReviewAdRequest request,
        [FromServices] IValidator<ReviewAdRequest> validator,
        CancellationToken cancellationToken)
    {
        if (await InvalidAsync(validator, request, cancellationToken) is { } problem)
        {
            return problem;
        }

        try
        {
            var ad = await _handler.ReviewAsync(CallerId(), id, request, cancellationToken);
            return ad is null ? NotFound() : Ok(ad);
        }
        catch (AdConflictException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    // The admin policy already required a valid account id in the token.
    private Guid CallerId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool TryGetCallerId(out Guid callerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out callerId);

    private async Task<ActionResult?> InvalidAsync<T>(IValidator<T> validator, T value, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(value, cancellationToken);
        if (validation.IsValid)
        {
            return null;
        }

        foreach (var failure in validation.Errors)
        {
            ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
        }

        return ValidationProblem(ModelState);
    }
}
