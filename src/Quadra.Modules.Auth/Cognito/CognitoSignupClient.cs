using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Microsoft.Extensions.Options;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Configuration;
using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Cognito;

/// <summary>
/// <see cref="ICognitoSignupClient"/> implementation that wraps <see cref="IAmazonCognitoIdentityProvider"/>.
/// Translates AWS SDK exceptions into the application-layer signup exceptions so the handler
/// and controller never have to reference the SDK directly.
/// </summary>
public sealed class CognitoSignupClient : ICognitoSignupClient
{
    private const string GoogleProviderName = "Google";
    private const string AppleProviderName = "SignInWithApple";
    private const string CognitoSubjectAttributeName = "Cognito_Subject";

    private readonly IAmazonCognitoIdentityProvider _cognito;
    private readonly CognitoSignupOptions _options;

    public CognitoSignupClient(
        IAmazonCognitoIdentityProvider cognito,
        IOptions<CognitoSignupOptions> options)
    {
        _cognito = cognito;
        _options = options.Value;
    }

    public async Task<PhoneSignupResult> SignUpPhoneAsync(
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        var password = ThrowawayPasswordGenerator.Generate();

        var request = new SignUpRequest
        {
            ClientId = _options.AppClientId,
            Username = phoneNumber,
            Password = password,
            UserAttributes = new List<AttributeType>
            {
                new() { Name = "phone_number", Value = phoneNumber },
            },
        };

        if (!string.IsNullOrEmpty(_options.AppClientSecret))
        {
            request.SecretHash = SecretHashCalculator.Compute(
                phoneNumber,
                _options.AppClientId,
                _options.AppClientSecret);
        }

        try
        {
            var response = await _cognito.SignUpAsync(request, cancellationToken)
                .ConfigureAwait(false);

            var delivery = response.CodeDeliveryDetails;
            return new PhoneSignupResult(
                response.UserSub,
                delivery?.DeliveryMedium?.Value ?? "SMS",
                delivery?.Destination ?? MaskPhoneNumber(phoneNumber));
        }
        catch (UsernameExistsException ex)
        {
            throw new PhoneAlreadyRegisteredException(phoneNumber) { Source = ex.Source };
        }
        catch (AliasExistsException ex)
        {
            throw new PhoneAlreadyRegisteredException(phoneNumber) { Source = ex.Source };
        }
        catch (InvalidParameterException ex)
        {
            throw new CognitoPolicyViolationException(ex.Message, ex);
        }
        catch (InvalidPasswordException ex)
        {
            throw new CognitoPolicyViolationException(ex.Message, ex);
        }
        catch (CodeDeliveryFailureException ex)
        {
            throw new CognitoPolicyViolationException(ex.Message, ex);
        }
        catch (AmazonCognitoIdentityProviderException ex)
        {
            throw new CognitoUnavailableException("Cognito SignUp call failed.", ex);
        }
    }

    public async Task<ExternalSignupResult> ProvisionExternalUserAsync(
        IdentityProvider provider,
        string externalSub,
        string? email,
        bool emailVerified,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalSub);
        EnsureExternalProvider(provider);

        var username = Guid.NewGuid().ToString();
        var attributes = new List<AttributeType>();
        if (!string.IsNullOrEmpty(email))
        {
            attributes.Add(new AttributeType { Name = "email", Value = email });
            attributes.Add(new AttributeType
            {
                Name = "email_verified",
                Value = emailVerified ? "true" : "false",
            });
        }

        var request = new AdminCreateUserRequest
        {
            UserPoolId = _options.UserPoolId,
            Username = username,
            MessageAction = MessageActionType.SUPPRESS,
            UserAttributes = attributes,
        };

        try
        {
            var response = await _cognito.AdminCreateUserAsync(request, cancellationToken)
                .ConfigureAwait(false);

            var subAttribute = response.User?.Attributes?
                .FirstOrDefault(a => string.Equals(a.Name, "sub", StringComparison.Ordinal));
            var userSub = subAttribute?.Value ?? response.User?.Username ?? username;

            return new ExternalSignupResult(userSub);
        }
        catch (UsernameExistsException ex)
        {
            throw new ExternalIdentityAlreadyLinkedException(provider.ToString(), externalSub) { Source = ex.Source };
        }
        catch (InvalidParameterException ex)
        {
            throw new CognitoPolicyViolationException(ex.Message, ex);
        }
        catch (AmazonCognitoIdentityProviderException ex)
        {
            throw new CognitoUnavailableException("Cognito AdminCreateUser call failed.", ex);
        }
    }

    public async Task LinkExternalIdentityAsync(
        IdentityProvider provider,
        string destinationUsername,
        string externalSub,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalSub);
        EnsureExternalProvider(provider);

        var providerName = MapProviderName(provider);

        var request = new AdminLinkProviderForUserRequest
        {
            UserPoolId = _options.UserPoolId,
            DestinationUser = new ProviderUserIdentifierType
            {
                ProviderName = "Cognito",
                ProviderAttributeValue = destinationUsername,
            },
            SourceUser = new ProviderUserIdentifierType
            {
                ProviderName = providerName,
                ProviderAttributeName = CognitoSubjectAttributeName,
                ProviderAttributeValue = externalSub,
            },
        };

        try
        {
            await _cognito.AdminLinkProviderForUserAsync(request, cancellationToken)
                .ConfigureAwait(false);

            // Ensure status is CONFIRMED for the federated user (AdminCreateUser leaves it
            // in FORCE_CHANGE_PASSWORD by default; SUPPRESS keeps it there). For federated
            // accounts we want CONFIRMED so the JWT flow in FA.3 can issue tokens directly.
            await _cognito.AdminConfirmSignUpAsync(
                new AdminConfirmSignUpRequest
                {
                    UserPoolId = _options.UserPoolId,
                    Username = destinationUsername,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (NotAuthorizedException)
        {
            // AdminConfirmSignUp is rejected if the user is already CONFIRMED. That's fine.
        }
        catch (AliasExistsException ex)
        {
            throw new ExternalIdentityAlreadyLinkedException(provider.ToString(), externalSub) { Source = ex.Source };
        }
        catch (InvalidParameterException ex)
        {
            throw new CognitoPolicyViolationException(ex.Message, ex);
        }
        catch (AmazonCognitoIdentityProviderException ex)
        {
            throw new CognitoUnavailableException("Cognito AdminLinkProviderForUser call failed.", ex);
        }
    }

    public async Task<string?> FindExternalUserSubAsync(
        IdentityProvider provider,
        string externalSub,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalSub);
        EnsureExternalProvider(provider);

        var providerName = MapProviderName(provider);
        var filter = $"identities.providerName = \"{providerName}\" and identities.userId = \"{externalSub}\"";

        var request = new ListUsersRequest
        {
            UserPoolId = _options.UserPoolId,
            Filter = filter,
            Limit = 1,
        };

        try
        {
            var response = await _cognito.ListUsersAsync(request, cancellationToken)
                .ConfigureAwait(false);

            var existing = response.Users?.FirstOrDefault();
            if (existing is null)
            {
                return null;
            }

            var subAttribute = existing.Attributes?
                .FirstOrDefault(a => string.Equals(a.Name, "sub", StringComparison.Ordinal));
            return subAttribute?.Value ?? existing.Username;
        }
        catch (AmazonCognitoIdentityProviderException ex)
        {
            throw new CognitoUnavailableException("Cognito ListUsers call failed.", ex);
        }
    }

    private static void EnsureExternalProvider(IdentityProvider provider)
    {
        if (provider is not (IdentityProvider.Google or IdentityProvider.Apple))
        {
            throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "Only Google or Apple are valid external providers.");
        }
    }

    private static string MapProviderName(IdentityProvider provider) => provider switch
    {
        IdentityProvider.Google => GoogleProviderName,
        IdentityProvider.Apple => AppleProviderName,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    private static class ThrowawayPasswordGenerator
    {
        private const string Lower = "abcdefghijkmnpqrstuvwxyz";
        private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string Digit = "23456789";
        private const string Symbol = "!@#$%^&*()-_=+";

        public static string Generate()
        {
            // 20 chars total; guaranteed to satisfy reasonable Cognito password policies.
            var buffer = new char[20];
            buffer[0] = Pick(Lower);
            buffer[1] = Pick(Upper);
            buffer[2] = Pick(Digit);
            buffer[3] = Pick(Symbol);

            const string All = Lower + Upper + Digit + Symbol;
            for (var i = 4; i < buffer.Length; i++)
            {
                buffer[i] = Pick(All);
            }

            // Shuffle so the mandatory positions are not predictable.
            for (var i = buffer.Length - 1; i > 0; i--)
            {
                var j = System.Security.Cryptography.RandomNumberGenerator.GetInt32(i + 1);
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
            }

            return new string(buffer);
        }

        private static char Pick(string alphabet)
        {
            var index = System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length);
            return alphabet[index];
        }
    }

    private static string MaskPhoneNumber(string phoneNumber)
    {
        if (phoneNumber.Length <= 5)
        {
            return new string('*', phoneNumber.Length);
        }

        var prefix = phoneNumber[..3];
        var suffix = phoneNumber[^2..];
        var masked = new string('*', phoneNumber.Length - prefix.Length - suffix.Length);
        return $"{prefix}{masked}{suffix}";
    }
}
