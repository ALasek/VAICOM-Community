param(
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot "VAICOM.Standalone\VAICOM.Standalone.csproj"
$source = Join-Path $repoRoot "VAICOM.Standalone\bin\Release\net472"
$outputsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "dist"))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $outputsRoot "VAICOM-Whisper-Standalone"
}
$target = [IO.Path]::GetFullPath($OutputDirectory)

if (-not $target.StartsWith($outputsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be inside the repository's dist directory: $outputsRoot"
}

dotnet restore $project
dotnet build $project -c Release -p:StandaloneBuild=true --no-restore -m:1
if ($LASTEXITCODE -ne 0) {
    throw "Standalone build failed."
}

$model = Join-Path $source "Models\ggml-small.en.bin"
if (-not (Test-Path -LiteralPath $model)) {
    & (Join-Path $source "VAICOM.Standalone.exe") --download-model
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $model)) {
        throw "Whisper small.en model download failed."
    }
}

$voskModels = Join-Path $source "Models"
$voskModel = Join-Path $voskModels "vosk-model-small-en-us-0.15"
if (-not (Test-Path -LiteralPath $voskModel)) {
    New-Item -ItemType Directory -Path $voskModels -Force | Out-Null
    $voskArchive = Join-Path $voskModels "vosk-model-small-en-us-0.15.zip"
    try {
        Invoke-WebRequest -Uri "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip" -OutFile $voskArchive
        Expand-Archive -LiteralPath $voskArchive -DestinationPath $voskModels -Force
    }
    finally {
        if (Test-Path -LiteralPath $voskArchive) {
            Remove-Item -LiteralPath $voskArchive -Force
        }
    }
    if (-not (Test-Path -LiteralPath $voskModel)) {
        throw "Vosk English model download failed."
    }
}

if (Test-Path -LiteralPath $target) {
    Get-ChildItem -LiteralPath $target -Force | Remove-Item -Recurse -Force
}

New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $source "*") -Destination $target -Recurse -Force

$unneeded = @(
    "runtimes\linux-arm",
    "runtimes\linux-arm64",
    "runtimes\linux-x64",
    "runtimes\macos-arm64",
    "runtimes\macos-x64",
    "runtimes\win-arm64",
    "runtimes\win-x86",
    "runtimes\cuda\linux-x64",
    "ggml-metal.metal",
    "VAICOMPRO"
)

foreach ($relativePath in $unneeded) {
    $path = Join-Path $target $relativePath
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

Copy-Item -LiteralPath (Join-Path $repoRoot "STANDALONE.md") -Destination (Join-Path $target "README.md")
Copy-Item -LiteralPath (Join-Path $repoRoot "THIRD_PARTY_NOTICES-STANDALONE.md") -Destination (Join-Path $target "THIRD_PARTY_NOTICES.md")
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENCE.md") -Destination (Join-Path $target "VAICOM-LICENCE.md")
Copy-Item -LiteralPath (Join-Path $repoRoot "VAICOM.Standalone\Start-VAICOM.cmd") -Destination $target
Copy-Item -LiteralPath (Join-Path $repoRoot "VAICOM.Standalone\List-Microphones.cmd") -Destination $target
Copy-Item -LiteralPath (Join-Path $repoRoot "VAICOM.Standalone\Self-Test.cmd") -Destination $target

$size = (Get-ChildItem -LiteralPath $target -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host "Package ready: $target"
Write-Host ("Size: {0:N1} MiB" -f ($size / 1MB))
