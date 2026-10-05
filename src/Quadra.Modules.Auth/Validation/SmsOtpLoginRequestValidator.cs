using System.Text.RegularExpressions;
using FluentValidation;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Contracts;

namespace Quadra.Modules.Auth.Validation;

/// <summary>
/// FluentValidation rules for <see cref="SmsOtpLoginRequest"/>. A single endpoint discriminated by
/// <see cref="SmsOtpLoginRequest.Step"/>; the <c>verify</c> step adds the Code requirement.
/// </summary>
public sealed class SmsOtpLoginRequestValidator : AbstractValidator<SmsOtpLoginRequest>
{
    private const int MaxDeviceIdLength = 128;

    private static readonly string[] AllowedSteps =
    {
        SmsOtpLoginHandler.StepInitiate,
        SmsOtpLoginHandler.StepVerify,
    };

    private static readonly Regex E164Regex = new(
        @"^\+[1-9]\d{1,14}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex OtpCodeRegex = new(
        @"^\d{6}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public SmsOtpLoginRequestValidator()
    {
        RuleFor(x => x.Step)
            .NotEmpty()
            .Must(BeKnownStep)
            .WithMessage($"Step must be one of: {string.Join(", ", AllowedSteps)}.");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Must(BeValidE164)
            .WithMessage("PhoneNumber must be in E.164 format (e.g. +5511999999999).");

        When(IsVerifyStep, () =>
        {
            RuleFor(x => x.Code)
                .NotEmpty()
                .Must(BeValidOtpCode)
                .WithMessage("Code must be exactly 6 digits.");
        });

        RuleFor(x => x.DeviceId)
            .MaximumLength(MaxDeviceIdLength)
            .When(x => x.DeviceId is not null);
    }

    private static bool BeKnownStep(string? step)
    {
        if (string.IsNullOrWhiteSpace(step))
        {
            return false;
        }

        var normalized = step.Trim().ToLowerInvariant();
        return Array.IndexOf(AllowedSteps, normalized) >= 0;
    }

    private static bool IsVerifyStep(SmsOtpLoginRequest request) =>
        !string.IsNullOrWhiteSpace(request.Step)
        && string.Equals(request.Step.Trim(), SmsOtpLoginHandler.StepVerify, StringComparison.OrdinalIgnoreCase);

    private static bool BeValidE164(string? phoneNumber) =>
        !string.IsNullOrWhiteSpace(phoneNumber) && E164Regex.IsMatch(phoneNumber);

    private static bool BeValidOtpCode(string? code) =>
        !string.IsNullOrEmpty(code) && OtpCodeRegex.IsMatch(code);
}
