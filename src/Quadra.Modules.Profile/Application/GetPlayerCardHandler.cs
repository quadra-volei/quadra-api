using Microsoft.Extensions.Logging;
using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Loads a player's card snapshot, resolves the live free/premium flag and maps to
/// <see cref="PlayerCardResponse"/>. Serves both <c>GET /profiles/me/card</c> and
/// <c>GET /profiles/{userId}/card</c>.
/// </summary>
public sealed class GetPlayerCardHandler
{
    private readonly IPlayerCardRepository _cards;
    private readonly IPremiumStatusReader _premiumStatusReader;
    private readonly ILogger<GetPlayerCardHandler> _logger;

    public GetPlayerCardHandler(
        IPlayerCardRepository cards,
        IPremiumStatusReader premiumStatusReader,
        ILogger<GetPlayerCardHandler> logger)
    {
        _cards = cards;
        _premiumStatusReader = premiumStatusReader;
        _logger = logger;
    }

    public async Task<PlayerCardResponse> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var lookup = await _cards.FindByUserIdAsync(userId, cancellationToken);

        if (!lookup.ProfileExists)
        {
            throw new ProfileNotFoundException(userId);
        }

        if (lookup.Card is null)
        {
            throw new CardNotGeneratedException(lookup.MatchesPlayed, PlayerCard.GenerationThreshold);
        }

        var isPremium = await _premiumStatusReader.IsPremiumAsync(userId, cancellationToken);

        _logger.LogInformation(
            "Served player card for {UserId} (IsPremium={IsPremium}).",
            userId,
            isPremium);

        return MapToResponse(lookup.Card, isPremium);
    }

    /// <summary>Manual mapping from the card snapshot + live premium flag to the response DTO.</summary>
    public static PlayerCardResponse MapToResponse(PlayerCard card, bool isPremium)
    {
        return new PlayerCardResponse(
            card.UserId,
            card.DisplayName,
            card.Position?.ToString(),
            card.Level.ToString(),
            new PlayerStatsResponse(
                card.MatchesPlayed,
                card.Wins,
                card.Losses,
                card.Draws,
                card.MvpsReceived),
            isPremium,
            card.GeneratedAt,
            card.RefreshedAt);
    }
}
