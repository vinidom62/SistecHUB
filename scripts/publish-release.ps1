# Gera o instalador Velopack e publica no GitHub Releases.
# Instalação para todos os utilizadores: usar o .msi (Program Files\Sistec\SistecHub).
# O app concede escrita na pasta de instalação após o MSI para atualizar sem UAC repetido.
# O Setup.exe continua a instalar só para o utilizador atual (%LocalAppData%).
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

if (Test-Path $ReleasesDir) {
    Write-Host "Limpando pasta Releases (evita misturar versões antigas no upload)..." -ForegroundColor Cyan
    Remove-Item -Path (Join-Path $ReleasesDir "*") -Recurse -Force
}

Write-Host "Empacotando com Velopack..." -ForegroundColor Cyan
$packArgs = @(
    "pack",
    "--packId", "Sistec.SistecHub",
    "--packVersion", $Version,
    "--packTitle", "SistecHub",
    "--packAuthors", "Sistec",
    "--packDir", $PublishDir,
    "--mainExe", "SistecHub.exe",
    "--outputDir", $ReleasesDir,
    "--icon", $Icon,
    "--shortcuts", "Desktop,StartMenuRoot,Startup",
    "--msi",
    "--instLocation", "PerMachine"
)
& vpk @packArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack falhou." }

$ReleasesManifest = Join-Path $ReleasesDir "releases.win.json"
if (-not (Test-Path $ReleasesManifest)) { throw "releases.win.json não foi gerado." }
$manifest = Get-Content $ReleasesManifest -Raw | ConvertFrom-Json
$packedVersions = @($manifest.Assets | ForEach-Object { $_.Version } | Select-Object -Unique)
if ($packedVersions -notcontains $Version) {
    throw @"
O pacote gerado não corresponde à versão $Version.
Versões no releases.win.json: $($packedVersions -join ', ')
Confira Version no .csproj e se a pasta publish está atualizada.
"@
}
Write-Host "Pacote Velopack OK: versão $Version" -ForegroundColor Green

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
