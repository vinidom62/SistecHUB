# Gera o instalador Velopack e publica no GitHub Releases.
# Pré-requisitos: .NET SDK 8+, vpk 1.0.1 (dotnet tool install -g vpk --version 1.0.1)
#
# Uso:
#   $env:GITHUB_TOKEN = "ghp_..."   # token com permissão repo (só para upload)
#   .\scripts\publish-release.ps1
#
# Opcional: .\scripts\publish-release.ps1 -Version 1.2.0

param(
    [string]$Version = "",
    [string]$GitHubToken = $env:GITHUB_TOKEN,
    [switch]$SkipUpload
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$Csproj = Join-Path $Root "SistecHub.csproj"
$PublishDir = Join-Path $Root "publish"
$ReleasesDir = Join-Path $Root "Releases"
$Icon = Join-Path $Root "Assets\app.ico"

if (-not $Version) {
    $Version = dotnet msbuild $Csproj -getProperty:Version -nologo -v:q
}
if (-not $Version) { throw "Não foi possível ler a versão do projeto." }

Write-Host "Versão: $Version" -ForegroundColor Cyan

Write-Host "Publicando (self-contained win-x64)..." -ForegroundColor Cyan
dotnet publish $Csproj -c Release -r win-x64 --self-contained -o $PublishDir

Write-Host "Empacotando com Velopack..." -ForegroundColor Cyan
$packArgs = @(
    "pack",
    "--packId", "Sistec.SistecHub",
    "--packVersion", $Version,
    "--packTitle", "SistecHub",
    "--packDir", $PublishDir,
    "--mainExe", "SistecHub.exe",
    "--outputDir", $ReleasesDir,
    "--icon", $Icon
)
& vpk @packArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack falhou." }

if ($SkipUpload) {
    Write-Host "Pacotes em: $ReleasesDir (upload ignorado)." -ForegroundColor Green
    exit 0
}

if (-not $GitHubToken) {
    Write-Host "Defina GITHUB_TOKEN para enviar ao GitHub, ou use -SkipUpload." -ForegroundColor Yellow
    Write-Host "Pacotes prontos em: $ReleasesDir" -ForegroundColor Green
    exit 0
}

Write-Host "Enviando para GitHub Releases..." -ForegroundColor Cyan
& vpk upload github `
    --repoUrl "https://github.com/vinidom62/SistecHUB" `
    --token $GitHubToken `
    --outputDir $ReleasesDir `
    --publish
if ($LASTEXITCODE -ne 0) { throw "vpk upload github falhou." }

Write-Host "Release $Version publicada." -ForegroundColor Green
