using MDEdit.Application.ViewModels;
using MDEdit.Core.Interfaces;
using MDEdit.Core.Models;

namespace MDEdit.Tests;

public sealed class EditorWorkflowTests
{
    [Fact]
    public void AppendToEnd_AddsContentAfterBlankLine()
    {
        var document = new DocumentViewModel(new PassthroughFormatter());

        document.Load("sample.md", "# Title\n");
        document.AppendToEnd("- item");

        Assert.Equal("# Title\r\n\r\n- item", document.RawMarkdown);
        Assert.True(document.IsDirty);
        Assert.Equal("Content appended.", document.StatusMessage);
    }

    [Fact]
    public void Load_NormalizesLineEndingsToCrlfAndReportsSourceEol()
    {
        var document = new DocumentViewModel(new PassthroughFormatter());

        document.Load("sample.md", "# Title\n\nBody");

        Assert.Equal("# Title\r\n\r\nBody", document.RawMarkdown);
        Assert.Equal("LF -> CRLF", document.LineEndingStatusText);
        Assert.True(document.IsDirty);

        document.MarkSaved("sample.md");

        Assert.Equal("CRLF", document.LineEndingStatusText);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void Outline_ParsesHeadingHierarchyAndIgnoresCodeFences()
    {
        var document = new DocumentViewModel(new PassthroughFormatter());

        document.SetMarkdownFromEditor("""
            # Root

            ### Child

            ```
            # Not a heading
            ```

            ## Peer
            """);

        HeadingNode root = Assert.Single(document.HeadingNodes);
        Assert.Equal("Root", root.Title);
        Assert.Equal(2, root.Children.Count);
        Assert.Equal("Child", root.Children[0].Title);
        Assert.Equal("Peer", root.Children[1].Title);
    }

    [Fact]
    public void CloseAllSaved_LeavesDirtyTabsOpen()
    {
        var shell = new ShellViewModel(
            new DocumentServiceProvider(),
            new FakeFilePickerService(),
            new FakeApplicationService());

        DocumentViewModel firstSaved = shell.ActiveTab!;
        firstSaved.Load("first.md", "# First");

        shell.NewTab();
        DocumentViewModel dirty = shell.ActiveTab!;
        dirty.SetMarkdownFromEditor("# Dirty");

        shell.NewTab();
        DocumentViewModel secondSaved = shell.ActiveTab!;
        secondSaved.Load("second.md", "# Second");

        shell.ActiveTab = dirty;

        shell.CloseAllSaved();

        DocumentViewModel remaining = Assert.Single(shell.Tabs);
        Assert.Same(dirty, remaining);
        Assert.Same(dirty, shell.ActiveTab);
        Assert.Contains("unsaved tab(s) remain", dirty.StatusMessage);
    }

    private sealed class PassthroughFormatter : IMarkdownFormattingService
    {
        public string Format(string rawMarkdown, MarkdownFlavor flavor) => rawMarkdown;

        public MarkdownReflowResult ReflowHeadings(
            string rawMarkdown,
            MarkdownFlavor flavor,
            int topHeadingLevel) =>
            new(rawMarkdown, 0, []);
    }

    private sealed class DocumentServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(DocumentViewModel)
                ? new DocumentViewModel(new PassthroughFormatter())
                : null;
        }
    }

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public Task<string?> PickOpenMarkdownFileAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickSaveMarkdownFileAsync(string suggestedFileName) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeApplicationService : IApplicationService
    {
        public void Exit()
        {
        }
    }
}
