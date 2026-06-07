namespace MDEdit.Core.Models;

public sealed record HeadingLevelOption(int Level, string DisplayName)
{
    public static HeadingLevelOption H1 { get; } = new(1, "H1");
    public static HeadingLevelOption H2 { get; } = new(2, "H2");
    public static HeadingLevelOption H3 { get; } = new(3, "H3");

    public static IReadOnlyList<HeadingLevelOption> All { get; } =
    [
        H1,
        H2,
        H3,
    ];

    public static HeadingLevelOption FromLevel(int level) => level switch
    {
        1 => H1,
        2 => H2,
        3 => H3,
        _ => H1,
    };

    public override string ToString() => DisplayName;
}
