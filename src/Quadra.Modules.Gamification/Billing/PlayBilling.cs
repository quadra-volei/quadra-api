using System.Net;
using FluentValidation;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Quadra.Modules.Gamification.Billing;

// Season pass sold through Google Play Billing (DECISIONS #109). The app buys the season's one-time
// product with the Play Billing library and sends the purchase token here; the API asks Google
// whether the purchase is real, paid and made for this account, acknowledges it (Play refunds an
// unacknowledged purchase after 3 days) and records it. A purchase refunded or charged back is
// found by a sweep of Google's voided purchases and stops giving the pass. The price lives only in
// the Play Console product, so it changes there with no deploy.

// ── Options ────────────────────────────────────────────────────────────────

/// <summary><c>SeasonPass:Billing</c>: the Play app, the service account that reads its purchases and each season's product.</summary>
public sealed class PlayBillingOptions
{
    public const string SectionName = "SeasonPass:Billing";

    /// <summary>The Android application id the products belong to.</summary>
    public string PackageName { get; set; } = "app.quadravolei";

    /// <summary>
    /// The JSON key of a Google Cloud service account invited to the Play Console with "View financial
    /// data" and "Manage orders" (Render secret, never in a file). Empty = billing off: no product is
    /// offered and a purchase is refused with 503.
    /// </summary>
    public string? ServiceAccountJson { get; set; }

    /// <summary>The Play Console in-app product (one-time, non-consumable) of each season, by season code: <c>ProductIds__T1=passe_t1</c>.</summary>
    public Dictionary<string, string> ProductIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Minutes between sweeps of refunded and charged-back purchases. 0 = off.</summary>
    public int VoidedSweepMinutes { get; set; } = 60;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServiceAccountJson);

    /// <summary>The product of <paramref name="seasonCode"/>, or null when billing is off or the season has none.</summary>
    public string? ProductFor(string seasonCode) =>
        IsConfigured && ProductIds.TryGetValue(seasonCode, out var id) && !string.IsNullOrWhiteSpace(id) ? id : null;

    public bool IsValid() =>
        VoidedSweepMinutes >= 0
        && ProductIds.Values.All(id => System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9][a-z0-9._]{0,138}$"))
        && ProductIds.Values.Distinct(StringComparer.Ordinal).Count() == ProductIds.Count;
}

// ── Entity ─────────────────────────────────────────────────────────────────

public enum SeasonPassPurchaseState
{
    /// <summary>Paid and acknowledged: gives the pass.</summary>
    Active,

    /// <summary>Refunded, cancelled or charged back on Google's side: no longer gives the pass.</summary>
    Voided,
}

/// <summary>One season pass bought on Google Play (<c>season_pass_purchases</c>).</summary>
public sealed class SeasonPassPurchase
{
    public Guid Id { get; init; }

    /// <summary>The buyer; null once the account is deleted (the sale stays on the books).</summary>
    public Guid? UserId { get; set; }

    public string SeasonCode { get; init; } = null!;

    public string ProductId { get; init; } = null!;

    /// <summary>SHA-256 of the purchase token, hex: one purchase, one account. The token itself is never stored.</summary>
    public string PurchaseTokenHash { get; init; } = null!;

    /// <summary>Google's order id (GPA.…), what the Play Console and a refund request show.</summary>
    public string? OrderId { get; init; }

    public SeasonPassPurchaseState State { get; set; }

    public DateTimeOffset PurchasedAt { get; init; }

    public DateTimeOffset VerifiedAt { get; init; }

    public DateTimeOffset? VoidedAt { get; set; }
}

internal sealed class SeasonPassPurchaseConfiguration : IEntityTypeConfiguration<SeasonPassPurchase>
{
    public void Configure(EntityTypeBuilder<SeasonPassPurchase> builder)
    {
        builder.ToTable("season_pass_purchases");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.UserId).HasColumnName("user_id");
        builder.Property(p => p.SeasonCode).HasColumnName("season_code").HasMaxLength(16).IsRequired();
        builder.Property(p => p.ProductId).HasColumnName("product_id").HasMaxLength(140).IsRequired();
        builder.Property(p => p.PurchaseTokenHash).HasColumnName("purchase_token_hash").HasMaxLength(64).IsRequired();
        builder.Property(p => p.OrderId).HasColumnName("order_id").HasMaxLength(64);
        builder.Property(p => p.State).HasColumnName("state").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(p => p.PurchasedAt).HasColumnName("purchased_at");
        builder.Property(p => p.VerifiedAt).HasColumnName("verified_at");
        builder.Property(p => p.VoidedAt).HasColumnName("voided_at");
        builder.HasIndex(p => p.PurchaseTokenHash).IsUnique();
        builder.HasIndex(p => new { p.UserId, p.SeasonCode });
    }
}

// ── Google Play Developer API ──────────────────────────────────────────────

/// <summary>A product purchase as Google reports it (purchases.products.get).</summary>
public sealed record PlayProductPurchase(
    [property: JsonPropertyName("purchaseState")] int PurchaseState,                  // 0 purchased, 1 cancelled, 2 pending
    [property: JsonPropertyName("acknowledgementState")] int AcknowledgementState,    // 0 not yet, 1 acknowledged
    [property: JsonPropertyName("orderId")] string? OrderId,
    [property: JsonPropertyName("purchaseTimeMillis")] string? PurchaseTimeMillis,
    [property: JsonPropertyName("obfuscatedExternalAccountId")] string? ObfuscatedExternalAccountId,
    [property: JsonPropertyName("purchaseType")] int? PurchaseType);                  // 0 test (license tester), 1 promo, 2 rewarded; absent = real

/// <summary>A purchase Google voided (refund, cancellation or chargeback).</summary>
public sealed record PlayVoidedPurchase(
    [property: JsonPropertyName("purchaseToken")] string PurchaseToken,
    [property: JsonPropertyName("orderId")] string? OrderId,
    [property: JsonPropertyName("voidedTimeMillis")] string? VoidedTimeMillis);

/// <summary>The three calls the pass needs from the Google Play Developer API.</summary>
public interface IGooglePlayPurchases
{
    /// <summary>The purchase, or null when Google does not know this token for this product.</summary>
    Task<PlayProductPurchase?> GetAsync(string productId, string purchaseToken, CancellationToken cancellationToken);

    Task AcknowledgeAsync(string productId, string purchaseToken, CancellationToken cancellationToken);

    /// <summary>One-time products voided since <paramref name="since"/> (Google keeps 30 days).</summary>
    Task<IReadOnlyList<PlayVoidedPurchase>> ListVoidedAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>Google refused or failed a call: the purchase is not recorded and the app may retry.</summary>
public sealed class GooglePlayUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// <see cref="IGooglePlayPurchases"/> over HTTPS, signed in as the service account of
/// <see cref="PlayBillingOptions.ServiceAccountJson"/> (OAuth 2.0 JWT bearer grant, RS256, scope
/// androidpublisher). The access token is reused until a minute before it expires.
/// </summary>
public sealed class GooglePlayPurchasesClient : IGooglePlayPurchases
{
    public const string HttpClientName = "google-play";

    private const string Scope = "https://www.googleapis.com/auth/androidpublisher";
    private const string Api = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/";

    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static (string Token, DateTimeOffset ExpiresAt)? _cached;

    private readonly IHttpClientFactory _httpFactory;
    private readonly PlayBillingOptions _options;
    private readonly TimeProvider _clock;

    public GooglePlayPurchasesClient(IHttpClientFactory httpFactory, IOptions<PlayBillingOptions> options, TimeProvider clock)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _clock = clock;
    }

    public async Task<PlayProductPurchase?> GetAsync(string productId, string purchaseToken, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, ProductUrl(productId, purchaseToken), cancellationToken);

        // Google answers 400 (invalid value) or 404 for a token it never issued for this product.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.Gone)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "purchases.products.get", cancellationToken);
        return await response.Content.ReadFromJsonAsync<PlayProductPurchase>(cancellationToken);
    }

    public async Task AcknowledgeAsync(string productId, string purchaseToken, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, ProductUrl(productId, purchaseToken) + ":acknowledge", cancellationToken);
        await EnsureSuccessAsync(response, "purchases.products.acknowledge", cancellationToken);
    }

    public async Task<IReadOnlyList<PlayVoidedPurchase>> ListVoidedAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var voided = new List<PlayVoidedPurchase>();
        string? page = null;
        do
        {
            var url = $"{Api}{Uri.EscapeDataString(_options.PackageName)}/purchases/voidedpurchases?type=0&maxResults=1000"
                + $"&startTime={since.ToUnixTimeMilliseconds()}"
                + (page is null ? string.Empty : $"&token={Uri.EscapeDataString(page)}");
            using var response = await SendAsync(HttpMethod.Get, url, cancellationToken);
            await EnsureSuccessAsync(response, "purchases.voidedpurchases.list", cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<VoidedPage>(cancellationToken);
            voided.AddRange(body?.VoidedPurchases ?? []);
            page = body?.TokenPagination?.NextPageToken;
        }
        while (!string.IsNullOrEmpty(page));

        return voided;
    }

    private string ProductUrl(string productId, string purchaseToken) =>
        $"{Api}{Uri.EscapeDataString(_options.PackageName)}/purchases/products/{Uri.EscapeDataString(productId)}/tokens/{Uri.EscapeDataString(purchaseToken)}";

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(cancellationToken));
        if (method == HttpMethod.Post)
        {
            request.Content = JsonContent.Create(new { });
        }

        try
        {
            return await _httpFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new GooglePlayUnavailableException("Google Play could not be reached.", ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string call, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Google's error body names the problem (permission, product, package); it has no personal data.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new GooglePlayUnavailableException($"{call} answered {(int)response.StatusCode}: {body[..Math.Min(body.Length, 300)]}");
        }
    }

    private async Task<string> AccessTokenAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        if (_cached is { } fresh && fresh.ExpiresAt > now.AddMinutes(1))
        {
            return fresh.Token;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is { } again && again.ExpiresAt > now.AddMinutes(1))
            {
                return again.Token;
            }

            var key = JsonSerializer.Deserialize<ServiceAccountKey>(_options.ServiceAccountJson ?? string.Empty)
                ?? throw new GooglePlayUnavailableException("SeasonPass:Billing:ServiceAccountJson is not a service account key.");
            var assertion = SignedAssertion(key, now);
            using var response = await _httpFactory.CreateClient(HttpClientName).PostAsync(
                key.TokenUri ?? "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertion,
                }),
                cancellationToken);
            await EnsureSuccessAsync(response, "oauth2 token", cancellationToken);
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new GooglePlayUnavailableException("oauth2 token answered no body.");
            _cached = (token.AccessToken, now.AddSeconds(token.ExpiresIn));
            return token.AccessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    /// <summary>The RS256 JWT a service account presents for an access token.</summary>
    public static string SignedAssertion(ServiceAccountKey key, DateTimeOffset now)
    {
        static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = B64(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = key.ClientEmail,
            scope = Scope,
            aud = key.TokenUri ?? "https://oauth2.googleapis.com/token",
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddMinutes(55).ToUnixTimeSeconds(),
        }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(key.PrivateKey);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{claims}.{B64(signature)}";
    }

    public sealed record ServiceAccountKey(
        [property: JsonPropertyName("client_email")] string ClientEmail,
        [property: JsonPropertyName("private_key")] string PrivateKey,
        [property: JsonPropertyName("token_uri")] string? TokenUri);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record VoidedPage(
        [property: JsonPropertyName("voidedPurchases")] List<PlayVoidedPurchase>? VoidedPurchases,
        [property: JsonPropertyName("tokenPagination")] Pagination? TokenPagination);

    private sealed record Pagination([property: JsonPropertyName("nextPageToken")] string? NextPageToken);
}

// ── Use cases ──────────────────────────────────────────────────────────────

/// <summary><c>POST /api/v1/season-pass/purchases</c> body: what the Play Billing library handed the app.</summary>
public sealed record SeasonPassPurchaseRequest(string ProductId, string PurchaseToken);

/// <summary>Pending = paid with a slow method (boleto, cash): the pass opens when Google confirms and the app sends it again.</summary>
public sealed record SeasonPassPurchaseResponse(string Status, string Season);

public sealed class BillingNotConfiguredException() : Exception("Season pass billing is not configured.");

public sealed class UnknownSeasonPassProductException() : Exception("This product is not the pass of a season on sale.");

public sealed class InvalidSeasonPassPurchaseException(string reason) : Exception(reason);

public sealed class SeasonPassPurchaseTakenException() : Exception("This purchase belongs to another account.");

/// <summary>Records and checks the season passes bought on Google Play.</summary>
public sealed class SeasonPassPurchases
{
    private readonly Persistence.GamificationDbContext _db;
    private readonly IGooglePlayPurchases _play;
    private readonly PlayBillingOptions _billing;
    private readonly Application.SeasonPassOptions _seasons;
    private readonly TimeProvider _clock;
    private readonly ILogger<SeasonPassPurchases> _logger;

    public SeasonPassPurchases(
        Persistence.GamificationDbContext db,
        IGooglePlayPurchases play,
        IOptions<PlayBillingOptions> billing,
        IOptions<Application.SeasonPassOptions> seasons,
        TimeProvider clock,
        ILogger<SeasonPassPurchases> logger)
    {
        _db = db;
        _play = play;
        _billing = billing.Value;
        _seasons = seasons.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// The value the app passes to Play Billing as <c>obfuscatedAccountId</c>: Google returns it with the
    /// purchase, so a token bought on one account cannot be claimed by another. Not reversible to the id.
    /// </summary>
    public static string AccountTag(Guid userId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"quadra-pass:{userId:D}")));

    public static string HashToken(string purchaseToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(purchaseToken)));

    /// <summary>Whether <paramref name="userId"/> bought the pass of <paramref name="seasonCode"/> and it still stands.</summary>
    public Task<bool> HasPassAsync(Guid userId, string seasonCode, CancellationToken cancellationToken) =>
        _db.SeasonPassPurchases.AsNoTracking().AnyAsync(
            p => p.UserId == userId && p.SeasonCode == seasonCode && p.State == SeasonPassPurchaseState.Active,
            cancellationToken);

    public async Task<SeasonPassPurchaseResponse> VerifyAsync(Guid userId, SeasonPassPurchaseRequest request, CancellationToken cancellationToken)
    {
        if (!_billing.IsConfigured)
        {
            throw new BillingNotConfiguredException();
        }

        // Only the pass of a season still on sale (being played or about to start).
        var now = _clock.GetUtcNow();
        var season = Application.SeasonPassRules.Seasons(_seasons)
            .FirstOrDefault(s => _billing.ProductFor(s.Definition.Code) == request.ProductId && now < s.EndsAt)
            ?? throw new UnknownSeasonPassProductException();
        var code = season.Definition.Code;
        var tokenHash = HashToken(request.PurchaseToken);

        var known = await _db.SeasonPassPurchases.AsNoTracking().FirstOrDefaultAsync(p => p.PurchaseTokenHash == tokenHash, cancellationToken);
        if (known is not null)
        {
            // Sending the same purchase again (app restarted, retry) is fine; claiming another's is not.
            return known.UserId == userId && known.State == SeasonPassPurchaseState.Active
                ? new SeasonPassPurchaseResponse("Active", code)
                : throw new SeasonPassPurchaseTakenException();
        }

        var purchase = await _play.GetAsync(request.ProductId, request.PurchaseToken, cancellationToken)
            ?? throw new InvalidSeasonPassPurchaseException("Google does not know this purchase.");
        if (!string.Equals(purchase.ObfuscatedExternalAccountId, AccountTag(userId), StringComparison.Ordinal))
        {
            throw new InvalidSeasonPassPurchaseException("The purchase was made for another account.");
        }

        switch (purchase.PurchaseState)
        {
            case 2:
                return new SeasonPassPurchaseResponse("Pending", code);
            case not 0:
                throw new InvalidSeasonPassPurchaseException("The purchase was cancelled.");
        }

        if (purchase.AcknowledgementState == 0)
        {
            await _play.AcknowledgeAsync(request.ProductId, request.PurchaseToken, cancellationToken);
        }

        _db.SeasonPassPurchases.Add(new SeasonPassPurchase
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SeasonCode = code,
            ProductId = request.ProductId,
            PurchaseTokenHash = tokenHash,
            OrderId = purchase.OrderId,
            State = SeasonPassPurchaseState.Active,
            PurchasedAt = long.TryParse(purchase.PurchaseTimeMillis, out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : now,
            VerifiedAt = now,
        });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two requests with the same token at once: the unique index let one through.
            throw new SeasonPassPurchaseTakenException();
        }

        _logger.LogInformation(
            "Season pass bought. Season={Season} Product={Product} Order={Order} Test={Test}",
            code, request.ProductId, purchase.OrderId, purchase.PurchaseType == 0);
        return new SeasonPassPurchaseResponse("Active", code);
    }

    /// <summary>Marks as voided every recorded purchase Google refunded or charged back in its 30-day window.</summary>
    /// <returns>How many purchases stopped giving the pass.</returns>
    public async Task<int> SweepVoidedAsync(CancellationToken cancellationToken)
    {
        if (!_billing.IsConfigured)
        {
            return 0;
        }

        var now = _clock.GetUtcNow();
        var voided = await _play.ListVoidedAsync(now.AddDays(-30), cancellationToken);
        var hashes = voided.Select(v => HashToken(v.PurchaseToken)).ToList();
        if (hashes.Count == 0)
        {
            return 0;
        }

        var count = await _db.SeasonPassPurchases
            .Where(p => hashes.Contains(p.PurchaseTokenHash) && p.State == SeasonPassPurchaseState.Active)
            .ExecuteUpdateAsync(
                s => s.SetProperty(p => p.State, SeasonPassPurchaseState.Voided).SetProperty(p => p.VoidedAt, now),
                cancellationToken);
        if (count > 0)
        {
            _logger.LogWarning("Season pass purchases voided by Google (refund or chargeback): {Count}.", count);
        }

        return count;
    }
}

public sealed class SeasonPassPurchaseRequestValidator : AbstractValidator<SeasonPassPurchaseRequest>
{
    public SeasonPassPurchaseRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().MaximumLength(140);

        // Play tokens are a few hundred characters of letters, digits, dots, dashes and underscores.
        RuleFor(x => x.PurchaseToken).NotEmpty().MaximumLength(4096).Matches("^[A-Za-z0-9._\\-]+$");
    }
}
