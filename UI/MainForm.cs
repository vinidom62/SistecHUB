using SistecHub.Core;
using SistecHub.Modulos.GLPI;
using SistecHub.Modulos.GLPI.Chamados;
using SistecHub.Modulos.Inventario;

namespace SistecHub.UI;

internal sealed class MainForm : Form
{
    const int PageTransitionDurationMs = 280;

    readonly DoubleBufferedPanel _contentHost;
    readonly System.Windows.Forms.Timer _pageTransitionTimer;
    readonly Panel _sidebar;
    readonly SidebarFooterEntityBadge _footerEntityBadge;
    readonly SidebarFooterNavItem _footerSettings;
    readonly FlowLayoutPanel _footerFlow;
    readonly IReadOnlyDictionary<string, IAppModule> _modulesById;
    readonly NotifyIcon _trayIcon;

    bool _exitRequested;
    bool _machineRegistrationStarted;
    string _activePageId = "home";
    UserControl? _transitionOldPage;
    UserControl? _transitionNewPage;
    long _transitionStartTick;

    public MainForm()
    {
        _modulesById = ModuleLoader.DiscoverModules()
            .ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        AppDebugLog.Info("App", $"Módulos carregados: {string.Join(", ", _modulesById.Keys)}");

        Text = "SistecHub";
        ClientSize = new Size(1280, 720);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
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

        Shown += (_, _) => AppUpdateService.BeginAutomaticUpdateMonitoring(this);

        FormClosing += OnFormClosing;

        Win32Dwm.TryEnableRoundedCorners(this);

        _sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 232,
            BackColor = ShellTheme.SidebarBg,
        };

        var sidebarEdge = new Panel
        {
            Dock = DockStyle.Right,
            Width = 1,
            BackColor = ShellTheme.SidebarDivider,
        };

        var sidebarHeader = new Label
        {
            Text = "SistecHub",
            Dock = DockStyle.Top,
            Height = 64,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(20, 0, 0, 0),
            BackColor = ShellTheme.SidebarHeaderBg,
        };

        var navCaption = new Label
        {
            Text = "NAVEGAÇÃO",
            Dock = DockStyle.Top,
            Height = 28,
            ForeColor = ShellTheme.TextMuted,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(20, 12, 0, 0),
            BackColor = ShellTheme.SidebarBg,
        };

        var menuFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = ShellTheme.SidebarBg,
            Padding = new Padding(12, 8, 12, 16),
        };

        void SyncMenuButtonWidths()
        {
            var w = Math.Max(0, menuFlow.ClientSize.Width - menuFlow.Padding.Horizontal);
            foreach (Control c in menuFlow.Controls)
            {
                c.Width = w;
                if (c is Button b)
                    ShellTheme.ApplyRoundedRegion(b, 10);
            }
        }

        menuFlow.Resize += (_, _) => SyncMenuButtonWidths();

        foreach (var entry in BuildMenuEntries())
        {
            var btn = ShellTheme.CreateSidebarMenuButton(entry.MenuText);
            btn.Tag = entry.Id;
            btn.Click += (_, _) => ShowPage(entry.Id);
            menuFlow.Controls.Add(btn);
        }

        _footerEntityBadge = new SidebarFooterEntityBadge("\uE77B");
        _footerSettings = new SidebarFooterNavItem("\uE713", "Configurações");

        _footerFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            BackColor = Color.Transparent,
            Padding = new Padding(8, 10, 8, 12),
        };

        _footerFlow.Controls.Add(_footerEntityBadge);
        _footerFlow.Controls.Add(_footerSettings);

        var sidebarFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 118,
            BackColor = ShellTheme.SidebarBg,
            Padding = new Padding(4, 0, 4, 0),
        };

        void SyncFooterItemWidths()
        {
            var w = Math.Max(0, _footerFlow.ClientSize.Width - _footerFlow.Padding.Horizontal);
            _footerEntityBadge.Width = w;
            _footerSettings.Width = w;
        }

        _footerFlow.Resize += (_, _) => SyncFooterItemWidths();

        sidebarFooter.Paint += (_, e) =>
        {
            using var pen = new Pen(ShellTheme.SidebarDivider);
            e.Graphics.DrawLine(pen, 12, 0, Math.Max(12, sidebarFooter.Width - 12), 0);
        };

        sidebarFooter.Resize += (_, _) => SyncFooterItemWidths();
        sidebarFooter.Controls.Add(_footerFlow);

        _footerSettings.Click += (_, _) => ShowPage("settings");

        ChamadosDataCache.SnapshotUpdated += OnChamadosSnapshotUpdated;

        _sidebar.Controls.Add(menuFlow);
        _sidebar.Controls.Add(sidebarFooter);
        _sidebar.Controls.Add(navCaption);
        _sidebar.Controls.Add(sidebarHeader);
        _sidebar.Controls.Add(sidebarEdge);

        _contentHost = new DoubleBufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
        };

        _pageTransitionTimer = new System.Windows.Forms.Timer { Interval = 2 };
        _pageTransitionTimer.Tick += OnPageTransitionTick;

        FormClosed += (_, _) =>
        {
            ChamadosDataCache.SnapshotUpdated -= OnChamadosSnapshotUpdated;
            _pageTransitionTimer.Dispose();
            _trayIcon.Dispose();
        };

        _contentHost.Resize += OnContentHostResizeDuringTransition;

        var mainColumn = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
        };
        mainColumn.Controls.Add(_contentHost);

        Controls.Add(mainColumn);
        Controls.Add(_sidebar);

        Shown += (_, _) =>
        {
            SyncMenuButtonWidths();
            SyncFooterItemWidths();
            RefreshFooterEntityName();
            _ = PrefetchFooterEntityNameAsync();
            InventarioSnapshotCoordinator.Start();
            _ = EnsureMachineRegisteredOnStartupAsync();
        };
        Load += OnMainFormLoad;
    }

    async Task EnsureMachineRegisteredOnStartupAsync()
    {
        if (_machineRegistrationStarted || IsDisposed)
            return;
        _machineRegistrationStarted = true;

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
    }

    void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exitRequested)
            return;
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == (int)SingleInstanceApp.InstanceActivateMessage)
        {
            ShowFromTray();
            m.Result = IntPtr.Zero;
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

        _exitRequested = true;
        _trayIcon.Visible = false;
        Close();
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

        UpdateFooterSelection(id);
        _activePageId = id;
        AppDebugLog.Debug("UI", $"Página activa: {id}");

        if (_pageTransitionTimer.Enabled)
        {
            _pageTransitionTimer.Stop();
            CancelOngoingPageTransition();
        }

        UserControl next = CreatePage(id);

        if (_contentHost.Controls.Count == 0)
        {
            next.Dock = DockStyle.Fill;
            _contentHost.Controls.Add(next);
            return;
        }

        var previous = (UserControl)_contentHost.Controls[0];
        StartPageTransition(previous, next);
    }

    void StartPageTransition(UserControl oldPage, UserControl newPage)
    {
        _transitionOldPage = oldPage;
        _transitionNewPage = newPage;

        var size = _contentHost.ClientSize;
        oldPage.Dock = DockStyle.None;
        oldPage.Size = size;
        oldPage.Location = Point.Empty;

        newPage.Dock = DockStyle.None;
        newPage.Size = size;
        newPage.Location = new Point(size.Width, 0);
        _contentHost.Controls.Add(newPage);

        _transitionStartTick = Environment.TickCount64;
        _pageTransitionTimer.Start();
    }

    void OnPageTransitionTick(object? sender, EventArgs e)
    {
        if (_transitionOldPage is null || _transitionNewPage is null)
        {
            _pageTransitionTimer.Stop();
            return;
        }

        var w = Math.Max(1, _contentHost.ClientSize.Width);
        var elapsed = Environment.TickCount64 - _transitionStartTick;
        var t = Math.Min(1d, elapsed / (double)PageTransitionDurationMs);
        var smooth = t * t * (3d - 2d * t);

        int oldX = (int)Math.Round(-smooth * w);
        int newX = (int)Math.Round((1d - smooth) * w);

        _transitionOldPage.Location = new Point(oldX, 0);
        _transitionNewPage.Location = new Point(newX, 0);

        if (t >= 1d)
            CompletePageTransition();
    }

    void OnContentHostResizeDuringTransition(object? sender, EventArgs e)
    {
        if (!_pageTransitionTimer.Enabled || _transitionOldPage is null || _transitionNewPage is null)
            return;

        var size = _contentHost.ClientSize;
        _transitionOldPage.Size = size;
        _transitionNewPage.Size = size;

        var w = Math.Max(1, size.Width);
        var elapsed = Environment.TickCount64 - _transitionStartTick;
        var t = Math.Min(1d, elapsed / (double)PageTransitionDurationMs);
        var smooth = t * t * (3d - 2d * t);
        _transitionOldPage.Location = new Point((int)Math.Round(-smooth * w), 0);
        _transitionNewPage.Location = new Point((int)Math.Round((1d - smooth) * w), 0);
    }

    void CompletePageTransition()
    {
        _pageTransitionTimer.Stop();

        if (_transitionOldPage is not null)
        {
            _contentHost.Controls.Remove(_transitionOldPage);
            _transitionOldPage.Dispose();
            _transitionOldPage = null;
        }

        if (_transitionNewPage is not null)
        {
            _transitionNewPage.Dock = DockStyle.Fill;
            _transitionNewPage.Location = Point.Empty;
            _transitionNewPage = null;
        }
    }

    void CancelOngoingPageTransition()
    {
        foreach (Control c in _contentHost.Controls.Cast<Control>().ToList())
        {
            _contentHost.Controls.Remove(c);
            c.Dispose();
        }

        _transitionOldPage = null;
        _transitionNewPage = null;
    }

    void UpdateFooterSelection(string id)
    {
        bool isSettings = string.Equals(id, "settings", StringComparison.OrdinalIgnoreCase);
        _footerSettings.Selected = isSettings;
    }

    void OnChamadosSnapshotUpdated() => RefreshFooterEntityName();

    void RefreshFooterEntityName()
    {
        if (ChamadosDataCache.TryGetEntityDisplayName(out var name))
            _footerEntityBadge.DisplayText = name;
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

            _footerEntityBadge.DisplayText = name;
        }
        catch
        {
            // Mantém o rodapé vazio se a consulta falhar; o cache será preenchido ao abrir Chamados.
        }
    }

    UserControl CreatePage(string id)
    {
        if (string.Equals(id, "home", StringComparison.OrdinalIgnoreCase))
            return new HomeView();

        if (string.Equals(id, "settings", StringComparison.OrdinalIgnoreCase))
            return new SettingsView();

        if (string.Equals(id, "client", StringComparison.OrdinalIgnoreCase))
            return new PlaceholderView("Cliente", "Informações da conta ou entidade serão apresentadas aqui.");

        if (_modulesById.TryGetValue(id, out var module))
            return module.CreateContentView();

        return new PlaceholderView("SistecHub", "Página não encontrada.");
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
