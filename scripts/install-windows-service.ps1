# Regista e inicia o serviço Windows do SistecHub (instalação manual / desenvolvimento).
# A instalação via MSI (.msi Velopack) regista o serviço automaticamente (com UAC via ServiceSetup).
# Requer PowerShell elevado (Administrador).
#
# Uso:
#   .\scripts\install-windows-service.ps1
#   .\scripts\install-windows-service.ps1 -ServiceExePath "C:\Program Files\Sistec\SistecHub\current\SistecHub.Service.exe"

param(
    [string]$ServiceExePath = ""
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    throw "Execute este script como Administrador."
}

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")

if (-not $ServiceExePath) {
    $PublishCandidate = Join-Path $Root "publish\SistecHub.Service.exe"
    if (Test-Path $PublishCandidate) {
        $ServiceExePath = $PublishCandidate
    }
    else {
        throw "Indique -ServiceExePath ou publique o projecto (dotnet publish) antes de instalar."
    }
}

$ServiceExePath = (Resolve-Path $ServiceExePath).Path
if (-not (Test-Path $ServiceExePath)) {
    throw "Executável não encontrado: $ServiceExePath"
}

$SetupExe = Join-Path (Split-Path $ServiceExePath -Parent) "SistecHub.ServiceSetup.exe"
if (-not (Test-Path $SetupExe)) {
    $SetupExe = Join-Path $Root "publish\SistecHub.ServiceSetup.exe"
}

if (-not (Test-Path $SetupExe)) {
    throw "SistecHub.ServiceSetup.exe não encontrado. Publique o projecto SistecHub.ServiceSetup."
}

Write-Host "A instalar serviço via $SetupExe ..." -ForegroundColor Cyan
& $SetupExe install --service-exe $ServiceExePath
if ($LASTEXITCODE -ne 0) { throw "ServiceSetup falhou com código $LASTEXITCODE." }

Get-Service -Name "SistecHubService" -ErrorAction SilentlyContinue | Format-Table Name, Status, StartType -AutoSize
Write-Host "Serviço instalado: $ServiceExePath" -ForegroundColor Green
