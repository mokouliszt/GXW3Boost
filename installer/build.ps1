# ============================================================
# GXW3Boost ビルドスクリプト
#
# 使い方:
#   .\build.ps1           # framework-dependent ビルド（.NET 8ランタイムが必要）
#   .\build.ps1 -SelfContained   # self-contained ビルド（ランタイム同梱、推奨）
#   .\build.ps1 -SkipInstaller   # Inno Setup をスキップ
# ============================================================

param(
    [switch]$SelfContained,
    [switch]$SkipInstaller,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishRoot = Join-Path $PSScriptRoot "..\publish"
$DistDir = Join-Path $PSScriptRoot "..\dist"

function Invoke-Step([string]$Name, [scriptblock]$Block) {
    Write-Host ""
    Write-Host "━━ $Name ━━" -ForegroundColor Cyan
    $LASTEXITCODE = 0
    & $Block
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ $Name に失敗しました (exit code: $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host "✅ $Name 完了" -ForegroundColor Green
}

# ========== クリーンアップ ==========
Invoke-Step "クリーンアップ" {
    if (Test-Path $PublishRoot) { Remove-Item $PublishRoot -Recurse -Force }
    if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
    New-Item -ItemType Directory -Path $PublishRoot | Out-Null
    New-Item -ItemType Directory -Path $DistDir | Out-Null
}

# ========== Launcher のビルド ==========
$LauncherOut = Join-Path $PublishRoot "launcher"
Invoke-Step "Launcher ビルド" {
    $publishArgs = @(
        "publish"
        (Join-Path $RepoRoot "GXW3Boost.Launcher\GXW3Boost.Launcher.csproj")
        "-c", $Configuration
        "-o", $LauncherOut
    )
    if ($SelfContained) {
        $publishArgs += "-r", $Runtime, "--self-contained", "true"
        $publishArgs += "-p:PublishSingleFile=true"
        $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
    } else {
        $publishArgs += "--no-self-contained"
    }
    & dotnet @publishArgs
}

# ========== Warmer のビルド ==========
$WarmerOut = Join-Path $PublishRoot "warmer"
Invoke-Step "Warmer ビルド" {
    $publishArgs = @(
        "publish"
        (Join-Path $RepoRoot "GXW3Boost.Warmer\GXW3Boost.Warmer.csproj")
        "-c", $Configuration
        "-o", $WarmerOut
    )
    if ($SelfContained) {
        $publishArgs += "-r", $Runtime, "--self-contained", "true"
        $publishArgs += "-p:PublishSingleFile=true"
        $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
    } else {
        $publishArgs += "--no-self-contained"
    }
    & dotnet @publishArgs
}

# ========== ビルド結果確認 ==========
Invoke-Step "成果物確認" {
    $launcherExe = Join-Path $LauncherOut "GXW3Boost.Launcher.exe"
    $warmerExe   = Join-Path $WarmerOut   "GXW3Boost.Warmer.exe"

    if (-not (Test-Path $launcherExe)) { throw "Launcher.exe が見つかりません: $launcherExe" }
    if (-not (Test-Path $warmerExe))   { throw "Warmer.exe が見つかりません: $warmerExe" }

    Write-Host "  Launcher : $launcherExe ($([int]((Get-Item $launcherExe).Length / 1KB)) KB)"
    Write-Host "  Warmer   : $warmerExe ($([int]((Get-Item $warmerExe).Length / 1KB)) KB)"
}

# ========== Inno Setup でインストーラー生成 ==========
if (-not $SkipInstaller) {
    $pf86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    Invoke-Step "インストーラー生成" {
        $isccCandidates = @(
            "$pf86\Inno Setup 6\ISCC.exe"
            "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
            "ISCC.exe"
        )
        $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

        if (-not $iscc) {
            Write-Host "⚠️  Inno Setup が見つかりません。インストーラー生成をスキップします。" -ForegroundColor Yellow
            Write-Host "   https://jrsoftware.org/isinfo.php からダウンロードしてください。"
            return
        }

        $issFile = Join-Path $PSScriptRoot "setup.iss"
        & $iscc $issFile "/O$DistDir"
    }
} else {
    Write-Host ""
    Write-Host "⚠️  -SkipInstaller が指定されたためインストーラー生成をスキップします。" -ForegroundColor Yellow
}

# ========== 完了 ==========
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "✅ ビルド完了！" -ForegroundColor Green
Write-Host ""
Write-Host "  publish\launcher\GXW3Boost.Launcher.exe"
Write-Host "  publish\warmer\GXW3Boost.Warmer.exe"
if (-not $SkipInstaller) {
    $setupExe = Get-ChildItem $DistDir -Filter "*.exe" | Select-Object -First 1
    if ($setupExe) {
        Write-Host "  dist\$($setupExe.Name)  ← インストーラー"
    }
}
Write-Host ""
