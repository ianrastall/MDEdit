# MDEdit

MDEdit is a Windows Markdown editor focused on fast manuscript editing and heading-structure work. It combines a plain source editor, live Markdown preview, heading outline, formatting, and deterministic structure tools for numbering and reshaping Markdown heading hierarchies.

The app is built with WinUI 3 and .NET, with Markdig handling Markdown parsing and formatting.

## Features

- Tabbed Markdown and plain-text editing.
- Open, save, save as, close tab, and close all saved tabs.
- Editor and preview modes with a rendered Markdown preview.
- Heading outline pane with click-to-jump navigation.
- Line numbers and current-line highlighting in the editor.
- Source encoding and line-ending status in the status bar.
- Markdown flavor selection:
  - CommonMark
  - GitHub Flavored Markdown
  - Markdig Advanced
  - Pandoc Markdown
  - MultiMarkdown
- Markdown formatting through Markdig.
- Heading reflow from the status bar, with top-level choices of H1, H2, or H3.
- Heading structure tools:
  - Number headings as-is, such as `# 1 Title`, `## 1.1 Section`, `### 1.1.1 Detail`.
  - Remove existing heading numbers.
  - Promote or demote the heading under the cursor.
  - Promote or demote the whole subtree under the cursor.
  - Renumber automatically after heading-level changes when the document is already numbered.
- Application icon assets included under `src/MDEdit.WinUI/Assets`.

## Heading Structure Workflow

The Structure menu and second toolbar row are intended for book-scale outline cleanup without a separate audit window.

Place the cursor on a Markdown heading line, then use:

- **Promote** to move only that heading up one level, such as `### Title` to `## Title`.
- **Demote** to move only that heading down one level.
- **Promote Tree** to move the current heading and all child headings up one level.
- **Demote Tree** to move the current heading and all child headings down one level.

Subtree operations include following headings with deeper levels and stop at the next heading with the same or shallower level.

Example:

```md
## Parent
### Child A
#### Grandchild
### Child B
## Next Parent
```

With the cursor on `Parent`, a subtree command affects `Parent`, `Child A`, `Grandchild`, and `Child B`, but not `Next Parent`.

If the document already has heading numbers, MDEdit renumbers after each promote/demote operation so the outline reflects the new hierarchy immediately.

## Requirements

- Windows 10 version 2004 / build 19041 or newer.
- x64 Windows runtime.
- .NET 10 SDK.

The WinUI project targets:

```xml
net10.0-windows10.0.19041.0
```

## Build

Restore and build from the repository root:

```powershell
dotnet restore MDEdit.slnx
dotnet build src/MDEdit.WinUI/MDEdit.WinUI.csproj --no-restore
```

Run the test suite:

```powershell
dotnet test tests/MDEdit.Tests/MDEdit.Tests.csproj --no-restore
```

Run the app after building:

```powershell
.\src\MDEdit.WinUI\bin\Debug\net10.0-windows10.0.19041.0\win-x64\MDEdit.WinUI.exe
```

### Build Troubleshooting

Windows App SDK resource generation may use the system temp drive. If that drive is full, set `TEMP` and `TMP` to a location with space before building:

```powershell
$env:TEMP = "$PWD\.tmp-build"
$env:TMP = "$PWD\.tmp-build"
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
dotnet build src/MDEdit.WinUI/MDEdit.WinUI.csproj --no-restore /p:UseSharedCompilation=false
```

## Project Layout

```text
src/
  MDEdit.Core/            Domain models and interfaces.
  MDEdit.Application/     ViewModels and editor workflows.
  MDEdit.Infrastructure/  Markdown formatting implementation.
  MDEdit.WinUI/           WinUI 3 app, views, services, and assets.

tests/
  MDEdit.Tests/           xUnit tests for editor workflows and formatting.
```

## Notes

- Formatting and structure operations are deterministic and local; there is no AI dependency.
- Heading structure tools only rewrite heading lines. They preserve surrounding prose and document layout.
- Fenced code blocks and front matter are ignored by heading parsing and structure operations.
