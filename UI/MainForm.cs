using System.Runtime.InteropServices;
using System.Windows.Forms.Integration;
using SistecHub.Core;
using SistecHub.Modulos.AcessoRemoto;
using SistecHub.Modulos.GLPI;
using SistecHub.Modulos.GLPI.Chamados;
using SistecHub.Modulos.Inventario;

namespace SistecHub.UI;

internal sealed class MainForm : Form
{
    const int WM_EXITSIZEMOVE = 0x0232;
    const int WM_HOTKEY = 0x0312;
    const int HOTKEY_ID_ACESSO_REMOTO = 0x5157;
    const uint MOD_ALT = 0x0001;
    const uint MOD_CONTROL = 0x0002;
    const uint MOD_NOREPEAT = 0x4000;
    const uint VK_SPACE = 0x20;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    readonly Panel _contentHost;
    readonly Panel _sidebar;
    readonly SidebarControl _sidebarControl;
    readonly IReadOnlyDictionary<string, IAppModule> _modulesById;
    readonly NotifyIcon _trayIcon;

    bool _exitRequested;
    bool _machineRegistrationStarted;
    bool _startupServicesStarted;
    readonly bool _startMinimized;
    bool _allowShow;
    string _activePageId = "home";

    Panel _remoteSessionBanner = null!;
    Label _remoteBannerText = null!;
    Button _remoteBannerChatBtn = null!;
    Button _remoteBannerDisconnectBtn = null!;
    System.Windows.Forms.Timer _remoteBannerDurationTimer = null!;
    DateTimeOffset? _remoteBannerStartTime;

    public MainForm(bool startMinimized = false)
    {
        _startMinimized = startMinimized;
        _allowShow = !startMinimized;

        _modulesById = ModuleLoader.DiscoverModules()
            .ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        AppDebugLog.Info("App", $"Módulos carregados: {string.Join(", ", _modulesById.Keys)}");

        Text = "SistecHub";
        ClientSize = new Size(1280, 720);
        MinimumSize = new Size(1020, 620);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        BackColor = ShellTheme.MainBg;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon != null)
            Icon = appIcon;

        _trayIcon = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = "SistecHub",
            Visible = true,
        };
        var trayMenu = new ContextMenuStrip();
        var trayClose = new ToolStripMenuItem("Fechar");
        trayClose.Click += (_, _) => RequestExit();
        trayMenu.Items.Add(trayClose);
        _trayIcon.ContextMenuStrip = trayMenu;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        AppUpdateService.UpdateRestartRequested += OnUpdateRestartRequested;

        FormClosing += OnFormClosing;

        Win32Dwm.TryEnableRoundedCorners(this);

        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
            };
        }

        AppThemeManager.Initialize();
        var isDark = AppThemeManager.IsDarkMode;
        var sidebarBg = isDark ? Color.FromArgb(17, 17, 19) : ShellTheme.SidebarBg;

        _sidebarControl = new SidebarControl();
        _sidebarControl.SetMenuEntries(BuildMenuEntries());
        _sidebarControl.SetDarkMode(isDark);
        _sidebarControl.PageSelected += pageId =>
        {
            if (InvokeRequired)
                BeginInvoke(() => ShowPage(pageId));
            else
                ShowPage(pageId);
        };

        var sidebarElementHost = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = sidebarBg,
            Child = _sidebarControl,
        };

        _sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 236,
            BackColor = sidebarBg,
        };
        _sidebar.Controls.Add(sidebarElementHost);

        ChamadosDataCache.SnapshotUpdated += OnChamadosSnapshotUpdated;

        _contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
        };

        _remoteSessionBanner = CreateRemoteSessionBanner();

        AcessoRemotoAgentService.SessionStarted += OnRemoteSessionStarted;
        AcessoRemotoAgentService.SessionEnded += OnRemoteSessionEnded;
        AcessoRemotoAgentService.StateChanged += OnRemoteAgentStateChanged;

        FormClosed += (_, _) =>
        {
            AcessoRemotoAgentService.SessionStarted -= OnRemoteSessionStarted;
            AcessoRemotoAgentService.SessionEnded -= OnRemoteSessionEnded;
            AcessoRemotoAgentService.StateChanged -= OnRemoteAgentStateChanged;
            AcessoRemotoChatForm.CloseIfOpen();
            ChamadosDataCache.SnapshotUpdated -= OnChamadosSnapshotUpdated;
            _remoteBannerDurationTimer.Dispose();
            _trayIcon.Dispose();
            sidebarElementHost.Dispose();
        };

        var mainColumn = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
        };
        mainColumn.Controls.Add(_contentHost);
        mainColumn.Controls.Add(_remoteSessionBanner);

        Controls.Add(mainColumn);
        Controls.Add(_sidebar);

        Shown += (_, _) =>
        {
            EnsureBackgroundServicesStarted();
        };
        Load += OnMainFormLoad;
    }

    void EnsureBackgroundServicesStarted()
    {
        if (_startupServicesStarted)
            return;
        _startupServicesStarted = true;

        AppUpdateService.BeginAutomaticUpdateMonitoring(this);
        RefreshFooterEntityName();
        _ = PrefetchFooterEntityNameAsync();
        InventarioSnapshotCoordinator.Start();
        AcessoRemotoAgentService.Start();
        _ = EnsureMachineRegisteredOnStartupAsync();
    }

    async Task EnsureMachineRegisteredOnStartupAsync()
    {
        if (_machineRegistrationStarted || IsDisposed)
            return;
        _machineRegistrationStarted = true;

        var settings = AppSettingsStore.Load();
        if (InventarioMachineRegistration.HasMachineId(settings))
            return;

        // Registo + coleta + upload correm no SistecHub.Service (elevado).
        InventarioServiceCoordinator.RequestRefresh();
        InventarioAutoUploadCoordinator.Start();

        try
        {
            for (var i = 0; i < 45 && !IsDisposed; i++)
            {
                var createdId = InventarioSnapshotCoordinator.TryConsumeNewlyRegisteredMachineId();
                if (createdId is int id)
                {
                    MessageBox.Show(
                        this,
                        $"Máquina inventáriada, ID: {id}",
                        "Inventário",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                // Se após 12 segundos o serviço ainda não registou, tenta o registo direto como fallback
                if (i == 6)
                {
                    settings = AppSettingsStore.Load();
                    if (!InventarioMachineRegistration.HasMachineId(settings)
                        && AppSettingsStore.IsInitialSetupComplete(settings))
                    {
                        try
                        {
                            var fallbackId = await InventarioMachineRegistration.EnsureRegisteredAsync()
                                .ConfigureAwait(true);
                            if (fallbackId is int fid)
                            {
                                InventarioServiceCoordinator.RequestUpload();
                                MessageBox.Show(
                                    this,
                                    $"Máquina inventáriada, ID: {fid}",
                                    "Inventário",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);
                                return;
                            }
                        }
                        catch (Exception fallbackEx)
                        {
                            AppDebugLog.LogException("Inventario", fallbackEx, "Registo de máquina no startup (fallback)");
                        }
                    }
                }

                await Task.Delay(2000).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                global::SistecHub.UserFacingErrorHelper.ShowErrorFromException(
                    this, ex, "Inventário");
            }
        }
    }

    void OnMainFormLoad(object? sender, EventArgs e)
    {
        ShowPage("home");
        EnsureBackgroundServicesStarted();

        if (_startMinimized)
        {
            _ = Task.Delay(2000).ContinueWith(_ =>
            {
                if (!IsDisposed)
                    MemoryOptimizer.TrimWorkingSet();
            });
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        if (_startMinimized && !_allowShow)
        {
            value = false;
            if (!IsHandleCreated)
                CreateHandle();
        }
        base.SetVisibleCore(value);
    }

    void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exitRequested)
            return;
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            MemoryOptimizer.TrimWorkingSet();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            MemoryOptimizer.TrimWorkingSet();
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _sidebar?.PerformLayout();
        _contentHost?.PerformLayout();
        _contentHost?.Invalidate(true);
        RefreshHostedWpfControls(this);
    }

    static void RefreshHostedWpfControls(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            if (c is ElementHost host)
            {
                host.Invalidate();
                host.Update();
                host.Child?.InvalidateMeasure();
                host.Child?.InvalidateVisual();
            }
            if (c.HasChildren)
            {
                RefreshHostedWpfControls(c);
            }
        }
    }

    void ShowFromTray()
    {
        _allowShow = true;
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Handle != IntPtr.Zero)
        {
            // Registra o atalho global Ctrl + Alt + Espaço para abrir o SistecHub na tela de Acesso Remoto
            if (!RegisterHotKey(Handle, HOTKEY_ID_ACESSO_REMOTO, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_SPACE))
            {
                RegisterHotKey(Handle, HOTKEY_ID_ACESSO_REMOTO, MOD_CONTROL | MOD_ALT, VK_SPACE);
            }
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (Handle != IntPtr.Zero)
        {
            try { UnregisterHotKey(Handle, HOTKEY_ID_ACESSO_REMOTO); } catch { }
        }
        base.OnHandleDestroyed(e);
    }

    void OpenAcessoRemotoFromGlobalHotkey()
    {
        ShowFromTray();
        if (Handle != IntPtr.Zero)
        {
            try { SetForegroundWindow(Handle); } catch { }
        }
        ShowPage("acesso-remoto");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID_ACESSO_REMOTO)
        {
            OpenAcessoRemotoFromGlobalHotkey();
            m.Result = IntPtr.Zero;
            return;
        }

        if (m.Msg == (int)SingleInstanceApp.InstanceActivateMessage)
        {
            ShowFromTray();
            m.Result = IntPtr.Zero;
            return;
        }

        if (m.Msg == WM_EXITSIZEMOVE)
        {
            base.WndProc(ref m);
            _sidebar?.PerformLayout();
            _contentHost?.PerformLayout();
            Invalidate(true);
            RefreshHostedWpfControls(this);
            return;
        }

        base.WndProc(ref m);
    }

    void OnUpdateRestartRequested()
    {
        if (InvokeRequired)
        {
            BeginInvoke(OnUpdateRestartRequested);
            return;
        }

        RequestExit();
    }

    void RequestExit()
    {
        AppUpdateService.SignalApplyOnExit();

        InventarioAutoUploadCoordinator.Stop();
        InventarioSnapshotCoordinator.Stop();
        AcessoRemotoAgentService.Stop();
        AcessoRemotoChatForm.CloseIfOpen();

        _exitRequested = true;
        try
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        catch { }

        try
        {
            Close();
            Application.Exit();
        }
        catch { }

        try
        {
            SingleInstanceApp.ReleaseFirstInstance();
        }
        catch { }

        Environment.Exit(0);
    }

    Panel CreateRemoteSessionBanner()
    {
        var banner = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = Color.FromArgb(15, 23, 42), // Slate 900
            Padding = new Padding(20, 0, 20, 0),
            Visible = false,
        };

        banner.Paint += (_, e) =>
        {
            using var borderPen = new Pen(Color.FromArgb(30, 41, 59));
            e.Graphics.DrawLine(borderPen, 0, banner.Height - 1, banner.Width, banner.Height - 1);
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var leftFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };

        var dot = new Label
        {
            Text = "●",
            AutoSize = true,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(16, 185, 129), // Emerald 500
            Margin = new Padding(0, 11, 8, 0),
        };

        _remoteBannerText = new Label
        {
            Text = "Acesso remoto ativo com o técnico",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(241, 245, 249),
            Margin = new Padding(0, 13, 0, 0),
        };

        leftFlow.Controls.Add(dot);
        leftFlow.Controls.Add(_remoteBannerText);

        var rightFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 0),
        };

        _remoteBannerDisconnectBtn = new Button
        {
            Text = "Encerrar Acesso",
            AutoSize = true,
            Height = 32,
            Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(8, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(220, 38, 38), // Red 600
            Cursor = Cursors.Hand,
        };
        _remoteBannerDisconnectBtn.FlatAppearance.BorderSize = 0;
        _remoteBannerDisconnectBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(185, 28, 28);
        _remoteBannerDisconnectBtn.FlatAppearance.MouseDownBackColor = Color.FromArgb(153, 27, 27);
        _remoteBannerDisconnectBtn.Click += (_, _) =>
        {
            var res = MessageBox.Show(
                this,
                "Deseja realmente encerrar a sessão de acesso remoto com o técnico?",
                "Encerrar Acesso Remoto",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (res == DialogResult.Yes)
            {
                AcessoRemotoAgentService.DisconnectActiveSession();
            }
        };

        _remoteBannerChatBtn = new Button
        {
            Text = "Abrir Chat",
            AutoSize = true,
            Height = 32,
            Padding = new Padding(12, 0, 12, 0),
            Margin = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(226, 232, 240),
            BackColor = Color.FromArgb(30, 41, 59), // Slate 800
            Cursor = Cursors.Hand,
        };
        _remoteBannerChatBtn.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
        _remoteBannerChatBtn.FlatAppearance.BorderSize = 1;
        _remoteBannerChatBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(51, 65, 85);
        _remoteBannerChatBtn.Click += (_, _) => AcessoRemotoChatForm.ShowOrActivateChat();

        rightFlow.Controls.Add(_remoteBannerDisconnectBtn);
        rightFlow.Controls.Add(_remoteBannerChatBtn);

        table.Controls.Add(leftFlow, 0, 0);
        table.Controls.Add(rightFlow, 1, 0);

        banner.Controls.Add(table);

        _remoteBannerDurationTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _remoteBannerDurationTimer.Tick += (_, _) => UpdateRemoteBannerText();

        return banner;
    }

    void UpdateRemoteBannerText()
    {
        if (_remoteBannerStartTime is null)
            return;

        var elapsed = DateTimeOffset.Now - _remoteBannerStartTime.Value;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var snap = AcessoRemotoAgentService.GetSnapshot();
        var tech = !string.IsNullOrWhiteSpace(snap.ConnectedTechnicianName)
            ? snap.ConnectedTechnicianName
            : "Técnico de Suporte";
        _remoteBannerText.Text = $"Acesso remoto ativo com {tech} • Duração: {elapsed:hh\\:mm\\:ss}";
    }

    void SyncRemoteBannerWithService()
    {
        if (InvokeRequired)
        {
            BeginInvoke(SyncRemoteBannerWithService);
            return;
        }

        var snap = AcessoRemotoAgentService.GetSnapshot();
        if (snap.IsSessionActive)
        {
            _remoteBannerStartTime = snap.SessionStartTime ?? DateTimeOffset.Now;
            UpdateRemoteBannerText();
            _remoteSessionBanner.Visible = true;
            _remoteBannerDurationTimer.Start();
        }
        else
        {
            _remoteBannerDurationTimer.Stop();
            _remoteSessionBanner.Visible = false;
            _remoteBannerStartTime = null;
        }
    }

    void OnRemoteAgentStateChanged(object? sender, EventArgs e)
    {
        SyncRemoteBannerWithService();
    }

    void OnRemoteSessionStarted(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnRemoteSessionStarted(sender, e));
            return;
        }

        SyncRemoteBannerWithService();
        WindowState = FormWindowState.Minimized;
        AcessoRemotoChatForm.ShowOrActivateChat();
    }

    void OnRemoteSessionEnded(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnRemoteSessionEnded(sender, e));
            return;
        }

        SyncRemoteBannerWithService();
        AcessoRemotoChatForm.CloseIfOpen();
        ShowFromTray();
    }

    IEnumerable<(string Id, string MenuText)> BuildMenuEntries()
    {
        yield return ("home", "Início");
        foreach (var m in _modulesById.Values.OrderBy(m => m.MenuText, StringComparer.CurrentCultureIgnoreCase))
            yield return (m.Id, m.MenuText);
    }

    void ShowPage(string id)
    {
        if (string.Equals(id, "settings", StringComparison.OrdinalIgnoreCase)
            && !TryAuthorizeSettingsAccess())
        {
            return;
        }

        var navId = id.StartsWith("glpi", StringComparison.OrdinalIgnoreCase) ? "glpi" : id;
        UpdateFooterSelection(navId);
        if (string.Equals(_activePageId, id, StringComparison.OrdinalIgnoreCase) && _contentHost.Controls.Count > 0)
        {
            return;
        }

        _activePageId = id;
        AppDebugLog.Debug("UI", $"Página activa: {id}");

        _contentHost.SuspendLayout();
        try
        {
            while (_contentHost.Controls.Count > 0)
            {
                var c = _contentHost.Controls[0];
                _contentHost.Controls.RemoveAt(0);
                c.Dispose();
            }

            var next = CreatePage(id);
            next.Dock = DockStyle.Fill;
            _contentHost.Controls.Add(next);
        }
        finally
        {
            _contentHost.ResumeLayout(true);
        }
    }

    void UpdateFooterSelection(string id)
    {
        _sidebarControl.SetActivePage(id);
    }

    void OnChamadosSnapshotUpdated() => RefreshFooterEntityName();

    void RefreshFooterEntityName()
    {
        if (ChamadosDataCache.TryGetEntityDisplayName(out var name))
            _sidebarControl.EntityDisplayText = name;
    }

    async Task PrefetchFooterEntityNameAsync()
    {
        if (ChamadosDataCache.TryGetEntityDisplayName(out _))
            return;

        var settings = AppSettingsStore.Load();
        if (!int.TryParse(settings.EntityId?.Trim(), out var entityId) || entityId < 1)
            return;

        if (ChamadosDataCache.TryGetForEntity(entityId, out var cached) && cached != null)
        {
            RefreshFooterEntityName();
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var entity = await GlpiApiClient.GetEntityAsync(settings, entityId, cts.Token).ConfigureAwait(true);
            var name = entity.LeafDisplayName;
            if (string.IsNullOrWhiteSpace(name))
                name = entity.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = "Entidade #" + entityId;

            _sidebarControl.EntityDisplayText = name;
        }
        catch
        {
            // Mantém o rodapé vazio se a consulta falhar; o cache será preenchido ao abrir Chamados.
        }
    }

    UserControl CreatePage(string id)
    {
        if (string.Equals(id, "home", StringComparison.OrdinalIgnoreCase))
        {
            var home = new HomeView();
            home.NavigateRequested += pageId => ShowPage(pageId);
            home.DarkModeChanged += ApplyGlobalDarkMode;
            return home;
        }

        if (string.Equals(id, "settings", StringComparison.OrdinalIgnoreCase))
            return new SettingsView();

        if (string.Equals(id, "client", StringComparison.OrdinalIgnoreCase))
            return new PlaceholderView("Cliente", "Informações da conta ou entidade serão apresentadas aqui.");

        if (string.Equals(id, "glpi:novo", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "abrir-chamado", StringComparison.OrdinalIgnoreCase))
            return new Modulos.GLPI.GLPIView(openAbertura: true);

        if (_modulesById.TryGetValue(id, out var module))
            return module.CreateContentView();

        return new PlaceholderView("SistecHub", "Página não encontrada.");
    }

    void ApplyGlobalDarkMode(bool isDark)
    {
        var mainBg = ShellTheme.MainBg;
        BackColor = mainBg;
        _contentHost.BackColor = mainBg;

        var sidebarBg = isDark ? Color.FromArgb(17, 17, 19) : ShellTheme.SidebarBg;
        _sidebar.BackColor = sidebarBg;
        if (_sidebar.Controls.Count > 0)
            _sidebar.Controls[0].BackColor = sidebarBg;
        _sidebarControl.SetDarkMode(isDark);

        if (IsHandleCreated)
            Win32Dwm.ApplyTitleBarTheme(Handle, isDark);
    }

    bool TryAuthorizeSettingsAccess()
    {
        using var prompt = new SettingsPasswordForm();
        if (prompt.ShowDialog(this) == DialogResult.OK)
            return true;

        UpdateFooterSelection(_activePageId);
        return false;
    }
}
