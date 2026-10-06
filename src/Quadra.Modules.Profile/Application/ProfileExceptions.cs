namespace Quadra.Modules.Profile.Application;

/// <summary>Thrown when no profile exists for the requested user.</summary>
public sealed class ProfileNotFoundException : Exception
{
    public ProfileNotFoundException(Guid userId)
        : base($"No profile exists for user '{userId}'.") { }
}

/// <summary>Thrown when a supplied position string is not a valid <c>PlayerPosition</c>.</summary>
public sealed class InvalidPositionException : Exception
{
    public InvalidPositionException(string value)
        : base($"'{value}' is not a valid player position.") { }
}
