# Remove o serviço Windows do SistecHub.
# Requer PowerShell elevado (Administrador).
#
# Uso:
#   .\scripts\uninstall-windows-service.ps1

$ErrorActionPreference = "Stop"

$ServiceName = "SistecHubService"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    throw "Execute este script como Administrador."
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Serviço $ServiceName não está instalado." -ForegroundColor Yellow
    exit 0
}

Write-Host "A parar serviço $ServiceName..." -ForegroundColor Cyan
if ($existing.Status -ne "Stopped") {
    Stop-Service -Name $ServiceName -Force
}

Write-Host "A remover serviço..." -ForegroundColor Cyan
sc.exe delete $ServiceName | Out-Null

Write-Host "Serviço removido." -ForegroundColor Green
