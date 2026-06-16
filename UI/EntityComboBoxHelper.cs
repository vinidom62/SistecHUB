using SistecHub.Modulos.GLPI;

namespace SistecHub.UI;

internal static class EntityComboBoxHelper
{
    public const int MaxVisibleDropDownItems = 20;

    public static void Configure(ComboBox comboBox)
    {
        comboBox.IntegralHeight = false;
        comboBox.MaxDropDownItems = MaxVisibleDropDownItems;
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    }

    /// <summary>
    /// Liga entidades ao ComboBox sem usar <c>DisplayMember</c> (evita conflito com essa propriedade no WinForms).
    /// </summary>
    public static void Bind(ComboBox comboBox, IReadOnlyList<GlpiEntityInfo> entities, int selectedEntityId)
    {
        comboBox.DataSource = null;
        comboBox.Items.Clear();
        comboBox.DisplayMember = string.Empty;
        comboBox.ValueMember = nameof(GlpiEntityInfo.Id);

        var list = entities.ToList();
        var bindingSource = new BindingSource { DataSource = list };
        comboBox.DataSource = bindingSource;

        if (selectedEntityId >= 1)
        {
            try
            {
                comboBox.SelectedValue = selectedEntityId;
            }
            catch (ArgumentException)
            {
                // Id não está na lista filtrada; mantém seleção abaixo.
            }
        }

        if (comboBox.SelectedIndex < 0 && comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
    }

    public static bool MatchesFilter(GlpiEntityInfo entity, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return true;

        return entity.PickerLabel.Contains(term, StringComparison.OrdinalIgnoreCase)
            || entity.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || entity.LeafDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
