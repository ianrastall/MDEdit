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
    public void NewDocumentAndSingleLineTextReportNoLineEnding()
    {
        var document = new DocumentViewModel(new PassthroughFormatter());

        Assert.Equal("N/A", document.LineEndingStatusText);

        document.SetMarkdownFromEditor("# Title");

        Assert.Equal("N/A", document.LineEndingStatusText);
    }

    [Fact]
    public void Load_CleanDocumentDoesNotPublishDirtyTitle()
    {
        var document = new DocumentViewModel(new PassthroughFormatter());
        var titleChanges = new List<string>();
        document.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.DocumentTitle))
            {
                titleChanges.Add(document.DocumentTitle);
            }
        };

        document.Load("sample.md", "# Title\r\n");

        Assert.False(document.IsDirty);
        Assert.Equal("sample.md", document.DocumentTitle);
        Assert.DoesNotContain("* sample.md", titleChanges);
    }

    [Fact]
    public void MarkSaved_PublishesCleanDocumentTitle()
    {
        var document = new DocumentViewModel(new PassthroughFormatter());
        var titleChanges = new List<string>();
        document.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.DocumentTitle))
            {
                titleChanges.Add(document.DocumentTitle);
            }
        };

        document.Load("sample.md", "# Title");
        document.SetMarkdownFromEditor("# Updated");

        Assert.Equal("* sample.md", document.DocumentTitle);

        titleChanges.Clear();
        document.MarkSaved("sample.md");

        Assert.False(document.IsDirty);
        Assert.Equal("sample.md", document.DocumentTitle);
        Assert.Contains("sample.md", titleChanges);
        Assert.DoesNotContain("* sample.md", titleChanges);
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

        Assert.True(
            SpinWait.SpinUntil(() => document.HeadingNodes.Count > 0, TimeSpan.FromSeconds(1)),
            "Heading parse did not complete.");

        HeadingNode root = Assert.Single(document.HeadingNodes);
        Assert.Equal("Root", root.Title);
        Assert.Equal(2, root.Children.Count);
        Assert.Equal("Child", root.Children[0].Title);
        Assert.Equal("Peer", root.Children[1].Title);
    }

    [Fact]
    public void Outline_DoesNotCloseFenceWhenFenceCharactersHaveTrailingContent()
    {
        var document = new DocumentViewModel(new PassthroughFormatter());

        document.SetMarkdownFromEditor("""
            # Root

            ```
            ````extra
            # Not a heading
            ```

            ## Peer
            """);

        Assert.True(
            SpinWait.SpinUntil(() => document.HeadingNodes.Count > 0, TimeSpan.FromSeconds(1)),
            "Heading parse did not complete.");

        HeadingNode root = Assert.Single(document.HeadingNodes);
        HeadingNode child = Assert.Single(root.Children);
        Assert.Equal("Peer", child.Title);
    }

    [Fact]
    public void ToggleOutlineVisibility_TogglesViewModelState()
    {
        var document = new DocumentViewModel(new PassthroughFormatter());

        document.ToggleOutlineVisibility();
        Assert.True(document.IsOutlineVisible);

        document.ToggleOutlineVisibility();
        Assert.False(document.IsOutlineVisible);
    }

    [Fact]
    public void NumberHeadings_AddsHierarchyNumbersWithoutChangingLevels()
    {
        var document = new DocumentViewModel(new PassthroughFormatter());

        document.SetMarkdownFromEditor("""
            # Intro

            ## Background

            ### Detail

            # Next
            """);

        Assert.True(document.NumberHeadings());

        Assert.Equal(
            "# 1 Intro\r\n\r\n## 1.1 Background\r\n\r\n### 1.1.1 Detail\r\n\r\n# 2 Next",
            document.RawMarkdown);
        Assert.Contains("Numbered headings", document.StatusMessage);
    }

    [Fact]
    public void RemoveHeadingNumbers_StripsHierarchyNumbersWithoutChangingLevels()
    {
        var document = new DocumentViewModel(new PassthroughFormatter());

        document.SetMarkdownFromEditor("""
            # 1 Intro

            ## 1.1 Background

            ### 1.1.1 Detail
            """);

        Assert.True(document.RemoveHeadingNumbers());

        Assert.Equal("# Intro\r\n\r\n## Background\r\n\r\n### Detail", document.RawMarkdown);
    }

    [Fact]
    public void PromoteHeading_ChangesOnlyCurrentHeadingAndRenumbersDocument()
    {
        var document = new DocumentViewModel(new PassthroughFormatter());
        document.SetMarkdownFromEditor("""
            # 1 Intro

            ## 1.1 Background

            ### 1.1.1 Detail

            ## 1.2 Other
            """);

        int offset = document.RawMarkdown.IndexOf("Detail", StringComparison.Ordinal);

        Assert.True(document.TryChangeHeadingLevelAtOffset(offset, levelDelta: -1, includeSubtree: false, out _));

        Assert.Equal(
            "# 1 Intro\r\n\r\n## 1.1 Background\r\n\r\n## 1.2 Detail\r\n\r\n## 1.3 Other",
            document.RawMarkdown);
    }

    [Fact]
    public void PromoteSubtree_ChangesCurrentHeadingAndChildrenThenRenumbersDocument()
    {
        var document = new DocumentViewModel(new PassthroughFormatter());
        document.SetMarkdownFromEditor("""
            # 1 Intro

            ## 1.1 Part

            ### 1.1.1 Child

            #### 1.1.1.1 Grandchild

            ## 1.2 Next
            """);

        int offset = document.RawMarkdown.IndexOf("Part", StringComparison.Ordinal);

        Assert.True(document.TryChangeHeadingLevelAtOffset(offset, levelDelta: -1, includeSubtree: true, out _));

        Assert.Equal(
            "# 1 Intro\r\n\r\n# 2 Part\r\n\r\n## 2.1 Child\r\n\r\n### 2.1.1 Grandchild\r\n\r\n## 2.2 Next",
            document.RawMarkdown);
    }

    [Fact]
    public void HeadingLevelCommands_RespectLevelBoundaries()
    {
        var document = new DocumentViewModel(new PassthroughFormatter());
        document.SetMarkdownFromEditor("""
            # Root

            ###### Deep
            """);

        int rootOffset = document.RawMarkdown.IndexOf("Root", StringComparison.Ordinal);
        int deepOffset = document.RawMarkdown.IndexOf("Deep", StringComparison.Ordinal);

        Assert.False(document.CanChangeHeadingLevelAtOffset(rootOffset, levelDelta: -1, includeSubtree: false));
        Assert.False(document.CanChangeHeadingLevelAtOffset(deepOffset, levelDelta: 1, includeSubtree: false));
    }

    [Fact]
    public async Task ExitCommand_PromptsWhenTabsAreDirty()
    {
        var application = new FakeApplicationService();
        var prompt = new FakeUnsavedChangesPromptService(shouldConfirm: false);
        var shell = new ShellViewModel(
            new DocumentServiceProvider(),
            new FakeFilePickerService(),
            application,
            prompt);

        shell.ActiveTab!.SetMarkdownFromEditor("# Dirty");

        await shell.ExitCommand.ExecuteAsync(null);

        Assert.Equal(1, prompt.PromptCount);
        Assert.False(application.DidExit);
        Assert.Contains("unsaved changes remain", shell.ActiveTab.StatusMessage);
    }

    [Fact]
    public async Task OpenFileAsync_ActivatesAlreadyOpenTab()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(filePath, "# Existing");

        try
        {
            var shell = new ShellViewModel(
                new DocumentServiceProvider(),
                new FakeFilePickerService(),
                new FakeApplicationService(),
                new FakeUnsavedChangesPromptService(shouldConfirm: true));

            await shell.OpenFileAsync(filePath);
            DocumentViewModel openedTab = shell.ActiveTab!;

            shell.NewTab();
            await shell.OpenFileAsync(filePath);

            Assert.Equal(2, shell.Tabs.Count);
            Assert.Same(openedTab, shell.ActiveTab);
            Assert.Contains("Already open", openedTab.StatusMessage);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task SaveAsCommand_BlocksPathAlreadyOpenInAnotherTab()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(filePath, "# Existing");

        try
        {
            var shell = new ShellViewModel(
                new DocumentServiceProvider(),
                new FakeFilePickerService(savePath: filePath),
                new FakeApplicationService(),
                new FakeUnsavedChangesPromptService(shouldConfirm: true));

            await shell.OpenFileAsync(filePath);
            shell.NewTab();
            DocumentViewModel unsavedTab = shell.ActiveTab!;
            unsavedTab.SetMarkdownFromEditor("# Replacement");

            await shell.SaveAsCommand.ExecuteAsync(null);

            Assert.Null(unsavedTab.Document.FilePath);
            Assert.Contains("already open", unsavedTab.StatusMessage);
            Assert.Equal("# Existing", await File.ReadAllTextAsync(filePath));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void CloseTab_KeepsActiveTabWhenClosingBackgroundTab()
    {
        var shell = new ShellViewModel(
            new DocumentServiceProvider(),
            new FakeFilePickerService(),
            new FakeApplicationService(),
            new FakeUnsavedChangesPromptService(shouldConfirm: true));

        DocumentViewModel first = shell.ActiveTab!;
        shell.NewTab();
        DocumentViewModel second = shell.ActiveTab!;
        shell.NewTab();
        DocumentViewModel third = shell.ActiveTab!;
        shell.NewTab();
        DocumentViewModel fourth = shell.ActiveTab!;

        shell.CloseTab(second);

        Assert.DoesNotContain(second, shell.Tabs);
        Assert.Equal(3, shell.Tabs.Count);
        Assert.Same(fourth, shell.ActiveTab);
        Assert.Contains(first, shell.Tabs);
        Assert.Contains(third, shell.Tabs);
    }

    [Fact]
    public void CloseAllSaved_LeavesDirtyTabsOpen()
    {
        var shell = new ShellViewModel(
            new DocumentServiceProvider(),
            new FakeFilePickerService(),
            new FakeApplicationService(),
            new FakeUnsavedChangesPromptService(shouldConfirm: true));

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

    private sealed class FakeFilePickerService(string? openPath = null, string? savePath = null) : IFilePickerService
    {
        public Task<string?> PickOpenMarkdownFileAsync() => Task.FromResult(openPath);

        public Task<string?> PickSaveMarkdownFileAsync(string suggestedFileName) =>
            Task.FromResult(savePath);
    }

    private sealed class FakeApplicationService : IApplicationService
    {
        public bool DidExit { get; private set; }

        public Task ExitAsync()
        {
            DidExit = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnsavedChangesPromptService(bool shouldConfirm) : IUnsavedChangesPromptService
    {
        public int PromptCount { get; private set; }

        public Task<bool> ConfirmDiscardUnsavedChangesAsync(int unsavedDocumentCount)
        {
            PromptCount++;
            return Task.FromResult(shouldConfirm);
        }
    }
}
