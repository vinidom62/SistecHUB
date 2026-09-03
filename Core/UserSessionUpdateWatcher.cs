using System.Diagnostics;
using System.Text;

namespace SistecHub.Core;

/// <summary>
/// Executa um watcher leve em segundo plano na própria sessão do utilizador logado.
/// Ao contrário do serviço Windows (Sessão 0), este watcher roda com a integridade e ambiente
/// da área de trabalho do utilizador, relançando o SistecHub nativamente e sem risco de bloqueio.
/// </summary>
public static class UserSessionUpdateWatcher
{
    static bool _launched;

    public static void LaunchWatcherProcess(string? expectedVersion)
    {
        if (_launched)
            return;
        _launched = true;

        try
        {
            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                UpdateActivityLog.Warn("Update", "Watcher de sessão não iniciado: executável principal não encontrado.");
                return;
            }

            var currentPid = Environment.ProcessId;
            var settings = AppSettingsStore.Load();
            var startMinimized = settings.IniciarMinimizado;
            var args = startMinimized ? "--autostart" : "";
            var statusFile = UpdateServiceCoordinator.StatusFilePath;

            // Script PowerShell invisível que corre dentro da sessão gráfica do utilizador.
            // 1. Aguarda o término do SistecHub atual (PID antigo).
            // 2. Aguarda a atualização ser aplicada pelo serviço (verifica a versão real do executável no disco e o status).
            // 3. Aguarda o ficheiro executável estar completamente desbloqueado.
            // 4. Inicia o novo SistecHub.exe diretamente na sessão do utilizador (respeitando --autostart se minimizado).
            var psScript = $@"
$ErrorActionPreference = 'SilentlyContinue'
$targetExe = '{exePath.Replace("'", "''")}'
$statusFile = '{statusFile.Replace("'", "''")}'
$pidToWait = {currentPid}
$launchArgs = '{args}'
$expectedVer = '{expectedVersion?.Replace("'", "''") ?? ""}'

# 1. Espera o processo antigo do SistecHub fechar completamente
try {{
    $proc = Get-Process -Id $pidToWait -ErrorAction SilentlyContinue
    if ($proc) {{
        $proc.WaitForExit(45000)
    }}
}} catch {{}}

Start-Sleep -Seconds 2

# 2. Aguarda a atualização ser de facto aplicada
# Não reabre enquanto o executável no disco for a versão antiga!
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {{
    $verMatches = $false
    if ($expectedVer -ne '' -and (Test-Path $targetExe)) {{
        try {{
            $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($targetExe)
            $prodVer = $info.ProductVersion
            $fileVer = $info.FileVersion
            if (($prodVer -and $prodVer -match [regex]::Escape($expectedVer)) -or ($fileVer -and $fileVer -match [regex]::Escape($expectedVer))) {{
                $verMatches = $true
            }}
        }} catch {{}}
    }}

    if (Test-Path $statusFile) {{
        try {{
            $content = Get-Content -Path $statusFile -Raw -ErrorAction SilentlyContinue
            $isCompleted = ($content -match '""phase""\s*:\s*(6|""Completed"")')
            $isError = ($content -match '""phase""\s*:\s*(7|""Error"")')

            # Se a versão no executável já atualizou OU se o serviço gravou Completed para a nova versão
            if ($verMatches -or ($isCompleted -and ($expectedVer -eq '' -or $content -match [regex]::Escape($expectedVer)))) {{
                break
            }}
            if ($isError) {{
                # Em caso de falha irreversível, reabre a versão existente
                break
            }}
        }} catch {{}}
    }} elseif ($verMatches) {{
        break
    }}

    Start-Sleep -Seconds 1
}}

# 3. Aguarda o ficheiro executável estar completamente gravado e desbloqueado
$fileDeadline = (Get-Date).AddSeconds(15)
while ((Get-Date) -lt $fileDeadline) {{
    try {{
        $stream = [System.IO.File]::Open($targetExe, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        $stream.Close()
        $stream.Dispose()
        break
    }} catch {{
        Start-Sleep -Milliseconds 500
    }}
}}

Start-Sleep -Seconds 1

# 4. Inicia o SistecHub na sessão gráfica do utilizador
if ($launchArgs -ne '') {{
    Start-Process -FilePath $targetExe -ArgumentList $launchArgs
}} else {{
    Start-Process -FilePath $targetExe
}}
";

            var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            Process.Start(startInfo);
            UpdateActivityLog.Info("Update", $"Watcher na sessão do utilizador iniciado (PID {currentPid}, versao esperada: '{expectedVersion}', args: '{args}').");
        }
        catch (Exception ex)
        {
            UpdateActivityLog.LogException("Update", ex, "Falha ao iniciar watcher na sessão do utilizador.");
        }
    }
}
