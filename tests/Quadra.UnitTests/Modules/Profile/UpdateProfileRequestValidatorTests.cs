using FluentAssertions;
using FluentValidation;
using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Validation;

namespace Quadra.UnitTests.Modules.Profile;

/// <summary>
/// Unit tests for <see cref="UpdateProfileRequestValidator"/>.
///
/// Covers: F2.1 AC "photo, name, primary/secondary position" validation:
///   - DisplayName length 2..80 (trimmed);
///   - position values restricted to the catalog Setter|OutsideHitter|Opposite|MiddleBlocker|Libero;
///   - secondary must differ from primary and requires a primary;
///   - PhotoObjectKey must match the caller-owned prefix profiles/{callerId}/ and be &lt;= 512 chars.
/// </summary>
public sealed class UpdateProfileRequestValidatorTests
{
    private static readonly Guid CallerId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly UpdateProfileRequestValidator _sut = new();

    private static UpdateProfileRequest Request(
        string displayName = "Valid Name",
        string? primary = null,
        string? secondary = null,
        string? photoKey = null) =>
        new(displayName, primary, secondary, photoKey);

    private FluentValidation.Results.ValidationResult Validate(UpdateProfileRequest request)
    {
        var context = new ValidationContext<UpdateProfileRequest>(request)
        {
            RootContextData = { [UpdateProfileRequestValidator.CallerIdKey] = CallerId },
        };
        return _sut.Validate(context);
    }

    // ─── DisplayName ─────────────────────────────────────────────────────────

    /// <summary>Covers: a well-formed request with only a display name is valid.</summary>
    [Fact]
    public void Valid_minimal_request_passes()
    {
        Validate(Request()).IsValid.Should().BeTrue();
    }

    /// <summary>Covers: DisplayName below the 2-char minimum is rejected.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData("  x  ")] // trims to 1 char
    public void DisplayName_too_short_is_invalid(string name)
    {
        Validate(Request(displayName: name)).IsValid.Should().BeFalse();
    }

    /// <summary>Covers: DisplayName null is rejected.</summary>
    [Fact]
    public void DisplayName_null_is_invalid()
    {
        Validate(Request(displayName: null!)).IsValid.Should().BeFalse();
    }

    /// <summary>Covers: DisplayName boundary values 2 and 80 chars are accepted.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(80)]
    public void DisplayName_at_length_bounds_is_valid(int length)
    {
        Validate(Request(displayName: new string('a', length))).IsValid.Should().BeTrue();
    }

    /// <summary>Covers: DisplayName above the 80-char maximum is rejected.</summary>
    [Fact]
    public void DisplayName_over_max_is_invalid()
    {
        Validate(Request(displayName: new string('a', 81))).IsValid.Should().BeFalse();
    }

    // ─── Positions ───────────────────────────────────────────────────────────

    /// <summary>Covers: every catalog position value is accepted as a primary position.</summary>
    [Theory]
    [InlineData("Setter")]
    [InlineData("OutsideHitter")]
    [InlineData("Opposite")]
    [InlineData("MiddleBlocker")]
    [InlineData("Libero")]
    public void Valid_primary_position_passes(string position)
    {
        Validate(Request(primary: position)).IsValid.Should().BeTrue();
    }

    /// <summary>Covers: a value outside the catalog is rejected for the primary position.</summary>
    [Theory]
    [InlineData("Goalkeeper")]
    [InlineData("setter")] // case-sensitive catalog
    [InlineData("Spiker")]
    public void Invalid_primary_position_is_invalid(string position)
    {
        Validate(Request(primary: position)).IsValid.Should().BeFalse();
    }

    /// <summary>Covers: a value outside the catalog is rejected for the secondary position.</summary>
    [Fact]
    public void Invalid_secondary_position_is_invalid()
    {
        Validate(Request(primary: "Setter", secondary: "NotAPosition")).IsValid.Should().BeFalse();
    }

    /// <summary>Covers: a valid distinct secondary paired with a primary is accepted.</summary>
    [Fact]
    public void Distinct_secondary_with_primary_passes()
    {
        Validate(Request(primary: "Setter", secondary: "Libero")).IsValid.Should().BeTrue();
    }

    /// <summary>Covers: secondary must differ from primary.</summary>
    [Fact]
    public void Secondary_equal_to_primary_is_invalid()
    {
        Validate(Request(primary: "Setter", secondary: "Setter")).IsValid.Should().BeFalse();
    }

    /// <summary>Covers: secondary may not be set without a primary.</summary>
    [Fact]
    public void Secondary_without_primary_is_invalid()
    {
        Validate(Request(primary: null, secondary: "Libero")).IsValid.Should().BeFalse();
    }

    // ─── PhotoObjectKey ownership ────────────────────────────────────────────

    /// <summary>Covers: a photo key under the caller-owned prefix is accepted.</summary>
    [Fact]
    public void PhotoObjectKey_under_caller_prefix_passes()
    {
        var key = $"profiles/{CallerId}/photo/{Guid.NewGuid()}.jpg";
        Validate(Request(photoKey: key)).IsValid.Should().BeTrue();
    }

    /// <summary>Covers: a photo key pointing at another user's prefix is rejected (ownership guard).</summary>
    [Fact]
    public void PhotoObjectKey_under_other_user_prefix_is_invalid()
    {
        var otherUser = Guid.NewGuid();
        var key = $"profiles/{otherUser}/photo/{Guid.NewGuid()}.jpg";
        Validate(Request(photoKey: key)).IsValid.Should().BeFalse();
    }

    /// <summary>Covers: a photo key with an arbitrary prefix is rejected.</summary>
    [Fact]
    public void PhotoObjectKey_with_foreign_prefix_is_invalid()
    {
        Validate(Request(photoKey: "uploads/evil.jpg")).IsValid.Should().BeFalse();
    }

    /// <summary>Covers: a photo key over 512 chars is rejected.</summary>
    [Fact]
    public void PhotoObjectKey_over_max_length_is_invalid()
    {
        var key = $"profiles/{CallerId}/photo/" + new string('a', 512) + ".jpg";
        Validate(Request(photoKey: key)).IsValid.Should().BeFalse();
    }

    /// <summary>Covers: a null photo key clears the photo and is valid.</summary>
    [Fact]
    public void Null_photo_key_passes()
    {
        Validate(Request(photoKey: null)).IsValid.Should().BeTrue();
    }
}
