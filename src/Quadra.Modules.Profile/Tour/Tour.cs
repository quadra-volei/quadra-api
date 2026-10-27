using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.Modules.Profile.Tour;

// The app's first-access tour (a few highlighted steps over the tab bar). The server only keeps
// how the player left it, so a reinstall or a new phone does not show it again and the team can
// count who finished or skipped it. The steps and their wording belong to the app.

/// <param name="Version">The app's tour version, 1 or more.</param>
/// <param name="Outcome"><c>Completed</c> or <c>Skipped</c>.</param>
/// <param name="LastStep">The last step the player saw, 0-based.</param>
public sealed record RecordTourRequest(int? Version, string? Outcome, int? LastStep);

/// <summary>How the player left the tour. Only the owner gets it (<c>profiles/me</c>).</summary>
public sealed record PlayerTourResponse(int Version, string Outcome, int LastStep, DateTimeOffset EndedAt)
{
    /// <summary>The profile's tour record; null while the player never went through one.</summary>
    public static PlayerTourResponse? From(PlayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile is { TourVersion: { } version, TourOutcome: { } outcome, TourLastStep: { } step, TourEndedAt: { } at }
            ? new PlayerTourResponse(version, outcome.ToString(), step, at)
            : null;
    }
}

public sealed class RecordTourRequestValidator : AbstractValidator<RecordTourRequest>
{
    /// <summary>Upper bounds that keep a tampered request from storing nonsense.</summary>
    public const int MaxVersion = 1000;
    public const int MaxStep = 50;

    public RecordTourRequestValidator()
    {
        RuleFor(x => x.Version).NotNull().InclusiveBetween(1, MaxVersion);

        RuleFor(x => x.Outcome)
            .NotEmpty()
            .Must(o => Enum.TryParse<TourOutcome>(o, ignoreCase: false, out _) && !int.TryParse(o, out _))
            .WithMessage("Outcome must be Completed or Skipped.");

        RuleFor(x => x.LastStep).NotNull().InclusiveBetween(0, MaxStep);
    }
}

public sealed class TourHandler
{
    private readonly ProfileDbContext _context;
    private readonly TimeProvider _timeProvider;

    public TourHandler(ProfileDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    /// <summary>Records how the caller left the tour; null when the caller has no profile.</summary>
    public async Task<PlayerTourResponse?> RecordAsync(
        Guid callerId,
        RecordTourRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await _context.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == callerId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        // The validator has already checked every field; these reads cannot fail.
        profile.RecordTour(
            request.Version!.Value,
            Enum.Parse<TourOutcome>(request.Outcome!),
            request.LastStep!.Value,
            _timeProvider.GetUtcNow());
        await _context.SaveChangesAsync(cancellationToken);

        return PlayerTourResponse.From(profile);
    }
}

[ApiController]
[Authorize]
public sealed class TourController : ControllerBase
{
    private readonly TourHandler _handler;
    private readonly IValidator<RecordTourRequest> _validator;

    public TourController(TourHandler handler, IValidator<RecordTourRequest> validator)
    {
        _handler = handler;
        _validator = validator;
    }

    /// <summary>PUT /api/v1/profiles/me/tour — how the caller finished or skipped the first-access tour.</summary>
    [HttpPut("api/v1/profiles/me/tour")]
    public async Task<ActionResult<PlayerTourResponse>> Record(
        [FromBody] RecordTourRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        var result = await _handler.RecordAsync(callerId, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
