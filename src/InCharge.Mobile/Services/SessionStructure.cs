namespace InCharge.Mobile.Services;

// Mirrors SessionStructure in docs/contract.md: what a run was planned to be when it started.
public class SessionStructure
{
    public int? WarmupSeconds { get; set; }
    public List<StructureGroup> Groups { get; set; } = new();
    public int? CooldownSeconds { get; set; }

    public static SessionStructure FromSession(AdvancedSession session) => new()
    {
        WarmupSeconds = session.WarmupEnabled && session.WarmupDurationSeconds > 0 ? session.WarmupDurationSeconds : null,
        CooldownSeconds = session.CooldownEnabled && session.CooldownDurationSeconds > 0 ? session.CooldownDurationSeconds : null,
        Groups = session.Groups
            .Where(g => g.Blocks.Count > 0)
            .Select(g => new StructureGroup
            {
                Repeat = Math.Max(1, g.RepeatCount),
                Blocks = g.Blocks.Select(b => new StructureBlock
                {
                    Kind = b.IsMove ? StructureBlock.Move : StructureBlock.Rest,
                    Seconds = Math.Max(1, b.DurationSeconds),
                    Label = IntervalBlock.CleanLabel(b.Label)
                }).ToList()
            }).ToList()
    };

    public static SessionStructure ForBasic(int moveSeconds, int restSeconds, int repeats, int? warmupSeconds, int? cooldownSeconds) => new()
    {
        WarmupSeconds = warmupSeconds,
        CooldownSeconds = cooldownSeconds,
        Groups =
        [
            new StructureGroup
            {
                Repeat = Math.Max(1, repeats),
                Blocks =
                [
                    new StructureBlock { Kind = StructureBlock.Move, Seconds = Math.Max(1, moveSeconds) },
                    new StructureBlock { Kind = StructureBlock.Rest, Seconds = Math.Max(1, restSeconds) }
                ]
            }
        ]
    };
}

public class StructureGroup
{
    public int Repeat { get; set; } = 1;
    public List<StructureBlock> Blocks { get; set; } = new();
}

public class StructureBlock
{
    public const string Move = "move";
    public const string Rest = "rest";

    public string Kind { get; set; } = Move;
    public int Seconds { get; set; }
    public string? Label { get; set; }
}
