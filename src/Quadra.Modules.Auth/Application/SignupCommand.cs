using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Application;

/// <summary>
/// Internal, validated representation of a signup request. The controller is responsible
/// for translating <see cref="Contracts.SignupRequest"/> into one of the concrete subtypes.
/// </summary>
public abstract record SignupCommand
{
    private SignupCommand()
    {
    }

    public abstract IdentityProvider Provider { get; }

    public sealed record Phone(string PhoneNumber) : SignupCommand
    {
        public override IdentityProvider Provider => IdentityProvider.Phone;
    }

    public sealed record External(IdentityProvider ExternalProvider, string IdToken) : SignupCommand
    {
        public override IdentityProvider Provider => ExternalProvider;
    }
}
