using FluentValidation;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Validation;

public sealed class SavePlayerStatsRequestValidator : AbstractValidator<SavePlayerStatsRequest>
{
    /// <summary>More than any match can hold (24 slots), so a body cannot be inflated.</summary>
    private const int MaxPlayers = 60;

    public SavePlayerStatsRequestValidator()
    {
        RuleFor(x => x.Players).NotNull();
        RuleFor(x => x.Players.Count).LessThanOrEqualTo(MaxPlayers).When(x => x.Players is not null);
        RuleFor(x => x.Players)
            .Must(p => p.Select(e => e.PlayerId).Distinct().Count() == p.Count)
            .When(x => x.Players is not null)
            .WithMessage("Each player can appear only once.");

        RuleForEach(x => x.Players).ChildRules(entry =>
        {
            entry.RuleFor(e => e.PlayerId).NotEmpty();
            entry.RuleFor(e => e.Kills).InclusiveBetween(0, PlayerMatchStats.MaxCount);
            entry.RuleFor(e => e.Blocks).InclusiveBetween(0, PlayerMatchStats.MaxCount);
            entry.RuleFor(e => e.Aces).InclusiveBetween(0, PlayerMatchStats.MaxCount);
            entry.RuleFor(e => e.Digs).InclusiveBetween(0, PlayerMatchStats.MaxCount);
            entry.RuleFor(e => e.Assists).InclusiveBetween(0, PlayerMatchStats.MaxCount);
        });
    }
}
