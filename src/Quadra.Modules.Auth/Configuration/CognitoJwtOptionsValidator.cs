using FluentValidation;

namespace Quadra.Modules.Auth.Configuration;

/// <summary>
/// FluentValidation validator that enforces the presence of every required Cognito setting
/// at application startup. Failing fast here avoids accidentally booting an API with
/// silently disabled authentication.
/// </summary>
public sealed class CognitoJwtOptionsValidator : AbstractValidator<CognitoJwtOptions>
{
    public CognitoJwtOptionsValidator()
    {
        RuleFor(x => x.UserPoolId)
            .NotEmpty()
            .WithMessage("Auth:Cognito:UserPoolId is required.");

        RuleFor(x => x.Region)
            .NotEmpty()
            .WithMessage("Auth:Cognito:Region is required.");

        RuleFor(x => x.Audience)
            .NotEmpty()
            .WithMessage("Auth:Cognito:Audience is required.");

        RuleFor(x => x.ClockSkewSeconds)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Auth:Cognito:ClockSkewSeconds must be zero or positive.");
    }
}
