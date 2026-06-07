using System.Collections.ObjectModel;

namespace MDEdit.Core.Models;

public sealed class HeadingNode
{
    public string Title { get; init; } = string.Empty;
    public int Level { get; init; }
    public int LineNumber { get; init; }
    public int CharacterOffset { get; init; }
    public ObservableCollection<HeadingNode> Children { get; } = [];
}
