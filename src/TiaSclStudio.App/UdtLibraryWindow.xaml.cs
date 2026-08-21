using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    public partial class UdtLibraryWindow : Window
    {
        private readonly DiagramProject _project;
        private readonly Dictionary<Guid, UdtDefinition> _originalById;
        private readonly ObservableCollection<UdtDefinition> _dataTypes;

        public UdtLibraryWindow(DiagramProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            InitializeComponent();
            _project = project;
            var source = project.Plant == null
                ? new List<UdtDefinition>()
                : project.Plant.DataTypes ?? new List<UdtDefinition>();
            _dataTypes = new ObservableCollection<UdtDefinition>(
                source.Where(item => item != null).Select(UdtEditorWindow.CloneDataType));
            _originalById = source
                .Where(item => item != null)
                .GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => UdtEditorWindow.CloneDataType(group.First()));
            DataTypeList.ItemsSource = _dataTypes;
            DataTypeList.SelectedIndex = _dataTypes.Count == 0 ? -1 : 0;
            UpdateState();
        }

        public IList<UdtDefinition> EditedDataTypes { get; private set; }

        private void DataTypeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateState();
        }

        private void DataTypeList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            EditSelectedDataType();
            e.Handled = true;
        }

        private void NewDataType_Click(object sender, RoutedEventArgs e)
        {
            var draft = new UdtDefinition
            {
                Name = CreateUniqueDataTypeName(),
                Version = "0.1"
            };
            var editor = new UdtEditorWindow(draft, _dataTypes.Concat(new[] { draft }), true)
            {
                Owner = this
            };
            if (editor.ShowDialog() != true || editor.EditedDataType == null)
            {
                return;
            }

            string collision;
            if (TryFindGlobalNameCollision(editor.EditedDataType, out collision))
            {
                ShowCollision(collision);
                return;
            }

            _dataTypes.Add(editor.EditedDataType);
            DataTypeList.SelectedItem = editor.EditedDataType;
            DataTypeList.ScrollIntoView(editor.EditedDataType);
            UpdateState();
        }

        private void EditDataType_Click(object sender, RoutedEventArgs e)
        {
            EditSelectedDataType();
        }

        private void EditSelectedDataType()
        {
            var selected = DataTypeList.SelectedItem as UdtDefinition;
            if (selected == null)
            {
                return;
            }

            var editor = new UdtEditorWindow(selected, _dataTypes, false)
            {
                Owner = this
            };
            if (editor.ShowDialog() != true || editor.EditedDataType == null)
            {
                return;
            }

            string collision;
            if (TryFindGlobalNameCollision(editor.EditedDataType, out collision))
            {
                ShowCollision(collision);
                return;
            }

            var candidate = editor.EditedDataType;
            var oldName = selected.Name;
            if (!string.Equals(oldName, candidate.Name, StringComparison.Ordinal))
            {
                UdtLibraryEditorLogic.RewriteReferences(
                    _dataTypes.Where(item => item.Id != selected.Id),
                    oldName,
                    candidate.Name);
            }

            var index = _dataTypes.IndexOf(selected);
            _dataTypes[index] = candidate;
            DataTypeList.SelectedItem = candidate;
            UpdateState();
        }

        private void DeleteDataType_Click(object sender, RoutedEventArgs e)
        {
            var selected = DataTypeList.SelectedItem as UdtDefinition;
            if (selected == null)
            {
                return;
            }

            var referenceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                selected.Name
            };
            UdtDefinition original;
            if (_originalById.TryGetValue(selected.Id, out original))
            {
                referenceNames.Add(original.Name);
            }

            var usages = UdtLibraryEditorLogic.FindUsages(
                _project,
                _dataTypes,
                selected.Id,
                referenceNames);
            if (usages.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "UDT «" + selected.Name + "» удалить нельзя: он используется.\n\n" +
                    string.Join("\n", usages.Take(10).Select(item => "• " + item)) +
                    (usages.Count > 10 ? "\n• и ещё мест: " + (usages.Count - 10) : string.Empty),
                    "Удаление UDT заблокировано",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var answer = MessageBox.Show(
                this,
                "Удалить неиспользуемый UDT «" + selected.Name + "»?",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            var index = _dataTypes.IndexOf(selected);
            _dataTypes.Remove(selected);
            DataTypeList.SelectedIndex = _dataTypes.Count == 0
                ? -1
                : Math.Min(index, _dataTypes.Count - 1);
            UpdateState();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            var validation = UdtLibraryEditorLogic.ValidateCandidates(_project, _dataTypes);
            if (validation.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, validation.Select(item => "• " + item)),
                    "Библиотека UDT не прошла проверку",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            EditedDataTypes = _dataTypes.Select(UdtEditorWindow.CloneDataType).ToList();
            DialogResult = true;
        }

        private bool TryFindGlobalNameCollision(UdtDefinition candidate, out string message)
        {
            message = string.Empty;
            var plant = _project.Plant ?? new PlantProject();
            var owner = (plant.Blocks ?? new List<BlockDefinition>())
                .Where(item => item != null)
                .Select(item => new { item.Name, Kind = "блоком" })
                .Concat((plant.Tags ?? new List<TagDefinition>())
                    .Where(item => item != null)
                    .Select(item => new { item.Name, Kind = "PLC-тегом" }))
                .Concat((plant.CallBlocks ?? new List<CallBlockDefinition>())
                    .Where(item => item != null)
                    .Select(item => new { item.Name, Kind = "блоком вызовов" }))
                .Concat((_project.Sheets ?? new List<CallSheet>())
                    .Where(item => item != null)
                    .Select(item => new { item.Name, Kind = "листом вызовов" }))
                .FirstOrDefault(item => string.Equals(
                    item.Name,
                    candidate.Name,
                    StringComparison.OrdinalIgnoreCase));
            if (owner == null)
            {
                return false;
            }

            message = "Имя «" + candidate.Name + "» уже занято " + owner.Kind + ".";
            return true;
        }

        private void ShowCollision(string message)
        {
            MessageBox.Show(
                this,
                message,
                "Имя UDT недоступно",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private string CreateUniqueDataTypeName()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var plant = _project.Plant ?? new PlantProject();
            foreach (var name in (plant.Blocks ?? new List<BlockDefinition>())
                .Where(item => item != null).Select(item => item.Name)
                .Concat((plant.Tags ?? new List<TagDefinition>())
                    .Where(item => item != null).Select(item => item.Name))
                .Concat((plant.CallBlocks ?? new List<CallBlockDefinition>())
                    .Where(item => item != null).Select(item => item.Name))
                .Concat((_project.Sheets ?? new List<CallSheet>())
                    .Where(item => item != null).Select(item => item.Name))
                .Concat(_dataTypes.Select(item => item.Name)))
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            var sequence = 1;
            string candidate;
            do
            {
                candidate = "UDT_NewType_" + sequence;
                sequence++;
            }
            while (names.Contains(candidate));
            return candidate;
        }

        private void UpdateState()
        {
            var selected = DataTypeList.SelectedItem is UdtDefinition;
            EditButton.IsEnabled = selected;
            DeleteButton.IsEnabled = selected;
            CountText.Text = _dataTypes.Count + " UDT";
        }
    }
}
