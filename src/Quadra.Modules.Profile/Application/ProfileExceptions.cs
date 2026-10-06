namespace Quadra.Modules.Profile.Application;

public sealed class ProfileNotFoundException : Exception
{
    public ProfileNotFoundException(Guid userId)
        : base($"No profile exists for user '{userId}'.") { }
}

/// <summary>The handle belongs to another player. Maps to HTTP 409.</summary>
public sealed class HandleAlreadyTakenException : Exception
{
    public HandleAlreadyTakenException(string handle)
        : base($"The handle '@{handle}' is already taken.") { }
}

/// <summary>
/// The request is well-formed but not acceptable for the profile's current state (e.g. missing
/// the fields that complete onboarding, or re-declaring the level). Maps to HTTP 400 on
/// <see cref="Field"/>.
/// </summary>
public sealed class ProfileUpdateRejectedException : Exception
{
    public ProfileUpdateRejectedException(string field, string message)
        : base(message)
    {
        Field = field;
    }

    public string Field { get; }
}
