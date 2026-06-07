[CmdletBinding()]
param(
    [string]$OutputPath,
    [int]$MaxPartCharacters = 80000,
    [switch]$ListOnly,
    [switch]$SkipParts
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

$SnapshotDirectory = Split-Path -Parent $SnapshotPath
$SnapshotBaseName = [System.IO.Path]::GetFileNameWithoutExtension($SnapshotPath)
$GeneratedUtc = [DateTime]::UtcNow.ToString("yyyy-MM-dd HH:mm:ss")

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

    if ($SnapshotDirectory -and
        $File.FullName.StartsWith($SnapshotDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

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

function Write-SnapshotHeader {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.TextWriter]$Writer,
        [Parameter(Mandatory = $true)]
        [int]$IncludedFileCount,
        [string]$PartLabel
    )

    $Writer.WriteLine("# MDEdit Source Snapshot")
    $Writer.WriteLine()
    $Writer.WriteLine("- Repository root: ``$RepoRoot``")
    $Writer.WriteLine("- Snapshot path: ``$SnapshotPath``")
    $Writer.WriteLine("- Generated UTC: $GeneratedUtc")
    $Writer.WriteLine("- Included files: $IncludedFileCount")
    if (-not [string]::IsNullOrWhiteSpace($PartLabel)) {
        $Writer.WriteLine("- Snapshot part: $PartLabel")
    }
    $Writer.WriteLine()
    $Writer.WriteLine("This snapshot includes text files from the software repository and excludes generated outputs, database files, archives, Git repository metadata, and generated snapshot files.")
    $Writer.WriteLine()
    $Writer.WriteLine("Integrity markers:")
    $Writer.WriteLine("- Every file section has BEGIN/END markers.")
    $Writer.WriteLine("- The full snapshot ends with ``<!-- END MDEDIT SOURCE SNAPSHOT -->``.")
    $Writer.WriteLine("- Part files end with ``<!-- END MDEDIT SOURCE SNAPSHOT PART n OF m -->``.")
}

function Write-SnapshotFooter {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.TextWriter]$Writer
    )

    $Writer.WriteLine()
    $Writer.WriteLine("---")
    $Writer.WriteLine()
    $Writer.WriteLine("<!-- END MDEDIT SOURCE SNAPSHOT -->")
}

function Write-SnapshotPartFooter {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.TextWriter]$Writer,
        [Parameter(Mandatory = $true)]
        [int]$PartNumber,
        [Parameter(Mandatory = $true)]
        [int]$PartCount
    )

    $Writer.WriteLine()
    $Writer.WriteLine("---")
    $Writer.WriteLine()
    $Writer.WriteLine("<!-- END MDEDIT SOURCE SNAPSHOT PART $PartNumber OF $PartCount -->")
}

function Write-SnapshotFile {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.TextWriter]$Writer,
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

function Convert-SnapshotFileToBlock {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File,
        [Parameter(Mandatory = $true)]
        [int]$Index,
        [Parameter(Mandatory = $true)]
        [int]$Total
    )

    $writer = [System.IO.StringWriter]::new()
    try {
        Write-SnapshotFile -Writer $writer -File $File -Index $Index -Total $Total
        return [pscustomobject]@{
            Text = $writer.ToString()
            RelativePath = Get-RelativePath -Root $RepoRoot -Path $File.FullName
        }
    }
    finally {
        $writer.Dispose()
    }
}

function Write-TextFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function New-FullSnapshotText {
    param(
        [Parameter(Mandatory = $true)]
        [array]$Blocks,
        [Parameter(Mandatory = $true)]
        [int]$IncludedFileCount
    )

    $writer = [System.IO.StringWriter]::new()
    try {
        Write-SnapshotHeader -Writer $writer -IncludedFileCount $IncludedFileCount
        foreach ($block in $Blocks) {
            $writer.Write($block.Text)
        }
        Write-SnapshotFooter -Writer $writer
        return $writer.ToString()
    }
    finally {
        $writer.Dispose()
    }
}

function Split-SnapshotBlocks {
    param(
        [Parameter(Mandatory = $true)]
        [array]$Blocks,
        [Parameter(Mandatory = $true)]
        [int]$MaxCharacters
    )

    $parts = @()
    $currentBlocks = @()
    $currentLength = 0
    $overheadAllowance = 2500

    foreach ($block in $Blocks) {
        $blockLength = $block.Text.Length
        if ($currentBlocks.Count -gt 0 -and
            ($currentLength + $blockLength + $overheadAllowance) -gt $MaxCharacters) {
            $parts += ,$currentBlocks
            $currentBlocks = @()
            $currentLength = 0
        }

        $currentBlocks += $block
        $currentLength += $blockLength
    }

    if ($currentBlocks.Count -gt 0) {
        $parts += ,$currentBlocks
    }

    return $parts
}

function Write-SnapshotParts {
    param(
        [Parameter(Mandatory = $true)]
        [array]$Blocks,
        [Parameter(Mandatory = $true)]
        [int]$IncludedFileCount
    )

    if ($SkipParts) {
        return @()
    }

    if ($MaxPartCharacters -lt 20000) {
        throw "MaxPartCharacters must be at least 20000."
    }

    foreach ($oldPart in Get-ChildItem -LiteralPath $SnapshotDirectory -Filter "$SnapshotBaseName.part-*.md" -Force -ErrorAction SilentlyContinue) {
        Remove-Item -LiteralPath $oldPart.FullName -Force
    }

    $manifestPath = Join-Path $SnapshotDirectory "$SnapshotBaseName.manifest.md"
    if (Test-Path -LiteralPath $manifestPath) {
        Remove-Item -LiteralPath $manifestPath -Force
    }

    $parts = Split-SnapshotBlocks -Blocks $Blocks -MaxCharacters $MaxPartCharacters
    $partInfos = @()
    $partCount = $parts.Count
    $partNumberWidth = [Math]::Max(3, $partCount.ToString().Length)

    for ($i = 0; $i -lt $partCount; $i++) {
        $partNumber = $i + 1
        $partFileName = "$SnapshotBaseName.part-$($partNumber.ToString().PadLeft($partNumberWidth, "0"))-of-$($partCount.ToString().PadLeft($partNumberWidth, "0")).md"
        $partPath = Join-Path $SnapshotDirectory $partFileName
        $partBlocks = @($parts[$i])
        $firstFile = $partBlocks[0].RelativePath
        $lastFile = $partBlocks[$partBlocks.Count - 1].RelativePath

        $writer = [System.IO.StringWriter]::new()
        try {
            Write-SnapshotHeader `
                -Writer $writer `
                -IncludedFileCount $IncludedFileCount `
                -PartLabel "$partNumber of $partCount; files in this part: $($partBlocks.Count); range: $firstFile through $lastFile"

            foreach ($block in $partBlocks) {
                $writer.Write($block.Text)
            }

            Write-SnapshotPartFooter -Writer $writer -PartNumber $partNumber -PartCount $partCount
            Write-TextFile -Path $partPath -Text $writer.ToString()
        }
        finally {
            $writer.Dispose()
        }

        $partFile = Get-Item -LiteralPath $partPath
        $partInfos += [pscustomobject]@{
            Number = $partNumber
            Path = $partPath
            Name = $partFileName
            Length = $partFile.Length
            Hash = (Get-FileHash -LiteralPath $partPath -Algorithm SHA256).Hash.ToLowerInvariant()
            FirstFile = $firstFile
            LastFile = $lastFile
            FileCount = $partBlocks.Count
        }
    }

    Write-SnapshotManifest -ManifestPath $manifestPath -PartInfos $partInfos
    return $partInfos
}

function Write-SnapshotManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath,
        [Parameter(Mandatory = $true)]
        [array]$PartInfos
    )

    $writer = [System.IO.StringWriter]::new()
    try {
        $writer.WriteLine("# MDEdit Snapshot Manifest")
        $writer.WriteLine()
        $writer.WriteLine("- Generated UTC: $GeneratedUtc")
        $writer.WriteLine("- Full snapshot: ``$(Split-Path -Leaf $SnapshotPath)``")
        $writer.WriteLine("- Full snapshot SHA-256: ``$((Get-FileHash -LiteralPath $SnapshotPath -Algorithm SHA256).Hash.ToLowerInvariant())``")
        $writer.WriteLine("- Part files: $($PartInfos.Count)")
        $writer.WriteLine("- Included source files: $($Files.Count)")
        $writer.WriteLine()
        $writer.WriteLine("Use the part files when a consumer truncates large Markdown files. Each part has an explicit END marker.")
        $writer.WriteLine()
        $writer.WriteLine("| Part | File | Bytes | SHA-256 | Source file range |")
        $writer.WriteLine("| ---: | --- | ---: | --- | --- |")

        foreach ($part in $PartInfos) {
            $writer.WriteLine("| $($part.Number) | ``$($part.Name)`` | $($part.Length) | ``$($part.Hash)`` | ``$($part.FirstFile)`` through ``$($part.LastFile)`` |")
        }

        $writer.WriteLine()
        $writer.WriteLine("<!-- END MDEDIT SNAPSHOT MANIFEST -->")
        Write-TextFile -Path $ManifestPath -Text $writer.ToString()
    }
    finally {
        $writer.Dispose()
    }
}

$Files = @(Get-SnapshotFiles -Directory $RepoRoot | Sort-Object FullName)

if ($ListOnly) {
    $Files | ForEach-Object { Get-RelativePath -Root $RepoRoot -Path $_.FullName }
    Write-Host "Would include $($Files.Count) file(s)."
    return
}

if (-not (Test-Path -LiteralPath $SnapshotDirectory)) {
    New-Item -ItemType Directory -Path $SnapshotDirectory | Out-Null
}

if (Test-Path -LiteralPath $SnapshotPath) {
    Remove-Item -LiteralPath $SnapshotPath -Force
}

$blocks = for ($i = 0; $i -lt $Files.Count; $i++) {
    Convert-SnapshotFileToBlock -File $Files[$i] -Index ($i + 1) -Total $Files.Count
}

$fullSnapshot = New-FullSnapshotText -Blocks @($blocks) -IncludedFileCount $Files.Count
Write-TextFile -Path $SnapshotPath -Text $fullSnapshot
$partInfos = Write-SnapshotParts -Blocks @($blocks) -IncludedFileCount $Files.Count

Write-Host "Snapshot refreshed: $SnapshotPath"
Write-Host "Included $($Files.Count) file(s)."
if (-not $SkipParts) {
    Write-Host "Part files refreshed: $($partInfos.Count)"
    Write-Host "Manifest refreshed: $(Join-Path $SnapshotDirectory "$SnapshotBaseName.manifest.md")"
}
