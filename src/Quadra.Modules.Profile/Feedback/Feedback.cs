using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.Modules.Profile.Feedback;

// The whole "send feedback" feature (app settings → "Enviar feedback"): a user tells the team
// something and it is stored. Nothing is sent anywhere — the team reads the table.

/// <summary>What kind of message it is, as the app's form offers it.</summary>
public enum FeedbackType
{
    Suggestion,
    Problem,
    Praise,
}

/// <summary>One message a user sent to the team. Rows are only ever inserted.</summary>
public sealed class FeedbackEntry
{
    public const int MessageMaxLength = 1000;
    public const int AppVersionMaxLength = 32;
    public const int PlatformMaxLength = 16;

    // Private parameterless constructor required by EF Core.
    private FeedbackEntry() { }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public FeedbackType Type { get; private set; }

    public string Message { get; private set; } = null!;

    /// <summary>App version the message was sent from, when the app says it.</summary>
    public string? AppVersion { get; private set; }

    /// <summary>android | ios | web, when the app says it.</summary>
    public string? Platform { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static FeedbackEntry Create(
        Guid userId,
        FeedbackType type,
        string message,
        string? appVersion,
        string? platform,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Message = message.Trim(),
            AppVersion = string.IsNullOrWhiteSpace(appVersion) ? null : appVersion.Trim(),
            Platform = string.IsNullOrWhiteSpace(platform) ? null : platform.Trim().ToLowerInvariant(),
            CreatedAt = now,
        };
}

public sealed class FeedbackEntryConfiguration : IEntityTypeConfiguration<FeedbackEntry>
{
    public void Configure(EntityTypeBuilder<FeedbackEntry> builder)
    {
        builder.ToTable("feedback");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(f => f.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(f => f.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(f => f.Message).HasColumnName("message").HasMaxLength(FeedbackEntry.MessageMaxLength).IsRequired();
        builder.Property(f => f.AppVersion).HasColumnName("app_version").HasMaxLength(FeedbackEntry.AppVersionMaxLength);
        builder.Property(f => f.Platform).HasColumnName("platform").HasMaxLength(FeedbackEntry.PlatformMaxLength);
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();

        // Read newest first, and count what one user sent recently.
        builder.HasIndex(f => f.CreatedAt).HasDatabaseName("ix_feedback_created_at");
        builder.HasIndex(f => new { f.UserId, f.CreatedAt }).HasDatabaseName("ix_feedback_user_id_created_at");
    }
}

/// <summary>Body of <c>POST /api/v1/feedback</c>.</summary>
/// <param name="Type"><c>suggestion</c>, <c>problem</c> or <c>praise</c> (any casing).</param>
/// <param name="Message">1 to 1000 characters after trimming.</param>
/// <param name="AppVersion">Optional, e.g. <c>1.0.0</c>.</param>
/// <param name="Platform">Optional: <c>android</c>, <c>ios</c> or <c>web</c>.</param>
public sealed record SendFeedbackRequest(string? Type, string? Message, string? AppVersion = null, string? Platform = null);

public sealed record SendFeedbackResponse(Guid Id, DateTimeOffset CreatedAt);

public sealed class SendFeedbackRequestValidator : AbstractValidator<SendFeedbackRequest>
{
    private static readonly string[] Platforms = ["android", "ios", "web"];

    public SendFeedbackRequestValidator()
    {
        RuleFor(x => x.Type)
            .Must(type => Enum.TryParse<FeedbackType>(type, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                && !int.TryParse(type, out _))
            .WithMessage("Type must be one of: suggestion, problem, praise.");

        RuleFor(x => x.Message)
            .Must(message => !string.IsNullOrWhiteSpace(message))
            .WithMessage("Message is required.")
            .Must(message => message is null || message.Trim().Length <= FeedbackEntry.MessageMaxLength)
            .WithMessage($"Message must have at most {FeedbackEntry.MessageMaxLength} characters.");

        RuleFor(x => x.AppVersion)
            .MaximumLength(FeedbackEntry.AppVersionMaxLength);

        RuleFor(x => x.Platform)
            .Must(platform => string.IsNullOrWhiteSpace(platform)
                || Platforms.Contains(platform.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Platform must be one of: android, ios, web.");
    }
}

/// <summary>Stores a feedback message, within a daily limit per user.</summary>
public sealed class SendFeedbackHandler
{
    /// <summary>
    /// How many messages one user may send in 24 hours. The table is free to write to for any
    /// logged-in user, so this keeps one account from filling it.
    /// </summary>
    public const int DailyLimit = 20;

    private readonly ProfileDbContext _context;
    private readonly TimeProvider _timeProvider;

    public SendFeedbackHandler(ProfileDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    /// <returns>The stored entry, or null when the user is over the daily limit.</returns>
    public async Task<SendFeedbackResponse?> HandleAsync(
        Guid userId,
        SendFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var since = now.AddHours(-24);
        var recent = await _context.Feedback
            .CountAsync(f => f.UserId == userId && f.CreatedAt >= since, cancellationToken);
        if (recent >= DailyLimit)
        {
            return null;
        }

        var entry = FeedbackEntry.Create(
            userId,
            Enum.Parse<FeedbackType>(request.Type!, ignoreCase: true),
            request.Message!,
            request.AppVersion,
            request.Platform,
            now);
        _context.Feedback.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);

        return new SendFeedbackResponse(entry.Id, entry.CreatedAt);
    }
}

[ApiController]
[Route("api/v1/feedback")]
[Authorize]
public sealed class FeedbackController : ControllerBase
{
    private readonly SendFeedbackHandler _handler;
    private readonly IValidator<SendFeedbackRequest> _validator;

    public FeedbackController(SendFeedbackHandler handler, IValidator<SendFeedbackRequest> validator)
    {
        _handler = handler;
        _validator = validator;
    }

    /// <summary>POST /api/v1/feedback — store a message from the signed-in user.</summary>
    [HttpPost]
    public async Task<ActionResult<SendFeedbackResponse>> Send(
        [FromBody] SendFeedbackRequest request,
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

        var response = await _handler.HandleAsync(callerId, request, cancellationToken);
        return response is null
            ? Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                detail: "Too many feedback messages today. Try again tomorrow.")
            : StatusCode(StatusCodes.Status201Created, response);
    }
}
