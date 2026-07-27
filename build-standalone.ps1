param(
    [string]$OutputDirectory,
    [switch]$IncludeWhisperModel,
    [switch]$IncludeCuda
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot "VAICOM.Standalone\VAICOM.Standalone.csproj"
$source = Join-Path $repoRoot "VAICOM.Standalone\bin\Release\net472"
$executable = Join-Path $source "VAICOM-Community-noVA.exe"
$outputsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "dist"))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $outputsRoot "VAICOM-Community-noVA"
}
$target = [IO.Path]::GetFullPath($OutputDirectory)

if (-not $target.StartsWith($outputsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be inside the repository's dist directory: $outputsRoot"
}

if ($IncludeCuda -and -not $IncludeWhisperModel) {
    throw "-IncludeCuda requires -IncludeWhisperModel."
}

$includeCudaValue = $IncludeCuda.IsPresent.ToString().ToLowerInvariant()
dotnet restore $project -p:IncludeWhisperCuda=$includeCudaValue
if ($LASTEXITCODE -ne 0) {
    throw "Standalone restore failed."
}

dotnet build $project -c Release -p:StandaloneBuild=true -p:IncludeWhisperCuda=$includeCudaValue --no-restore -m:1
if ($LASTEXITCODE -ne 0) {
    throw "Standalone build failed."
}

$model = Join-Path $source "Models\ggml-small.en.bin"
if ($IncludeWhisperModel -and -not (Test-Path -LiteralPath $model)) {
    & $executable --download-model
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $model)) {
        throw "Whisper small.en model download failed."
    }
}

$voskModels = Join-Path $source "Models"
$voskModel = Join-Path $voskModels "vosk-model-small-en-us-0.15"
$voskExpectedSha256 = "30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498"
$voskMarker = Join-Path $voskModel ".nova-source.sha256"
$voskVerified = (Test-Path -LiteralPath $voskMarker) -and
    ((Get-Content -LiteralPath $voskMarker -Raw).Trim() -eq $voskExpectedSha256)
if (-not (Test-Path -LiteralPath $voskModel) -or -not $voskVerified) {
    New-Item -ItemType Directory -Path $voskModels -Force | Out-Null
    $voskArchive = Join-Path $voskModels "vosk-model-small-en-us-0.15.zip"
    try {
        Invoke-WebRequest -Uri "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip" -OutFile $voskArchive
        $voskActualSha256 = (Get-FileHash -LiteralPath $voskArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($voskActualSha256 -ne $voskExpectedSha256) {
            throw "Vosk model checksum mismatch. Expected $voskExpectedSha256, got $voskActualSha256."
        }
        if (Test-Path -LiteralPath $voskModel) {
            Remove-Item -LiteralPath $voskModel -Recurse -Force
        }
        Expand-Archive -LiteralPath $voskArchive -DestinationPath $voskModels -Force
        Set-Content -LiteralPath $voskMarker -Value $voskExpectedSha256 -Encoding ascii -NoNewline
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

if (-not $IncludeWhisperModel) {
    $packagedWhisperModel = Join-Path $target "Models\ggml-small.en.bin"
    if (Test-Path -LiteralPath $packagedWhisperModel) {
        Remove-Item -LiteralPath $packagedWhisperModel -Force
    }
}

if (-not $IncludeCuda) {
    $packagedCudaRuntime = Join-Path $target "runtimes\cuda"
    if (Test-Path -LiteralPath $packagedCudaRuntime) {
        Remove-Item -LiteralPath $packagedCudaRuntime -Recurse -Force
    }
}

Get-ChildItem -LiteralPath $target -Filter "*.pdb" -Recurse -File | Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $repoRoot "STANDALONE.md") -Destination (Join-Path $target "README.md")
Copy-Item -LiteralPath (Join-Path $repoRoot "THIRD_PARTY_NOTICES-STANDALONE.md") -Destination (Join-Path $target "THIRD_PARTY_NOTICES.md")
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENCE.md") -Destination (Join-Path $target "VAICOM-LICENCE.md")
Copy-Item -LiteralPath (Join-Path $repoRoot "VAICOM.Standalone\Start-VAICOM.cmd") -Destination $target
Copy-Item -LiteralPath (Join-Path $repoRoot "VAICOM.Standalone\List-Microphones.cmd") -Destination $target
Copy-Item -LiteralPath (Join-Path $repoRoot "VAICOM.Standalone\Self-Test.cmd") -Destination $target

$size = (Get-ChildItem -LiteralPath $target -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host "Package ready: $target"
Write-Host ("Size: {0:N1} MiB" -f ($size / 1MB))
