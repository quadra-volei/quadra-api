using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Modules.Profile.Feedback;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.IntegrationTests.Modules.Profile;

/// <summary>
/// HTTP tests for <c>POST /api/v1/feedback</c> against a real Postgres: the message is stored
/// with who sent it, invalid bodies are refused, and one user cannot flood the table.
/// </summary>
[Collection("Profile")]
public sealed class FeedbackEndpointTests
{
    private const string Url = "/api/v1/feedback";
    private readonly ProfileWebApplicationFactory _fx;

    public FeedbackEndpointTests(ProfileWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ProfileDbContext>()
            .Database.ExecuteSqlRawAsync("TRUNCATE TABLE feedback;", Ct);
    }

    [Fact]
    public async Task POST_stores_the_message_with_its_sender()
    {
        await ResetAsync();
        var userId = Guid.NewGuid();

        var response = await _fx.CreateAuthenticatedClient(userId).PostAsJsonAsync(
            Url,
            new { type = "problem", message = "  O placar travou no set 2.  ", appVersion = "1.0.0", platform = "Android" },
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        using var scope = _fx.Factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ProfileDbContext>()
            .Feedback.AsNoTracking().SingleAsync(Ct);
        stored.Id.Should().Be(id);
        stored.UserId.Should().Be(userId);
        stored.Type.Should().Be(FeedbackType.Problem);
        stored.Message.Should().Be("O placar travou no set 2.");
        stored.AppVersion.Should().Be("1.0.0");
        stored.Platform.Should().Be("android");
        stored.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task POST_without_optional_fields_is_accepted()
    {
        await ResetAsync();

        var response = await _fx.CreateAuthenticatedClient(Guid.NewGuid())
            .PostAsJsonAsync(Url, new { type = "praise", message = "Muito bom!" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task POST_with_an_invalid_body_returns_400_and_stores_nothing()
    {
        await ResetAsync();
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());

        foreach (var body in new object[]
        {
            new { type = "complaint", message = "texto" },
            new { type = "problem", message = "   " },
            new { type = "problem", message = new string('a', 1001) },
            new { type = "problem", message = "texto", platform = "windows-phone" },
        })
        {
            (await client.PostAsJsonAsync(Url, body, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using var scope = _fx.Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ProfileDbContext>().Feedback.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task POST_without_a_token_returns_401()
    {
        var response = await _fx.CreateAnonymousClient()
            .PostAsJsonAsync(Url, new { type = "problem", message = "texto" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_over_the_daily_limit_returns_429_for_that_user_only()
    {
        await ResetAsync();
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var body = new { type = "suggestion", message = "mais um" };

        for (var i = 0; i < SendFeedbackHandler.DailyLimit; i++)
        {
            (await client.PostAsJsonAsync(Url, body, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        (await client.PostAsJsonAsync(Url, body, Ct)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await _fx.CreateAuthenticatedClient(Guid.NewGuid()).PostAsJsonAsync(Url, body, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
