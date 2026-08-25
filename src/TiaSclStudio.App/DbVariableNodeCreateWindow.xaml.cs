using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    public partial class DbVariableNodeCreateWindow : Window
    {
        private readonly DiagramProject _project;
        private readonly Guid _sheetId;
        private bool _initialized;

        public DbVariableNodeCreateWindow(DiagramProject project, Guid sheetId)
        {
            _project = project ?? throw new ArgumentNullException("project");
            _sheetId = sheetId;
            InitializeComponent();

            var plant = _project.Plant ?? new PlantProject();
            var variables = (plant.DataBlockVariables ?? new List<DataBlockVariableDefinition>())
                .Where(item => item != null)
                .OrderBy(item => item.DataBlockName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.VariableName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ExistingVariableComboBox.ItemsSource = variables;
            ExistingVariableComboBox.SelectedIndex = variables.Count == 0 ? -1 : 0;
            ExistingVariableRadioButton.IsEnabled = variables.Count > 0;

            var dataTypes = (plant.DataTypes ?? new List<UdtDefinition>())
                .Where(item => item != null)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            DataTypeComboBox.ItemsSource = dataTypes;
            DataTypeComboBox.SelectedItem = dataTypes.FirstOrDefault(item =>
                string.Equals(item.Name, "Valve_Data", StringComparison.OrdinalIgnoreCase));
            if (DataTypeComboBox.SelectedItem == null && dataTypes.Count > 0)
            {
                DataTypeComboBox.SelectedIndex = 0;
            }

            DirectionComboBox.SelectedIndex = 0;
            _initialized = true;
            UpdateMode();
            UpdateValidation();
        }

        internal DbVariableNodeCreationResult Result { get; private set; }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (!_initialized)
            {
                return;
            }

            UpdateMode();
            UpdateValidation();
        }

        private void Field_Changed(object sender, RoutedEventArgs e)
        {
            UpdateValidation();
        }

        private void UpdateMode()
        {
            var useExisting = ExistingVariableRadioButton.IsChecked == true;
            ExistingPanel.Visibility = useExisting ? Visibility.Visible : Visibility.Collapsed;
            NewPanel.Visibility = useExisting ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateValidation()
        {
            if (!_initialized || OkButton == null)
            {
                return;
            }

            var candidate = CreateCandidate();
            var errors = candidate == null
                ? new List<string> { "Выберите DB-переменную и UDT." }
                : DbVariableNodeCreationLogic.Validate(_project, _sheetId, candidate).ToList();
            OkButton.IsEnabled = errors.Count == 0;
            ValidationText.Text = errors.Count == 0
                ? "Готово: " + candidate.Node.DataBlockName + "." + candidate.Node.VariableName +
                    " : " + candidate.Node.DataType
                : string.Join(Environment.NewLine, errors.Select(error => "• " + error));
            ValidationText.Foreground = FindResource(errors.Count == 0 ? "SuccessBrush" : "ErrorBrush") as Brush;
        }

        private DbVariableNodeCreationResult CreateCandidate()
        {
            var directionItem = DirectionComboBox.SelectedItem as ComboBoxItem;
            TerminalDirection direction;
            if (directionItem == null ||
                !Enum.TryParse(directionItem.Tag as string, false, out direction))
            {
                return null;
            }

            if (ExistingVariableRadioButton.IsChecked == true)
            {
                var existing = ExistingVariableComboBox.SelectedItem as DataBlockVariableDefinition;
                return existing == null
                    ? null
                    : DbVariableNodeCreationLogic.CreateExisting(existing, direction);
            }

            var udt = DataTypeComboBox.SelectedItem as UdtDefinition;
            return udt == null
                ? null
                : DbVariableNodeCreationLogic.CreateNew(
                    _project,
                    DataBlockNameTextBox.Text,
                    VariableNameTextBox.Text,
                    udt,
                    CommentTextBox.Text,
                    GenerateDataBlockCheckBox.IsChecked == true,
                    direction);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var candidate = CreateCandidate();
            var errors = candidate == null
                ? new List<string> { "Выберите DB-переменную и UDT." }
                : DbVariableNodeCreationLogic.Validate(_project, _sheetId, candidate).ToList();
            if (errors.Count > 0)
            {
                UpdateValidation();
                return;
            }

            Result = candidate;
            DialogResult = true;
        }
    }
}
