namespace MDEdit.Core.Utilities;

public static class MarkdownTextUtilities
{
    public const string Crlf = "\r\n";

    public static string NormalizeLineEndingsToCrlf(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", Crlf, StringComparison.Ordinal);
    }

    public static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 1;
        }

        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                count++;
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
            }
            else if (text[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }
}
