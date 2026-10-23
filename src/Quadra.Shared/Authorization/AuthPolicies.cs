namespace Quadra.Shared.Authorization;

/// <summary>Authorization policies any module can require.</summary>
public static class AuthPolicies
{
    /// <summary>
    /// The team's accounts (<c>Moderation:AdminUserIds</c>, on Render <c>Moderation__AdminUserIds__0</c> …):
    /// moderation and arena curation. Registered by the Profile module.
    /// </summary>
    public const string Admin = "Admin";
}
