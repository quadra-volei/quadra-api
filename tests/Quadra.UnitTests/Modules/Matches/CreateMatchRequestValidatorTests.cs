using FluentAssertions;
using FluentValidation;
using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Validation;

namespace Quadra.UnitTests.Modules.Matches;

/// <summary>
/// Unit tests for <see cref="CreateMatchRequestValidator"/>.
/// Each test targets a specific validation rule from the F1.1 spec.
/// </summary>
public sealed class CreateMatchRequestValidatorTests
{
    // Fixed "now" used by the validator — all "future" dates must be after this.
    private static readonly DateTimeOffset ValidatorNow = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FutureDateTime = ValidatorNow.AddDays(30);
    private static readonly DateTimeOffset FutureWindowOpens = ValidatorNow.AddDays(5);
    private static readonly DateTimeOffset FutureWindowCloses = ValidatorNow.AddDays(15);

    private readonly IValidator<CreateMatchRequest> _sut;

    public CreateMatchRequestValidatorTests()
    {
        // Use a fixed TimeProvider so all future-date checks are deterministic.
        _sut = new CreateMatchRequestValidator(new FixedTimeProvider(ValidatorNow));
    }

    // ─── Valid cases ─────────────────────────────────────────────────────────

    /// <summary>
    /// Covers: AC-1 — valid OneOff payload passes validation.
    /// </summary>
    [Fact]
    public async Task ValidOneOff_request_passes_validation()
    {
        var request = BuildValidOneOff();
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Covers: AC-2 — valid Recurring payload (with Frequency + DayOfWeek) passes validation.
    /// </summary>
    [Fact]
    public async Task ValidRecurring_request_passes_validation()
    {
        var request = BuildValidRecurring();
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeTrue();
    }

    // ─── Name ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Empty_Name_fails_validation()
    {
        var request = BuildValidOneOff() with { Name = "" };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.Name));
    }

    [Fact]
    public async Task Name_exceeding_120_chars_fails_validation()
    {
        var request = BuildValidOneOff() with { Name = new string('A', 121) };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.Name));
    }

    [Fact]
    public async Task Name_of_exactly_120_chars_passes_validation()
    {
        var request = BuildValidOneOff() with { Name = new string('A', 120) };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeTrue();
    }

    // ─── Address ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Empty_Address_fails_validation()
    {
        var request = BuildValidOneOff() with { Address = "" };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.Address));
    }

    [Fact]
    public async Task Address_exceeding_300_chars_fails_validation()
    {
        var request = BuildValidOneOff() with { Address = new string('B', 301) };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
    }

    // ─── Coordinates ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(-91.0)]
    [InlineData(91.0)]
    public async Task Latitude_out_of_range_fails_validation(double lat)
    {
        var request = BuildValidOneOff() with { Latitude = lat };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.Latitude));
    }

    [Theory]
    [InlineData(-181.0)]
    [InlineData(181.0)]
    public async Task Longitude_out_of_range_fails_validation(double lon)
    {
        var request = BuildValidOneOff() with { Longitude = lon };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.Longitude));
    }

    // ─── DateTime ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DateTime_in_the_past_fails_validation()
    {
        // All dates relative to the fixed ValidatorNow so the test is deterministic.
        var request = BuildValidOneOff() with
        {
            DateTime = ValidatorNow.AddDays(-1),
            WindowOpensAt = ValidatorNow.AddHours(1),
            WindowClosesAt = ValidatorNow.AddHours(2),
        };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.DateTime));
    }

    // ─── MaxPlayers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Covers: AC-4 — MaxPlayers = 1 (below minimum of 2) returns 400.
    /// </summary>
    [Fact]
    public async Task MaxPlayers_equal_to_1_fails_validation()
    {
        var request = BuildValidOneOff() with { MaxPlayers = 1, RegularSlots = 0 };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.MaxPlayers));
    }

    [Fact]
    public async Task MaxPlayers_equal_to_101_fails_validation()
    {
        var request = BuildValidOneOff() with { MaxPlayers = 101, RegularSlots = 10 };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.MaxPlayers));
    }

    [Fact]
    public async Task MaxPlayers_equal_to_2_passes_validation()
    {
        var request = BuildValidOneOff() with { MaxPlayers = 2, RegularSlots = 1 };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeTrue();
    }

    // ─── RegularSlots ────────────────────────────────────────────────────────

    /// <summary>
    /// Covers: AC-5 — RegularSlots > MaxPlayers returns 400.
    /// </summary>
    [Fact]
    public async Task RegularSlots_exceeding_MaxPlayers_fails_validation()
    {
        var request = BuildValidOneOff() with { MaxPlayers = 10, RegularSlots = 11 };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.RegularSlots));
    }

    [Fact]
    public async Task RegularSlots_negative_fails_validation()
    {
        var request = BuildValidOneOff() with { RegularSlots = -1 };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
    }

    // ─── Type ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invalid_Type_fails_validation()
    {
        var request = BuildValidOneOff() with { Type = "InvalidType" };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.Type));
    }

    // ─── Recurring rules ─────────────────────────────────────────────────────

    /// <summary>
    /// Covers: AC-6 — Type=Recurring but missing Frequency returns 400.
    /// </summary>
    [Fact]
    public async Task Recurring_without_Frequency_fails_validation()
    {
        var request = BuildValidRecurring() with { Frequency = null };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.Frequency));
    }

    [Fact]
    public async Task Recurring_without_DayOfWeek_fails_validation()
    {
        var request = BuildValidRecurring() with { DayOfWeek = null };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.DayOfWeek));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public async Task Recurring_with_DayOfWeek_out_of_range_fails_validation(int dayOfWeek)
    {
        var request = BuildValidRecurring() with { DayOfWeek = dayOfWeek };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.DayOfWeek));
    }

    [Fact]
    public async Task Recurring_with_invalid_Frequency_fails_validation()
    {
        var request = BuildValidRecurring() with { Frequency = "Daily" };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.Frequency));
    }

    // ─── OneOff rules ────────────────────────────────────────────────────────

    [Fact]
    public async Task OneOff_with_Frequency_set_fails_validation()
    {
        var request = BuildValidOneOff() with { Frequency = "Weekly" };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.Frequency));
    }

    [Fact]
    public async Task OneOff_with_DayOfWeek_set_fails_validation()
    {
        var request = BuildValidOneOff() with { DayOfWeek = 3 };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.DayOfWeek));
    }

    // ─── Window rules ────────────────────────────────────────────────────────

    /// <summary>
    /// Covers: AC-7 — WindowClosesAt after DateTime returns 400.
    /// </summary>
    [Fact]
    public async Task WindowClosesAt_after_DateTime_fails_validation()
    {
        var matchDate = DateTimeOffset.UtcNow.AddDays(5);
        var request = BuildValidOneOff() with
        {
            DateTime = matchDate,
            WindowOpensAt = DateTimeOffset.UtcNow.AddDays(1),
            // WindowClosesAt is after DateTime — violates the rule
            WindowClosesAt = matchDate.AddHours(1),
        };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.WindowClosesAt));
    }

    [Fact]
    public async Task WindowOpensAt_in_the_past_fails_validation()
    {
        // WindowOpensAt must be after ValidatorNow; use a value before the fixed "now".
        var request = BuildValidOneOff() with { WindowOpensAt = ValidatorNow.AddSeconds(-1) };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.WindowOpensAt));
    }

    [Fact]
    public async Task WindowOpensAt_after_WindowClosesAt_fails_validation()
    {
        var request = BuildValidOneOff() with
        {
            WindowOpensAt = FutureWindowCloses.AddHours(1),
            WindowClosesAt = FutureWindowCloses,
        };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.WindowOpensAt));
    }

    // ─── Price ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Price_negative_fails_validation()
    {
        var request = BuildValidOneOff() with { Price = -0.01m };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.Price));
    }

    [Fact]
    public async Task Price_exceeding_9999_99_fails_validation()
    {
        var request = BuildValidOneOff() with { Price = 10000m };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.Price));
    }

    [Fact]
    public async Task Null_Price_passes_validation()
    {
        var request = BuildValidOneOff() with { Price = null };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.IsValid.Should().BeTrue();
    }

    // ─── Mobile create form (format, level, privacy, recurrence, window) ─────

    private static CreateMatchRequest MobileOneOff() => BuildValidOneOff() with
    {
        WindowOpensAt = null,
        WindowClosesAt = null,
        ConfirmationOpensHoursBefore = 24,
        Format = "4X4",
        Level = "Intermediate",
        DurationMinutes = 90,
        Visibility = "Open",
    };

    private static CreateMatchRequest MobileRecurring() => MobileOneOff() with
    {
        Type = "Recurring",
        Frequency = "Weekly",
        RecurrenceDays = new[] { 2, 4 },
        PriceMonthly = 80m,
    };

    /// <summary>
    /// Covers: the body the mobile form sends — window as "opens N hours before", several week
    /// days without DayOfWeek, monthly price, private by code.
    /// </summary>
    [Fact]
    public async Task Mobile_form_payloads_pass_validation()
    {
        var privateByCode = MobileRecurring() with { Visibility = "Private", InviteMode = "Code" };

        foreach (var request in new[] { MobileOneOff(), MobileRecurring(), privateByCode })
        {
            (await _sut.ValidateAsync(request, CancellationToken.None)).IsValid.Should().BeTrue();
        }
    }

    /// <summary>
    /// Covers: the window comes in exactly one form — neither both nor none.
    /// </summary>
    [Fact]
    public async Task Window_must_come_in_exactly_one_form()
    {
        var both = MobileOneOff() with { WindowOpensAt = FutureWindowOpens, WindowClosesAt = FutureWindowCloses };
        var none = MobileOneOff() with { ConfirmationOpensHoursBefore = null };

        (await _sut.ValidateAsync(both, CancellationToken.None)).Errors
            .Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.WindowOpensAt));
        (await _sut.ValidateAsync(none, CancellationToken.None)).Errors
            .Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.WindowOpensAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(169)]
    public async Task ConfirmationOpensHoursBefore_out_of_range_fails_validation(int hours)
    {
        var request = MobileOneOff() with { ConfirmationOpensHoursBefore = hours };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.Errors.Should().Contain(
            e => e.PropertyName == nameof(CreateMatchRequest.ConfirmationOpensHoursBefore));
    }

    [Fact]
    public async Task Unknown_format_level_or_visibility_fails_validation()
    {
        var request = MobileOneOff() with { Format = "5X5", Level = "Elite", Visibility = "Secret", DurationMinutes = 29 };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.Errors.Select(e => e.PropertyName).Should().Contain(
        [
            nameof(CreateMatchRequest.Format),
            nameof(CreateMatchRequest.Level),
            nameof(CreateMatchRequest.Visibility),
            nameof(CreateMatchRequest.DurationMinutes),
        ]);
    }

    /// <summary>
    /// Covers: InviteMode is required for a Private match and refused for an Open one.
    /// </summary>
    [Theory]
    [InlineData("Private", null)]
    [InlineData("Private", "Carrier pigeon")]
    [InlineData("Open", "Code")]
    [InlineData(null, "Guests")]
    public async Task InviteMode_must_match_the_visibility(string? visibility, string? inviteMode)
    {
        var request = MobileOneOff() with { Visibility = visibility, InviteMode = inviteMode };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.InviteMode));
    }

    [Theory]
    [InlineData(new[] { 0, 3 })]
    [InlineData(new[] { 8 })]
    [InlineData(new[] { 2, 2 })]
    public async Task RecurrenceDays_must_be_distinct_iso_week_days(int[] days)
    {
        var request = MobileRecurring() with { RecurrenceDays = days };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMatchRequest.RecurrenceDays));
    }

    /// <summary>
    /// Covers: recurrence settings and the monthly price make no sense for a one-off match.
    /// </summary>
    [Fact]
    public async Task OneOff_with_recurrence_days_or_monthly_price_fails_validation()
    {
        var request = MobileOneOff() with { RecurrenceDays = new[] { 2 }, PriceMonthly = 80m };
        var result = await _sut.ValidateAsync(request, CancellationToken.None);
        result.Errors.Select(e => e.PropertyName).Should().Contain(
        [
            nameof(CreateMatchRequest.RecurrenceDays),
            nameof(CreateMatchRequest.PriceMonthly),
        ]);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static CreateMatchRequest BuildValidOneOff() =>
        new(
            Name: "Sunday Volleyball",
            Description: null,
            Address: "Rua Teste, 123, São Paulo",
            Latitude: -23.5505,
            Longitude: -46.6333,
            DateTime: FutureDateTime,
            MaxPlayers: 12,
            RegularSlots: 10,
            Price: null,
            Type: "OneOff",
            Frequency: null,
            DayOfWeek: null,
            WindowOpensAt: FutureWindowOpens,
            WindowClosesAt: FutureWindowCloses);

    private static CreateMatchRequest BuildValidRecurring() =>
        new(
            Name: "Weekly Volleyball",
            Description: null,
            Address: "Rua Teste, 123, São Paulo",
            Latitude: -23.5505,
            Longitude: -46.6333,
            DateTime: FutureDateTime,
            MaxPlayers: 12,
            RegularSlots: 10,
            Price: null,
            Type: "Recurring",
            Frequency: "Weekly",
            DayOfWeek: 3,  // Wednesday
            WindowOpensAt: FutureWindowOpens,
            WindowClosesAt: FutureWindowCloses);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
