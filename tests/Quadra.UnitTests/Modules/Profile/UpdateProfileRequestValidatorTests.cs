using FluentAssertions;
using FluentValidation;
using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Validation;

namespace Quadra.UnitTests.Modules.Profile;

/// <summary>
/// Unit tests for <see cref="UpdateProfileRequestValidator"/> and <see cref="ProfileHandle"/>.
///
/// Covers: F2.1 field validation in the mobile shape:
///   - FirstName / LastName 1..40 (trimmed);
///   - Handle 3..20 of a-z, 0-9, underscore (case-insensitive, optional leading @);
///   - BirthDate a past date;
///   - Position restricted to LEV|PON|OPO|CEN|LIB|COR;
///   - Modality Indoor|Beach and Level Beginner|Intermediate|Advanced when present;
///   - PhotoObjectKey must match the caller-owned prefix profiles/{callerId}/ and be &lt;= 512 chars.
/// </summary>
public sealed class UpdateProfileRequestValidatorTests
{
    private static readonly Guid CallerId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly UpdateProfileRequestValidator _sut = new(new FixedClock(Now));

    private static UpdateProfileRequest Valid() => new(
        FirstName: "Ana",
        LastName: "Souza",
        Handle: "ana_souza",
        BirthDate: new DateOnly(1998, 3, 14),
        Position: "PON",
        Modality: "Indoor",
        Level: "Beginner",
        PhotoObjectKey: null);

    private FluentValidation.Results.ValidationResult Validate(UpdateProfileRequest request)
    {
        var context = new ValidationContext<UpdateProfileRequest>(request)
        {
            RootContextData = { [UpdateProfileRequestValidator.CallerIdKey] = CallerId },
        };
        return _sut.Validate(context);
    }

    private void ShouldFailOn(UpdateProfileRequest request, string property)
    {
        var result = Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == property);
    }

    /// <summary>Covers: a complete onboarding request is valid.</summary>
    [Fact]
    public void Complete_request_passes()
    {
        Validate(Valid()).IsValid.Should().BeTrue();
    }

    /// <summary>Covers: an edit request (no modality, no level) is valid.</summary>
    [Fact]
    public void Request_without_modality_and_level_passes()
    {
        Validate(Valid() with { Modality = null, Level = null }).IsValid.Should().BeTrue();
    }

    // ─── names ───────────────────────────────────────────────────────────────

    /// <summary>Covers: FirstName / LastName must have 1..40 characters after trimming.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_names_are_invalid(string? name)
    {
        ShouldFailOn(Valid() with { FirstName = name! }, nameof(UpdateProfileRequest.FirstName));
        ShouldFailOn(Valid() with { LastName = name! }, nameof(UpdateProfileRequest.LastName));
    }

    /// <summary>Covers: the 40-character bound (inclusive).</summary>
    [Fact]
    public void Names_are_bounded_at_40_characters()
    {
        Validate(Valid() with { FirstName = new string('a', 40) }).IsValid.Should().BeTrue();
        ShouldFailOn(Valid() with { FirstName = new string('a', 41) }, nameof(UpdateProfileRequest.FirstName));
    }

    // ─── handle ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Covers: valid handles — lowercase, digits, underscore, 3..20; mixed case and a leading @
    /// are accepted because the handle is normalized before use.
    /// </summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("ana_souza")]
    [InlineData("jogador_10")]
    [InlineData("Ana_Souza")]
    [InlineData("@ana")]
    [InlineData("  ana  ")]
    [InlineData("a2345678901234567890")] // 20 chars
    public void Valid_handles_pass(string handle)
    {
        Validate(Valid() with { Handle = handle }).IsValid.Should().BeTrue();
    }

    /// <summary>Covers: handles that are too short/long or contain other characters are rejected.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("a23456789012345678901")] // 21 chars
    [InlineData("ana souza")]
    [InlineData("ana-souza")]
    [InlineData("ana.souza")]
    [InlineData("anã")]
    [InlineData("@@ana")]
    [InlineData(null)]
    public void Invalid_handles_fail(string? handle)
    {
        ShouldFailOn(Valid() with { Handle = handle! }, nameof(UpdateProfileRequest.Handle));
    }

    /// <summary>Covers: normalization — trimmed, one leading @ dropped, lowercased.</summary>
    [Theory]
    [InlineData("  @Ana_Souza ", "ana_souza")]
    [InlineData("CRAQUE", "craque")]
    [InlineData(null, "")]
    public void Handle_is_normalized(string? input, string expected)
    {
        ProfileHandle.Normalize(input).Should().Be(expected);
    }

    // ─── birth date ──────────────────────────────────────────────────────────

    /// <summary>Covers: BirthDate must be strictly in the past and not absurdly old.</summary>
    [Theory]
    [InlineData(2026, 10, 6)] // today
    [InlineData(2030, 1, 1)]
    [InlineData(1899, 12, 31)]
    public void Invalid_birth_dates_fail(int year, int month, int day)
    {
        ShouldFailOn(
            Valid() with { BirthDate = new DateOnly(year, month, day) },
            nameof(UpdateProfileRequest.BirthDate));
    }

    /// <summary>Covers: yesterday is a valid birth date.</summary>
    [Fact]
    public void Yesterday_is_a_valid_birth_date()
    {
        Validate(Valid() with { BirthDate = new DateOnly(2026, 10, 5) }).IsValid.Should().BeTrue();
    }

    // ─── position / modality / level ─────────────────────────────────────────

    /// <summary>Covers: every position code in the catalog is accepted.</summary>
    [Theory]
    [InlineData("LEV")]
    [InlineData("PON")]
    [InlineData("OPO")]
    [InlineData("CEN")]
    [InlineData("LIB")]
    [InlineData("COR")]
    public void Valid_positions_pass(string position)
    {
        Validate(Valid() with { Position = position }).IsValid.Should().BeTrue();
    }

    /// <summary>Covers: the old position names, wrong casing and missing values are rejected.</summary>
    [Theory]
    [InlineData("Setter")]
    [InlineData("lev")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_positions_fail(string? position)
    {
        ShouldFailOn(Valid() with { Position = position! }, nameof(UpdateProfileRequest.Position));
    }

    /// <summary>Covers: Modality is Indoor or Beach when present.</summary>
    [Theory]
    [InlineData("Indoor", true)]
    [InlineData("Beach", true)]
    [InlineData("INDOOR", false)]
    [InlineData("Grass", false)]
    [InlineData("", false)]
    public void Modality_must_be_in_the_catalog(string modality, bool valid)
    {
        Validate(Valid() with { Modality = modality }).IsValid.Should().Be(valid);
    }

    /// <summary>Covers: only Beginner, Intermediate and Advanced can be declared — never Elite.</summary>
    [Theory]
    [InlineData("Beginner", true)]
    [InlineData("Intermediate", true)]
    [InlineData("Advanced", true)]
    [InlineData("Elite", false)]
    [InlineData("beginner", false)]
    public void Level_must_be_declarable(string level, bool valid)
    {
        Validate(Valid() with { Level = level }).IsValid.Should().Be(valid);
    }

    // ─── PhotoObjectKey ──────────────────────────────────────────────────────

    /// <summary>Covers: a key under the caller-owned prefix is accepted.</summary>
    [Fact]
    public void PhotoObjectKey_under_caller_prefix_passes()
    {
        var key = $"profiles/{CallerId}/photo/{Guid.NewGuid()}.jpg";
        Validate(Valid() with { PhotoObjectKey = key }).IsValid.Should().BeTrue();
    }

    /// <summary>Covers: a key under another user's prefix, or outside profiles/, is rejected.</summary>
    [Theory]
    [InlineData("profiles/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/photo/x.jpg")]
    [InlineData("other/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/photo.jpg")]
    public void PhotoObjectKey_outside_the_caller_prefix_is_invalid(string key)
    {
        ShouldFailOn(Valid() with { PhotoObjectKey = key }, nameof(UpdateProfileRequest.PhotoObjectKey));
    }

    /// <summary>Covers: a key longer than 512 characters is rejected.</summary>
    [Fact]
    public void PhotoObjectKey_over_max_length_is_invalid()
    {
        var key = $"profiles/{CallerId}/" + new string('x', 512);
        ShouldFailOn(Valid() with { PhotoObjectKey = key }, nameof(UpdateProfileRequest.PhotoObjectKey));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
