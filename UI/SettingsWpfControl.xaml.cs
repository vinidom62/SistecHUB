using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SistecHub.Core;
using SistecHub.Modulos.GLPI;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SistecHub.UI;

public sealed partial class SettingsWpfControl : System.Windows.Controls.UserControl
{
    const string LockGlyph = "\uE72E";
    const string UnlockGlyph = "\uE785";

    public event EventHandler? CheckUpdateClicked;
    public event EventHandler? CheckBetaUpdateClicked;
    public event EventHandler? ExportJsonClicked;
    public event EventHandler? EnviarInventarioClicked;
    public event EventHandler? MachineIdLockClicked;
    public event EventHandler? SaveClicked;
    public event EventHandler? BloqueioRemotoClicked;

    static readonly MediaBrush LockedFieldBgBrush = CreateFrozenBrush(241, 245, 249);
    static readonly MediaBrush UnlockedFieldBgBrush = CreateFrozenBrush(255, 255, 255);
    static readonly MediaBrush AccentBrush = CreateFrozenBrush(99, 102, 241);
    static readonly MediaBrush DangerBrush = CreateFrozenBrush(185, 28, 28);
    static readonly MediaBrush BadgeLiberadoBgBrush = CreateFrozenBrush(220, 252, 231);
    static readonly MediaBrush BadgeLiberadoFgBrush = CreateFrozenBrush(22, 163, 74);
    static readonly MediaBrush BadgeBloqueadoBgBrush = CreateFrozenBrush(254, 226, 226);
    static readonly MediaBrush BadgeBloqueadoFgBrush = CreateFrozenBrush(220, 38, 38);

    public SettingsWpfControl()
    {
        InitializeComponent();
        HintRemotoServerUrl.Text = AppSettingsStore.DefaultRemotoServerUrl;
    }

    public string GlpiUserToken
    {
        get => (PwdGlpiUserToken.Password ?? "").Trim();
        set => PwdGlpiUserToken.Password = value ?? "";
    }

    public string GlpiMachineId
    {
        get => (TxtGlpiMachineId.Text ?? "").Trim();
        set => TxtGlpiMachineId.Text = value ?? "";
    }

    public string RemotoServerUrl
    {
        get => (TxtRemotoServerUrl.Text ?? "").Trim();
        set => TxtRemotoServerUrl.Text = value ?? "";
    }

    public bool IniciarMinimizado
    {
        get => ChkIniciarMinimizado.IsChecked == true;
        set => ChkIniciarMinimizado.IsChecked = value;
    }

    public bool RemotoBloqueado { get; private set; }

    public void SetRemotoBloqueado(bool blocked)
    {
        RemotoBloqueado = blocked;
        if (blocked)
        {
            TxtBloqueioBadge.Text = "BLOQUEADO";
            TxtBloqueioBadge.Foreground = BadgeBloqueadoFgBrush;
            BdBloqueioBadge.Background = BadgeBloqueadoBgBrush;
            TxtBloqueioDesc.Text = "O acesso remoto está desabilitado nesta máquina. Nenhuma conexão de suporte pode ser realizada.";
            BtnToggleBloqueioRemoto.Content = "Desbloquear acesso remoto";
            if (TryFindResource("OutlineIndigoButtonStyle") is Style outlineStyle)
                BtnToggleBloqueioRemoto.Style = outlineStyle;
        }
        else
        {
            TxtBloqueioBadge.Text = "LIBERADO";
            TxtBloqueioBadge.Foreground = BadgeLiberadoFgBrush;
            BdBloqueioBadge.Background = BadgeLiberadoBgBrush;
            TxtBloqueioDesc.Text = "Desabilita o sistema de acesso remoto do SistecHUB neste computador e impede conexões de suporte.";
            BtnToggleBloqueioRemoto.Content = "Bloquear acesso remoto nessa máquina";
            if (TryFindResource("SoftDangerButtonStyle") is Style dangerStyle)
                BtnToggleBloqueioRemoto.Style = dangerStyle;
        }
    }

    public int EntityComboItemCount => CmbEntity.Items.Count;

    public void InitializeHeaderInfo(string displayVersion, string updateStatus, bool isUpdateSupported)
    {
        TxtVersion.Text = $"Versão: {displayVersion}";
        TxtUpdateStatus.Text = updateStatus ?? "";
        TxtUpdateUnsupported.Visibility = isUpdateSupported ? Visibility.Collapsed : Visibility.Visible;
        BtnCheckUpdate.IsEnabled = isUpdateSupported;
        BtnCheckBetaUpdate.IsEnabled = isUpdateSupported;
    }

    public void SetUpdateStatusText(string text)
    {
        TxtUpdateStatus.Text = text ?? "";
    }

    public void SetUpdateButtonsEnabled(bool enabled)
    {
        BtnCheckUpdate.IsEnabled = enabled;
        BtnCheckBetaUpdate.IsEnabled = enabled;
    }

    public void SetEnviarInventarioEnabled(bool enabled)
    {
        BtnEnviarInventario.IsEnabled = enabled;
    }

    public void SetSaveEnabled(bool enabled)
    {
        BtnSave.IsEnabled = enabled;
    }

    public void ShowFeedback(string text)
    {
        TxtFeedback.Text = text ?? "";
        TxtFeedback.Visibility = Visibility.Visible;
    }

    public void HideFeedback()
    {
        TxtFeedback.Visibility = Visibility.Collapsed;
    }

    public void SetMachineIdUnlockedState(bool unlocked)
    {
        TxtGlpiMachineId.IsReadOnly = !unlocked;
        BdGlpiMachineId.Background = unlocked ? UnlockedFieldBgBrush : LockedFieldBgBrush;
        TxtMachineIdLockGlyph.Text = unlocked ? UnlockGlyph : LockGlyph;
        TxtMachineIdLockGlyph.Foreground = unlocked ? DangerBrush : AccentBrush;
    }

    public void SetEntityComboEnabled(bool enabled)
    {
        CmbEntity.IsEnabled = enabled;
    }

    public void ShowNoEntityConfigured()
    {
        CmbEntity.IsEnabled = false;
        CmbEntity.Items.Clear();
        CmbEntity.Items.Add("Nenhuma entidade configurada");
        CmbEntity.SelectedIndex = 0;
    }

    internal void BindEntities(IReadOnlyList<GlpiEntityInfo> entities, int selectedEntityId)
    {
        CmbEntity.Items.Clear();
        int matchIdx = -1;
        for (int i = 0; i < entities.Count; i++)
        {
            var e = entities[i];
            CmbEntity.Items.Add(new EntityComboItem(e));
            if (selectedEntityId >= 1 && e.Id == selectedEntityId && matchIdx < 0)
                matchIdx = i;
        }

        if (matchIdx >= 0)
            CmbEntity.SelectedIndex = matchIdx;
        else if (CmbEntity.Items.Count > 0)
            CmbEntity.SelectedIndex = 0;
    }

    public int GetSelectedEntityId()
    {
        return CmbEntity.SelectedItem is EntityComboItem item ? item.Entity.Id : 0;
    }

    private void TxtRemotoServerUrl_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        HintRemotoServerUrl.Visibility = string.IsNullOrEmpty(TxtRemotoServerUrl.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        => CheckUpdateClicked?.Invoke(this, EventArgs.Empty);

    private void BtnCheckBetaUpdate_Click(object sender, RoutedEventArgs e)
        => CheckBetaUpdateClicked?.Invoke(this, EventArgs.Empty);

    private void BtnExportJson_Click(object sender, RoutedEventArgs e)
        => ExportJsonClicked?.Invoke(this, EventArgs.Empty);

    private void BtnEnviarInventario_Click(object sender, RoutedEventArgs e)
        => EnviarInventarioClicked?.Invoke(this, EventArgs.Empty);

    private void MachineIdLock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => MachineIdLockClicked?.Invoke(this, EventArgs.Empty);

    private void BtnSave_Click(object sender, RoutedEventArgs e)
        => SaveClicked?.Invoke(this, EventArgs.Empty);

    private void BtnToggleBloqueioRemoto_Click(object sender, RoutedEventArgs e)
        => BloqueioRemotoClicked?.Invoke(this, EventArgs.Empty);

    static MediaBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new MediaSolidColorBrush(MediaColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    sealed class EntityComboItem(GlpiEntityInfo entity)
    {
        public GlpiEntityInfo Entity { get; } = entity;

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Entity.PickerLabel) ? Entity.Name : Entity.PickerLabel;
    }
}

