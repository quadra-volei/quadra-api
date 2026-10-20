using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quadra.Infrastructure.Storage;
using Quadra.Modules.Profile.Application;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.Modules.Profile.CardStyle;

// How the player dressed their card ("Sua carta"): the finish (Prisma, Íon, Eclipse), an identity
// title and an optional photo just for the card. Cosmetic only: no title or finish gives points,
// a level or an advantage. Anyone who can see the profile sees the card the way its owner set it.

/// <summary>The identity titles a player can put on the card, by code. The app owns the wording.</summary>
public static class CardTitles
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "MAESTRO", "LANCEIRO", "DEFENSOR", "MURALHA", "CORINGA", "SAQUE_DE_FERRO",
    };
}

/// <param name="Finish"><c>Prisma</c>, <c>Ion</c> or <c>Eclipse</c>.</param>
/// <param name="Title">A code from <see cref="CardTitles"/>, or null for the app's default.</param>
/// <param name="PhotoObjectKey">Key from the photo upload-url endpoint, or null to use the profile photo.</param>
public sealed record UpdateCardStyleRequest(string? Finish, string? Title, string? PhotoObjectKey);

/// <param name="PhotoUrl">The card's own photo; null means the card shows the profile photo.</param>
/// <param name="PhotoObjectKey">Only for the owner, to send back and keep the photo.</param>
public sealed record PlayerCardStyleResponse(string Finish, string? Title, string? PhotoUrl, string? PhotoObjectKey);

public sealed class UpdateCardStyleRequestValidator : AbstractValidator<UpdateCardStyleRequest>
{
    /// <summary>Root-context-data key under which the controller supplies the caller id.</summary>
    public const string CallerIdKey = "CallerId";

    public UpdateCardStyleRequestValidator()
    {
        RuleFor(x => x.Finish)
            .NotEmpty()
            .Must(f => Enum.TryParse<CardFinish>(f, ignoreCase: false, out _) && !int.TryParse(f, out _))
            .WithMessage("Finish must be Prisma, Ion or Eclipse.");

        RuleFor(x => x.Title)
            .Must(t => t is null || CardTitles.All.Contains(t))
            .WithMessage("Title must be one of the card titles.");

        RuleFor(x => x.PhotoObjectKey).Custom((key, context) =>
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            // The caller may only point at an object of their own (see the upload-url endpoint).
            var owned = context.RootContextData.TryGetValue(CallerIdKey, out var raw)
                && raw is Guid callerId
                && key.StartsWith($"profiles/{callerId}/", StringComparison.Ordinal);
            if (key.Length > 512 || !owned)
            {
                context.AddFailure(nameof(UpdateCardStyleRequest.PhotoObjectKey),
                    "PhotoObjectKey must reference an object owned by the caller.");
            }
        });
    }
}

public sealed class CardStyleHandler
{
    private readonly ProfileDbContext _context;
    private readonly IProfilePhotoStorage _photoStorage;
    private readonly TimeProvider _timeProvider;

    public CardStyleHandler(ProfileDbContext context, IProfilePhotoStorage photoStorage, TimeProvider timeProvider)
    {
        _context = context;
        _photoStorage = photoStorage;
        _timeProvider = timeProvider;
    }

    /// <summary>Saves the caller's card style; null when the caller has no profile.</summary>
    public async Task<PlayerCardStyleResponse?> SaveAsync(
        Guid callerId,
        UpdateCardStyleRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await _context.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == callerId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        // The validator has already checked the finish; this parse cannot fail.
        profile.SetCardStyle(
            Enum.Parse<CardFinish>(request.Finish!),
            request.Title,
            string.IsNullOrEmpty(request.PhotoObjectKey) ? null : request.PhotoObjectKey,
            _timeProvider.GetUtcNow());
        await _context.SaveChangesAsync(cancellationToken);

        return await MapAsync(profile, includePrivate: true, _photoStorage, cancellationToken);
    }

    public static async Task<PlayerCardStyleResponse> MapAsync(
        PlayerProfile profile,
        bool includePrivate,
        IProfilePhotoStorage photoStorage,
        CancellationToken cancellationToken)
    {
        var photoUrl = await GetProfileHandler.ResolvePhotoUrlAsync(profile.CardPhotoObjectKey, photoStorage, cancellationToken);
        return new PlayerCardStyleResponse(
            profile.CardFinish.ToString(),
            profile.CardTitle,
            photoUrl,
            includePrivate ? profile.CardPhotoObjectKey : null);
    }
}

[ApiController]
[Authorize]
public sealed class CardStyleController : ControllerBase
{
    private readonly CardStyleHandler _handler;
    private readonly IValidator<UpdateCardStyleRequest> _validator;

    public CardStyleController(CardStyleHandler handler, IValidator<UpdateCardStyleRequest> validator)
    {
        _handler = handler;
        _validator = validator;
    }

    /// <summary>PUT /api/v1/profiles/me/card-style — the caller's card finish, title and card photo.</summary>
    [HttpPut("api/v1/profiles/me/card-style")]
    public async Task<ActionResult<PlayerCardStyleResponse>> Save(
        [FromBody] UpdateCardStyleRequest request,
        [FromServices] IProfilePhotoStorage photoStorage,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _validator.ValidateAsync(
            new ValidationContext<UpdateCardStyleRequest>(request)
            {
                RootContextData = { [UpdateCardStyleRequestValidator.CallerIdKey] = callerId },
            },
            cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        // The key is the caller's (validator); the uploaded object must also fit the limit (SEC-13).
        if (!string.IsNullOrEmpty(request.PhotoObjectKey)
            && !await photoStorage.IsWithinLimitsAsync(request.PhotoObjectKey, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.PhotoObjectKey), "The photo is missing, larger than 10 MB or refused by the content check.");
            return ValidationProblem(ModelState);
        }

        var result = await _handler.SaveAsync(callerId, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
