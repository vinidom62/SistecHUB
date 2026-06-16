using SistecHub.Modulos.GLPI;

namespace SistecHub.UI;

internal static class EntityComboBoxHelper
{
    public const int MaxVisibleDropDownItems = 20;

    sealed class EntityComboEntry(GlpiEntityInfo entity)
    {
        public GlpiEntityInfo Entity { get; } = entity;

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Entity.PickerLabel) ? Entity.Name : Entity.PickerLabel;
    }

    public static ComboBox Create(int width) =>
        new()
        {
            Width = width,
            DropDownStyle = ComboBoxStyle.DropDownList,
            IntegralHeight = false,
            MaxDropDownItems = MaxVisibleDropDownItems,
            DropDownWidth = Math.Max(width, 480),
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
        };

    public static void Bind(ComboBox comboBox, IReadOnlyList<GlpiEntityInfo> entities, int selectedEntityId)
    {
        comboBox.DataSource = null;
        comboBox.DisplayMember = string.Empty;
        comboBox.ValueMember = string.Empty;

        comboBox.BeginUpdate();
        try
        {
            comboBox.Items.Clear();
            foreach (var entity in entities)
                comboBox.Items.Add(new EntityComboEntry(entity));
        }
        finally
        {
            comboBox.EndUpdate();
        }

        if (!SelectEntityId(comboBox, selectedEntityId) && comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
    }

    public static bool SelectEntityId(ComboBox comboBox, int selectedEntityId)
    {
        if (selectedEntityId < 1)
            return false;

        for (var i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is EntityComboEntry entry && entry.Entity.Id == selectedEntityId)
            {
                comboBox.SelectedIndex = i;
                return true;
            }
        }

        return false;
    }

    public static int GetSelectedEntityId(ComboBox comboBox) =>
        comboBox.SelectedItem is EntityComboEntry entry ? entry.Entity.Id : 0;
}
