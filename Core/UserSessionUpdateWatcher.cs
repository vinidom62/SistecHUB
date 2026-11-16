using System.Diagnostics;
using System.Text;

namespace SistecHub.Core;

/// <summary>
/// Executa um watcher leve em segundo plano na própria sessão do utilizador logado.
/// Ao contrário do serviço Windows (Sessão 0), este watcher roda com a integridade e ambiente
/// da área de trabalho do utilizador, exibindo a tela de progresso durante a substituição dos
/// binários (quando visível) e relançando o SistecHub nativamente sem bloquear o executável.
/// </summary>
public static class UserSessionUpdateWatcher
{
    static bool _launched;

    public static void LaunchWatcherProcess(string? expectedVersion, bool showProgressWindow = false)
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
            var psScript = BuildWatcherScript(exePath, currentPid, expectedVersion, showProgressWindow);
            var launchArgs = showProgressWindow ? "" : "-minimized";

            var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-STA -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            Process.Start(startInfo);
            UpdateActivityLog.Info(
                "Update",
                $"Watcher na sessão do utilizador iniciado (PID {currentPid}, versao esperada: '{expectedVersion}', showUi: {showProgressWindow}, args: '{launchArgs}').");
        }
        catch (Exception ex)
        {
            UpdateActivityLog.LogException("Update", ex, "Falha ao iniciar watcher na sessão do utilizador.");
        }
    }

    public static string BuildWatcherScript(
        string exePath,
        int currentPid,
        string? expectedVersion,
        bool showProgressWindow)
    {
        var launchArgs = showProgressWindow ? "" : "-minimized";
        var statusFile = UpdateServiceCoordinator.StatusFilePath;
        var reopenFile = UpdateServiceCoordinator.ReopenAppRequestFilePath;
        var installReqFile = UpdateServiceCoordinator.InstallRequestFilePath;
        var logFile = UpdateActivityLog.LogFilePath;
        var showUiLiteral = showProgressWindow ? "$true" : "$false";
        var verDisplay = string.IsNullOrWhiteSpace(expectedVersion)
            ? "nova versão"
            : (expectedVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? expectedVersion : $"v{expectedVersion}");

        // Script PowerShell STA que corre dentro da sessão gráfica do utilizador.
        // 1. Se showProgressWindow=true, abre imediatamente uma janela WPF flutuante moderna informando a aplicação dos ficheiros.
        // 2. Aguarda o término do SistecHub atual (PID antigo).
        // 3. Aguarda a atualização ser aplicada pelo Update.exe lendo sq.version e update-status.json (SEM abrir lock em SistecHub.exe).
        // 4. Verifica se Update.exe já encerrou completamente antes de tocar no executável.
        // 5. Aguarda até 240s; se expirar sem atualizar, regista log detalhado e evita loop de reabertura.
        // 6. Inicia o novo SistecHub.exe na sessão do utilizador.
        return $@"
$ErrorActionPreference = 'SilentlyContinue'
$targetExe = '{exePath.Replace("'", "''")}'
$appDir = [System.IO.Path]::GetDirectoryName($targetExe)
$sqVersionFile = [System.IO.Path]::Combine($appDir, 'sq.version')
$statusFile = '{statusFile.Replace("'", "''")}'
$reopenFile = '{reopenFile.Replace("'", "''")}'
$installReqFile = '{installReqFile.Replace("'", "''")}'
$logFile = '{logFile.Replace("'", "''")}'
$pidToWait = {currentPid}
$launchArgs = '{launchArgs}'
$expectedVer = '{expectedVersion?.Replace("'", "''") ?? ""}'
$verDisplay = '{verDisplay.Replace("'", "''")}'
$showUi = {showUiLiteral}
$script:completedLaunch = $false
$script:allowWindowClose = $false

function Write-WatcherLog([string]$level, [string]$msg) {{
    try {{
        $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff')
        $line = ""[$ts] [$level] [Watcher] $msg"" + [Environment]::NewLine
        [System.IO.File]::AppendAllText($logFile, $line, [System.Text.Encoding]::UTF8)
    }} catch {{}}
}}

function Test-UpdateCompleted {{
    # Nunca concluir enquanto o Update.exe do Velopack ainda estiver a substituir ficheiros
    $updProc = Get-Process -Name 'Update' -ErrorAction SilentlyContinue
    if ($updProc) {{
        return 'Running'
    }}

    # Aguarda o serviço consumir o pedido de instalação antes de avaliar o status
    if (Test-Path $installReqFile) {{
        return 'Waiting'
    }}

    $verMatches = $false
    if ($expectedVer -ne '' -and (Test-Path $sqVersionFile)) {{
        try {{
            $sqContent = [System.IO.File]::ReadAllText($sqVersionFile)
            if ($sqContent -and $sqContent -match ('<version>\s*' + [regex]::Escape($expectedVer) + '\s*</version>')) {{
                $verMatches = $true
            }}
        }} catch {{}}
    }}

    if (Test-Path $statusFile) {{
        try {{
            $content = [System.IO.File]::ReadAllText($statusFile)
            $isCompleted = ($content -match '""phase""\s*:\s*(6|""Completed"")')
            $isError = ($content -match '""phase""\s*:\s*(7|""Error"")')

            if ($verMatches -or ($isCompleted -and ($expectedVer -eq '' -or $content -match [regex]::Escape($expectedVer)))) {{
                return 'Completed'
            }}
            if ($isError) {{
                return 'Error'
            }}
        }} catch {{}}
    }} elseif ($verMatches) {{
        return 'Completed'
    }}

    return 'Waiting'
}}

function Complete-And-Launch([string]$finalState) {{
    if ($script:completedLaunch) {{ return }}
    $script:completedLaunch = $true

    if ($finalState -eq 'Timeout') {{
        Write-WatcherLog 'ERR' ""Timeout de 240s atingido sem confirmação de aplicação da versão '$expectedVer'. Reabertura cancelada para impedir loop.""
        try {{
            if (Test-Path $reopenFile) {{ Remove-Item -Path $reopenFile -Force -ErrorAction SilentlyContinue }}
        }} catch {{}}
        return
    }}

    # Garante que qualquer instância residual do Update.exe terminou
    $updWait = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $updWait) {{
        $updProc = Get-Process -Name 'Update' -ErrorAction SilentlyContinue
        if (-not $updProc) {{ break }}
        Start-Sleep -Milliseconds 500
    }}

    # Aguarda o ficheiro executável existir e estar completamente desbloqueado
    $fileDeadline = (Get-Date).AddSeconds(20)
    $fileReady = $false
    while ((Get-Date) -lt $fileDeadline) {{
        if (Test-Path $targetExe) {{
            try {{
                $stream = [System.IO.File]::Open($targetExe, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
                $stream.Close()
                $stream.Dispose()
                $fileReady = $true
                break
            }} catch {{}}
        }}
        Start-Sleep -Milliseconds 500
    }}

    if (-not $fileReady) {{
        Write-WatcherLog 'ERR' ""Executável '$targetExe' indisponível ou bloqueado após atualização.""
        return
    }}

    Start-Sleep -Milliseconds 600
    Write-WatcherLog 'INF' ""A relançar SistecHub ($finalState) com argumentos '$launchArgs'.""
    if ($launchArgs -ne '') {{
        Start-Process -FilePath $targetExe -ArgumentList $launchArgs
    }} else {{
        Start-Process -FilePath $targetExe
    }}
}}

if ($showUi) {{
    try {{
        Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
        [xml]$xaml = @""
<Window xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        Title=""Atualizando o SistecHub""
        Width=""480"" Height=""215""
        WindowStartupLocation=""CenterScreen""
        WindowStyle=""None""
        AllowsTransparency=""True""
        Background=""Transparent""
        ResizeMode=""NoResize""
        Topmost=""True""
        ShowInTaskbar=""True"">
    <Border CornerRadius=""14"" Background=""#FFFFFF"" BorderBrush=""#CBD5E1"" BorderThickness=""1"" Margin=""10"">
        <Border.Effect>
            <DropShadowEffect Color=""#0F172A"" BlurRadius=""18"" ShadowDepth=""4"" Direction=""270"" Opacity=""0.18""/>
        </Border.Effect>
        <Grid Margin=""28,24"">
            <Grid.RowDefinitions>
                <RowDefinition Height=""Auto""/>
                <RowDefinition Height=""Auto""/>
                <RowDefinition Height=""Auto""/>
                <RowDefinition Height=""*""/>
            </Grid.RowDefinitions>
            <StackPanel Grid.Row=""0"" Orientation=""Horizontal"" Margin=""0,0,0,10"">
                <Border Width=""32"" Height=""32"" CornerRadius=""8"" Background=""#EEF2FF"" Margin=""0,0,12,0"" VerticalAlignment=""Center"">
                    <TextBlock Text=""&#xE895;"" FontFamily=""Segoe MDL2 Assets"" FontSize=""16"" Foreground=""#4F46E5"" HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                </Border>
                <TextBlock Text=""Atualizando o SistecHub"" FontSize=""17"" FontWeight=""Bold"" Foreground=""#0F172A"" VerticalAlignment=""Center""/>
            </StackPanel>
            <TextBlock x:Name=""TxtMessage"" Grid.Row=""1""
                       Text=""Aplicando arquivos da nova versão... Por favor, aguarde alguns instantes.""
                       FontSize=""13"" Foreground=""#475569"" TextWrapping=""Wrap"" Margin=""0,0,0,18""/>
            <ProgressBar Grid.Row=""2"" Height=""8"" IsIndeterminate=""True"" Foreground=""#6366F1"" Background=""#E2E8F0"" BorderThickness=""0""/>
            <TextBlock x:Name=""TxtSub"" Grid.Row=""3""
                       Text=""O aplicativo será reaberto automaticamente assim que a instalação terminar.""
                       FontSize=""11.5"" Foreground=""#64748B"" VerticalAlignment=""Bottom"" Margin=""0,12,0,0""/>
        </Grid>
    </Border>
</Window>
""@
        $reader = New-Object System.Xml.XmlNodeReader($xaml)
        $window = [System.Windows.Markup.XamlReader]::Load($reader)
        try {{
            $txtMsg = $window.FindName('TxtMessage')
            if ($txtMsg) {{
                $txtMsg.Text = ""Aplicando arquivos da versão $verDisplay... Por favor, aguarde alguns instantes.""
            }}
        }} catch {{}}

        $window.Add_Closing({{
            param($s, $e)
            if (-not $script:allowWindowClose) {{
                $e.Cancel = $true
            }}
        }})

        $script:startTime = Get-Date
        $script:deadline = $script:startTime.AddSeconds(240)
        $script:oldExited = $false
        $script:finalState = 'Timeout'

        $timer = New-Object System.Windows.Threading.DispatcherTimer
        $timer.Interval = [TimeSpan]::FromMilliseconds(800)
        $timer.Add_Tick({{
            if (-not $script:oldExited) {{
                $oldProc = Get-Process -Id $pidToWait -ErrorAction SilentlyContinue
                if (-not $oldProc -or ((Get-Date) -gt $script:startTime.AddSeconds(45))) {{
                    $script:oldExited = $true
                    $script:readyCheckStart = Get-Date
                }}
                return
            }}

            # Aguarda pelo menos 2s após o processo antigo fechar
            if (((Get-Date) - $script:readyCheckStart).TotalSeconds -lt 2) {{
                return
            }}

            if ((Get-Date) -ge $script:deadline) {{
                $script:finalState = 'Timeout'
                $timer.Stop()
                Complete-And-Launch 'Timeout'
                $script:allowWindowClose = $true
                $window.Close()
                return
            }}

            $state = Test-UpdateCompleted
            if ($state -eq 'Completed' -or $state -eq 'Error') {{
                $script:finalState = $state
                $timer.Stop()
                try {{
                    $txtSub = $window.FindName('TxtSub')
                    if ($txtSub) {{ $txtSub.Text = 'Iniciando a nova versão do SistecHub...' }}
                }} catch {{}}
                Complete-And-Launch $state
                $script:allowWindowClose = $true
                $window.Close()
            }}
        }})
        $timer.Start()
        [void]$window.ShowDialog()
        $timer.Stop()
    }} catch {{}}
}}

if (-not $script:completedLaunch) {{
    try {{
        $proc = Get-Process -Id $pidToWait -ErrorAction SilentlyContinue
        if ($proc) {{
            $proc.WaitForExit(45000)
        }}
    }} catch {{}}

    Start-Sleep -Seconds 2

    $deadline = (Get-Date).AddSeconds(240)
    $finalState = 'Timeout'
    while ((Get-Date) -lt $deadline) {{
        $state = Test-UpdateCompleted
        if ($state -eq 'Completed' -or $state -eq 'Error') {{
            $finalState = $state
            break
        }}
        Start-Sleep -Seconds 1
    }}

    Complete-And-Launch $finalState
}}
";
    }
}
