namespace Quadra.Shared.Contracts;

/// <summary>
/// The names of a match's teams, in the order they are drawn and shown, one per color of the
/// brand palette (DECISIONS #88): Azul #1A1AFF, Limão #AADD00, Laranja #FF6B00, Roxo #6B1AFF.
/// Two teams are Azul × Limão; Laranja and Roxo join as the count grows. The app colors a team
/// by its place in this order.
/// </summary>
public static class TeamPalette
{
    public static readonly IReadOnlyList<string> Names = ["Time Azul", "Time Limão", "Time Laranja", "Time Roxo"];

    /// <summary>
    /// Where a team sorts: its place in <see cref="Names"/>; names from before the palette
    /// ("Team A"…) come after, in name order (callers break ties by name).
    /// </summary>
    public static int Order(string name)
    {
        for (var i = 0; i < Names.Count; i++)
        {
            if (string.Equals(Names[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return Names.Count;
    }
}
