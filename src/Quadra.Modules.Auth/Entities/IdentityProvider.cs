namespace Quadra.Modules.Auth.Entities;

/// <summary>
/// Identity provider used to create the local user record. Persisted as a lowercase
/// string column via an EF value converter. MVP forbids mixing providers per user.
/// </summary>
public enum IdentityProvider
{
    Phone = 0,
    Google = 1,
    Apple = 2,
}
