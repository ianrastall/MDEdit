using MDEdit.Core.Models;

namespace MDEdit.Core.Interfaces;

public interface IMarkdownFormattingService
{
    string Format(string rawMarkdown, MarkdownFlavor flavor);
    MarkdownReflowResult ReflowHeadings(string rawMarkdown, MarkdownFlavor flavor, int topHeadingLevel);
}
