using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Application;

/// <summary>Entity → contract mapping shared by the Matches endpoints.</summary>
public static class MatchMapper
{
    public static MatchResponse ToResponse(Match m) =>
        new(
            Id: m.Id,
            OrganizerId: m.OrganizerId,
            Name: m.Name,
            Description: m.Description,
            Address: m.Address,
            Latitude: m.Location.Y,
            Longitude: m.Location.X,
            DateTime: m.DateTime,
            MaxPlayers: m.MaxPlayers,
            RegularSlots: m.RegularSlots,
            DropInSlots: m.DropInSlots,
            Price: m.Price,
            Type: m.Type.ToString(),
            Frequency: m.Frequency?.ToString(),
            DayOfWeek: m.DayOfWeek.HasValue ? (int)m.DayOfWeek.Value : null,
            WindowOpensAt: m.WindowOpensAt,
            WindowClosesAt: m.WindowClosesAt,
            Status: m.Status.ToString(),
            CreatedAt: m.CreatedAt,
            UpdatedAt: m.UpdatedAt,
            Format: m.Format,
            Level: m.Level?.ToString(),
            DurationMinutes: m.DurationMinutes.HasValue ? (int)m.DurationMinutes.Value : null,
            Visibility: m.Visibility.ToString(),
            InviteMode: m.InviteMode?.ToString(),
            PriceMonthly: m.PriceMonthly,
            RecurrenceDays: m.RecurrenceDays);

    public static MatchPlayerResponse ToPlayer(PlayerSummary summary) =>
        new(
            summary.UserId,
            summary.DisplayName,
            summary.Handle,
            summary.Position,
            summary.Level,
            summary.PhotoUrl);
}
