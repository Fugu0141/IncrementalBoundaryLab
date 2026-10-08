param(
    [string]$MozcDir = ".external\mozc",
    [string]$OutputDir = "artifacts\mozc",
    [switch]$UpdateDependencies
)

$ErrorActionPreference = "Stop"

$MozcCommit = "921b8cc99904c8d31b771e395513da0a5d55182a"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$MozcDir = Join-Path $RepoRoot $MozcDir
$OutputDir = Join-Path $RepoRoot $OutputDir

if (-not (Test-Path $MozcDir)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $MozcDir) | Out-Null
    git clone https://github.com/google/mozc.git $MozcDir
}

Push-Location $MozcDir
try {
    git fetch origin master
    git checkout $MozcCommit

    if ($UpdateDependencies) {
        Push-Location (Join-Path $MozcDir "src")
        try {
            python build_tools/update_deps.py
        }
        finally {
            Pop-Location
        }
    }

    $Overlay = Join-Path $RepoRoot "mozc_overlay\boundary_bridge"
    $Target = Join-Path $MozcDir "src\boundary_bridge"
    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    Copy-Item (Join-Path $Overlay "*") $Target -Force

    Push-Location (Join-Path $MozcDir "src")
    try {
        bazelisk build //boundary_bridge:boundary_mozc_bridge --config release_build
    }
    finally {
        Pop-Location
    }

    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    $Candidates = @(
        (Join-Path $MozcDir "src\bazel-bin\boundary_bridge\boundary_mozc_bridge.exe"),
        (Join-Path $MozcDir "src\bazel-bin\boundary_bridge\boundary_mozc_bridge")
    )

    $Built = $Candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $Built) {
        throw "boundary_mozc_bridge output was not found."
    }

    Copy-Item $Built (Join-Path $OutputDir "boundary_mozc_bridge.exe") -Force
    Write-Host "Mozc bridge built: $OutputDir\boundary_mozc_bridge.exe"
    Write-Host "Set BOUNDARYLAB_MOZC_BRIDGE to that path if auto-discovery does not find it."
}
finally {
    Pop-Location
}
