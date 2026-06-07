[CmdletBinding()]
param(
    [string]$OutputPath,
    [switch]$ListOnly
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path -LiteralPath $PSScriptRoot).ProviderPath

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $RepoRoot "Snapshot\MDEdit.snapshot.md"
}

if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $SnapshotPath = [System.IO.Path]::GetFullPath($OutputPath)
}
else {
    $SnapshotPath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $OutputPath))
}

$ExcludedDirectoryNames = @(
    ".git",
    ".vs",
    ".idea",
    ".vscode",
    "bin",
    "obj",
    "Build",
    "Debug",
    "Release",
    "TestResults",
    "coverage",
    "artifacts",
    "dist",
    "out",
    "publish",
    "packages",
    "node_modules",
    "__pycache__",
    ".pytest_cache",
    "Snapshot"
)

$ExcludedExtensions = @(
    ".accdb",
    ".bin",
    ".cache",
    ".db",
    ".db-shm",
    ".db-wal",
    ".dll",
    ".exe",
    ".ldf",
    ".log",
    ".mdb",
    ".mdf",
    ".nupkg",
    ".obj",
    ".pdb",
    ".sdf",
    ".snupkg",
    ".sqlite",
    ".sqlite-shm",
    ".sqlite-wal",
    ".sqlite3",
    ".zip"
)

function Test-SamePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Left,
        [Parameter(Mandatory = $true)]
        [string]$Right
    )

    return [string]::Equals(
        [System.IO.Path]::GetFullPath($Left).TrimEnd("\", "/"),
        [System.IO.Path]::GetFullPath($Right).TrimEnd("\", "/"),
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $rootWithSlash = [System.IO.Path]::GetFullPath($Root).TrimEnd("\", "/") + "\"
    $rootUri = [System.Uri]::new($rootWithSlash)
    $pathUri = [System.Uri]::new([System.IO.Path]::GetFullPath($Path))
    return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString())
}

function Test-TextFile {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File
    )

    if ($File.Length -eq 0) {
        return $true
    }

    $stream = [System.IO.File]::OpenRead($File.FullName)
    try {
        $bufferLength = [Math]::Min(4096, [int]$File.Length)
        $buffer = New-Object byte[] $bufferLength
        $read = $stream.Read($buffer, 0, $bufferLength)

        for ($i = 0; $i -lt $read; $i++) {
            if ($buffer[$i] -eq 0) {
                return $false
            }
        }
    }
    finally {
        $stream.Dispose()
    }

    return $true
}

function Test-ExcludedFile {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File
    )

    if (Test-SamePath -Left $File.FullName -Right $SnapshotPath) {
        return $true
    }

    if ($ExcludedExtensions -contains $File.Extension) {
        return $true
    }

    if ($File.Name -like "*.binlog") {
        return $true
    }

    return -not (Test-TextFile -File $File)
}

function Get-SnapshotFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            continue
        }

        if ($item.PSIsContainer) {
            if ($ExcludedDirectoryNames -contains $item.Name) {
                continue
            }

            Get-SnapshotFiles -Directory $item.FullName
            continue
        }

        if (-not (Test-ExcludedFile -File $item)) {
            $item
        }
    }
}

function Get-LanguageName {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File
    )

    switch ($File.Extension.ToLowerInvariant()) {
        ".cs" { return "csharp" }
        ".csproj" { return "xml" }
        ".props" { return "xml" }
        ".targets" { return "xml" }
        ".xaml" { return "xml" }
        ".xml" { return "xml" }
        ".json" { return "json" }
        ".md" { return "markdown" }
        ".ps1" { return "powershell" }
        ".slnx" { return "xml" }
        ".txt" { return "text" }
        ".yml" { return "yaml" }
        ".yaml" { return "yaml" }
        default { return "text" }
    }
}

function Write-SnapshotFile {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.StreamWriter]$Writer,
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File,
        [Parameter(Mandatory = $true)]
        [int]$Index,
        [Parameter(Mandatory = $true)]
        [int]$Total
    )

    $relativePath = Get-RelativePath -Root $RepoRoot -Path $File.FullName
    $hash = (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines = [System.IO.File]::ReadAllLines($File.FullName)
    $lineNumberWidth = [Math]::Max(4, $lines.Length.ToString().Length)
    $language = Get-LanguageName -File $File

    $Writer.WriteLine()
    $Writer.WriteLine("---")
    $Writer.WriteLine()
    $Writer.WriteLine("<!-- BEGIN SNAPSHOT FILE: $relativePath -->")
    $Writer.WriteLine()
    $Writer.WriteLine("## File ${Index} of ${Total}: ``$relativePath``")
    $Writer.WriteLine()
    $Writer.WriteLine("- Relative path: ``$relativePath``")
    $Writer.WriteLine("- Size: $($File.Length) bytes")
    $Writer.WriteLine("- Last modified UTC: $($File.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"))")
    $Writer.WriteLine("- SHA-256: ``$hash``")
    $Writer.WriteLine("- Lines: $($lines.Length)")
    $Writer.WriteLine()
    $Writer.WriteLine("~~~~$language")

    if ($lines.Length -eq 0) {
        $Writer.WriteLine("0000 | ")
    }
    else {
        for ($i = 0; $i -lt $lines.Length; $i++) {
            $lineNumber = ($i + 1).ToString().PadLeft($lineNumberWidth, "0")
            $Writer.WriteLine("$lineNumber | $($lines[$i])")
        }
    }

    $Writer.WriteLine("~~~~")
    $Writer.WriteLine()
    $Writer.WriteLine("<!-- END SNAPSHOT FILE: $relativePath -->")
}

$Files = @(Get-SnapshotFiles -Directory $RepoRoot | Sort-Object FullName)

if ($ListOnly) {
    $Files | ForEach-Object { Get-RelativePath -Root $RepoRoot -Path $_.FullName }
    Write-Host "Would include $($Files.Count) file(s)."
    return
}

$SnapshotDirectory = Split-Path -Parent $SnapshotPath
if (-not (Test-Path -LiteralPath $SnapshotDirectory)) {
    New-Item -ItemType Directory -Path $SnapshotDirectory | Out-Null
}

if (Test-Path -LiteralPath $SnapshotPath) {
    Remove-Item -LiteralPath $SnapshotPath -Force
}

$encoding = [System.Text.UTF8Encoding]::new($false)
$writer = [System.IO.StreamWriter]::new($SnapshotPath, $false, $encoding)

try {
    $writer.WriteLine("# MDEdit Source Snapshot")
    $writer.WriteLine()
    $writer.WriteLine("- Repository root: ``$RepoRoot``")
    $writer.WriteLine("- Snapshot path: ``$SnapshotPath``")
    $writer.WriteLine("- Generated UTC: $([DateTime]::UtcNow.ToString("yyyy-MM-dd HH:mm:ss"))")
    $writer.WriteLine("- Included files: $($Files.Count)")
    $writer.WriteLine()
    $writer.WriteLine("This snapshot includes text files from the software repository and excludes generated outputs, database files, archives, the Git repository metadata, and this snapshot file.")

    for ($i = 0; $i -lt $Files.Count; $i++) {
        Write-SnapshotFile -Writer $writer -File $Files[$i] -Index ($i + 1) -Total $Files.Count
    }
}
finally {
    $writer.Dispose()
}

Write-Host "Snapshot refreshed: $SnapshotPath"
Write-Host "Included $($Files.Count) file(s)."
