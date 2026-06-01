using System.Text.RegularExpressions;
using FluentValidation;
using Quadra.Modules.Auth.Contracts;

namespace Quadra.Modules.Auth.Validation;

/// <summary>
/// FluentValidation rules for <see cref="SignupRequest"/>. Enforces the polymorphic
/// payload-by-provider contract described in the FA.2 spec.
/// </summary>
public sealed class SignupRequestValidator : AbstractValidator<SignupRequest>
{
    public const string ProviderPhone = "phone";
    public const string ProviderGoogle = "google";
    public const string ProviderApple = "apple";

    private const int MaxIdTokenLength = 8192;

    private static readonly Regex E164Regex = new(
        @"^\+[1-9]\d{1,14}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly string[] AllowedProviders =
    {
        ProviderPhone,
        ProviderGoogle,
        ProviderApple,
    };

    public SignupRequestValidator()
    {
        RuleFor(x => x.Provider)
            .NotEmpty()
            .Must(BeKnownProvider)
            .WithMessage($"Provider must be one of: {string.Join(", ", AllowedProviders)}.");

        When(IsPhoneProvider, () =>
        {
            RuleFor(x => x.Phone)
                .NotNull()
                .WithMessage("Phone payload is required when provider = 'phone'.");

            RuleFor(x => x.Google)
                .Null()
                .WithMessage("Google payload must be null when provider = 'phone'.");

            RuleFor(x => x.Apple)
                .Null()
                .WithMessage("Apple payload must be null when provider = 'phone'.");

            When(x => x.Phone is not null, () =>
            {
                RuleFor(x => x.Phone!.PhoneNumber)
                    .NotEmpty()
                    .Must(BeValidE164)
                    .WithMessage("PhoneNumber must be in E.164 format (e.g. +5511999999999).");
            });
        });

        When(IsGoogleProvider, () =>
        {
            RuleFor(x => x.Google)
                .NotNull()
                .WithMessage("Google payload is required when provider = 'google'.");

            RuleFor(x => x.Phone)
                .Null()
                .WithMessage("Phone payload must be null when provider = 'google'.");

            RuleFor(x => x.Apple)
                .Null()
                .WithMessage("Apple payload must be null when provider = 'google'.");

            When(x => x.Google is not null, () =>
            {
                RuleFor(x => x.Google!.IdToken)
                    .NotEmpty()
                    .MaximumLength(MaxIdTokenLength);
            });
        });

        When(IsAppleProvider, () =>
        {
            RuleFor(x => x.Apple)
                .NotNull()
                .WithMessage("Apple payload is required when provider = 'apple'.");

            RuleFor(x => x.Phone)
                .Null()
                .WithMessage("Phone payload must be null when provider = 'apple'.");

            RuleFor(x => x.Google)
                .Null()
                .WithMessage("Google payload must be null when provider = 'apple'.");

            When(x => x.Apple is not null, () =>
            {
                RuleFor(x => x.Apple!.IdToken)
                    .NotEmpty()
                    .MaximumLength(MaxIdTokenLength);
            });
        });
    }

    private static bool BeKnownProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return false;
        }

        var normalized = provider.Trim().ToLowerInvariant();
        return Array.IndexOf(AllowedProviders, normalized) >= 0;
    }

    private static bool BeValidE164(string? phoneNumber)
    {
        return !string.IsNullOrWhiteSpace(phoneNumber) && E164Regex.IsMatch(phoneNumber);
    }

    private static bool IsPhoneProvider(SignupRequest request) =>
        IsProvider(request, ProviderPhone);

    private static bool IsGoogleProvider(SignupRequest request) =>
        IsProvider(request, ProviderGoogle);

    private static bool IsAppleProvider(SignupRequest request) =>
        IsProvider(request, ProviderApple);

    private static bool IsProvider(SignupRequest request, string expected)
    {
        return !string.IsNullOrWhiteSpace(request.Provider)
            && string.Equals(request.Provider.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
