# Gera o instalador Velopack e publica no GitHub Releases.
# Instalacao para todos os utilizadores: usar o .msi (Program Files\Sistec\SistecHub).
# O MSI regista automaticamente o servico Windows SistecHubService.
# Actualizacoes automaticas via servico (verificacao ao abrir o app); logs em ProgramData\SistecHub\.
# Pre-requisitos: .NET SDK 8+, vpk 1.0.1 (dotnet tool install -g vpk --version 1.0.1)
#
# Uso:
#   $env:GITHUB_TOKEN = "ghp_..."   # token com permissao repo (so para upload)
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
$ServiceCsproj = Join-Path $Root "SistecHub.Service\SistecHub.Service.csproj"
$ServiceSetupCsproj = Join-Path $Root "SistecHub.ServiceSetup\SistecHub.ServiceSetup.csproj"
$PublishDir = Join-Path $Root "publish"
$ReleasesDir = Join-Path $Root "Releases"
$Icon = Join-Path $Root "Assets\app.ico"

if (-not $Version) {
    $Version = (dotnet msbuild $Csproj -getProperty:Version -nologo -v:q | Out-String).Trim()
}
if (-not $Version) { throw "Nao foi possivel ler a versao do projeto." }

Write-Host "Versao: $Version" -ForegroundColor Cyan

# Limpar publish evita misturar DLLs de builds anteriores.
if (Test-Path $PublishDir) {
    Write-Host "Limpando pasta publish..." -ForegroundColor Cyan
    Remove-Item -Path (Join-Path $PublishDir "*") -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

# Ordem: Setup -> App -> Service (Service por ultimo para garantir deps do LHM).
Write-Host "Publicando utilitario de servico (self-contained win-x64)..." -ForegroundColor Cyan
dotnet publish $ServiceSetupCsproj -c Release -r win-x64 --self-contained -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish ServiceSetup falhou." }

Write-Host "Publicando app (self-contained win-x64)..." -ForegroundColor Cyan
dotnet publish $Csproj -c Release -r win-x64 --self-contained -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish app falhou." }

Write-Host "Publicando servico Windows (self-contained win-x64)..." -ForegroundColor Cyan
dotnet publish $ServiceCsproj -c Release -r win-x64 --self-contained -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish servico falhou." }

$PawnIoSetup = Join-Path $Root "ThirdParty\PawnIO\PawnIO_setup.exe"
if (-not (Test-Path $PawnIoSetup)) {
    Write-Host "A descarregar PawnIO_setup.exe 2.2.0..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path (Split-Path $PawnIoSetup) | Out-Null
    Invoke-WebRequest -Uri "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe" -OutFile $PawnIoSetup -UseBasicParsing
}
Copy-Item -Path $PawnIoSetup -Destination (Join-Path $PublishDir "PawnIO_setup.exe") -Force
Write-Host "PawnIO_setup.exe incluido no pacote." -ForegroundColor Cyan

# Copia explicita: publish incremental deixava DLL 8.0 antiga enquanto deps.json pedia 10.0.
$AccessControlDll = Join-Path $PublishDir "System.Threading.AccessControl.dll"
$AccessControlPkgDll = Join-Path $env:USERPROFILE ".nuget\packages\system.threading.accesscontrol\10.0.3\runtimes\win\lib\net8.0\System.Threading.AccessControl.dll"
if (-not (Test-Path $AccessControlPkgDll)) {
    $AccessControlPkgDll = Join-Path $env:USERPROFILE ".nuget\packages\system.threading.accesscontrol\10.0.3\lib\net8.0\System.Threading.AccessControl.dll"
}
if (-not (Test-Path $AccessControlPkgDll)) {
    throw "Pacote System.Threading.AccessControl 10.0.3 nao encontrado no cache NuGet. Execute dotnet restore."
}
Copy-Item -Path $AccessControlPkgDll -Destination $AccessControlDll -Force

$accessControlVersion = [Reflection.AssemblyName]::GetAssemblyName($AccessControlDll).Version
if ($accessControlVersion.Major -lt 10) {
    throw "System.Threading.AccessControl.dll e $accessControlVersion - precisa ser >= 10.0.0.0."
}
Write-Host "System.Threading.AccessControl.dll OK: $accessControlVersion" -ForegroundColor Cyan

if (Test-Path $ReleasesDir) {
    Write-Host "Limpando pasta Releases (evita misturar versoes antigas no upload)..." -ForegroundColor Cyan
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
    "--shortcuts", "Desktop,StartMenuRoot",
    "--msi",
    "--instLocation", "PerMachine"
)
& vpk @packArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack falhou." }

$ReleasesManifest = Join-Path $ReleasesDir "releases.win.json"
if (-not (Test-Path $ReleasesManifest)) { throw "releases.win.json nao foi gerado." }
$manifest = Get-Content $ReleasesManifest -Raw | ConvertFrom-Json
$packedVersions = @($manifest.Assets | ForEach-Object { $_.Version } | Select-Object -Unique)
if ($packedVersions -notcontains $Version) {
    throw ("O pacote gerado nao corresponde a versao {0}. Versoes no releases.win.json: {1}. Confira Version no .csproj e se a pasta publish esta atualizada." -f $Version, ($packedVersions -join ', '))
}
Write-Host "Pacote Velopack OK: versao $Version" -ForegroundColor Green

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
