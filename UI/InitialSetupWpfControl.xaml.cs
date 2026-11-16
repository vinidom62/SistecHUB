using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using SistecHub.Modulos.GLPI;

namespace SistecHub.UI;

public sealed partial class InitialSetupWpfControl : System.Windows.Controls.UserControl
{
    public event EventHandler? ContinueRequested;

    public InitialSetupWpfControl()
    {
        InitializeComponent();
    }

    public string Token => PwdToken.Password ?? "";

    public void SetStepGlpiToken(string token)
    {
        TxtStepIndicator.Text = "Passo 1 de 2";
        TxtSubtitle.Text = "Indique o user token do GLPI para continuar.";
        TxtFieldLabel.Text = "User token";
        PwdToken.Password = token ?? "";
        BdTokenInput.Visibility = Visibility.Visible;
        CmbEntity.Visibility = Visibility.Collapsed;
        BtnContinue.Content = "Continuar";
        PwdToken.Focus();
    }

    public void SetStepEntity(IReadOnlyList<GlpiEntityInfo>? entities, int selectedId, bool loading)
    {
        TxtStepIndicator.Text = "Passo 2 de 2";
        TxtSubtitle.Text = "Selecione a entidade no GLPI.";
        TxtFieldLabel.Text = "Entidade";
        BdTokenInput.Visibility = Visibility.Collapsed;
        CmbEntity.Visibility = Visibility.Visible;
        BtnContinue.Content = "Salvar e continuar";

        CmbEntity.Items.Clear();
        if (loading)
        {
            CmbEntity.Items.Add("A carregar entidades...");
            CmbEntity.SelectedIndex = 0;
            CmbEntity.IsEnabled = false;
            BtnContinue.IsEnabled = false;
            return;
        }

        CmbEntity.IsEnabled = true;
        BtnContinue.IsEnabled = true;

        if (entities != null)
        {
            int matchIdx = -1;
            for (int i = 0; i < entities.Count; i++)
            {
                var e = entities[i];
                CmbEntity.Items.Add(new EntityItem(e));
                if (selectedId >= 1 && e.Id == selectedId && matchIdx < 0)
                    matchIdx = i;
            }

            if (matchIdx >= 0)
                CmbEntity.SelectedIndex = matchIdx;
            else if (CmbEntity.Items.Count > 0)
                CmbEntity.SelectedIndex = 0;
        }

        CmbEntity.Focus();
    }

    public int GetSelectedEntityId()
    {
        return CmbEntity.SelectedItem is EntityItem item ? item.Entity.Id : 0;
    }

    public void SetContinueEnabled(bool enabled)
    {
        BtnContinue.IsEnabled = enabled;
    }

    private void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ContinueRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void BtnContinue_Click(object sender, RoutedEventArgs e)
        => ContinueRequested?.Invoke(this, EventArgs.Empty);

    sealed class EntityItem(GlpiEntityInfo entity)
    {
        public GlpiEntityInfo Entity { get; } = entity;

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Entity.PickerLabel) ? Entity.Name : Entity.PickerLabel;
    }
}

