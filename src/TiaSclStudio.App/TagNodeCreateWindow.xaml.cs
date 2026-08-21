using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    public partial class TagNodeCreateWindow : Window
    {
        private readonly DiagramProject _project;
        private readonly Guid _sheetId;
        private readonly TerminalDirection _direction;
        private readonly PlcCpuFamily? _fixedCpuFamily;
        private bool _initialized;

        public TagNodeCreateWindow(
            DiagramProject project,
            Guid sheetId,
            TerminalDirection direction)
            : this(project, sheetId, direction, null, false)
        {
        }

        /// <summary>
        /// Online integration can pass a read-only hardware channel catalogue and
        /// request strict I/O selection. Marker addresses remain manually editable.
        /// </summary>
        internal TagNodeCreateWindow(
            DiagramProject project,
            Guid sheetId,
            TerminalDirection direction,
            IEnumerable<PlcAddressChannelChoice> availableChannels,
            bool restrictHardwareIo)
            : this(
                project,
                sheetId,
                direction,
                availableChannels,
                restrictHardwareIo,
                null)
        {
        }

        internal TagNodeCreateWindow(
            DiagramProject project,
            Guid sheetId,
            TerminalDirection direction,
            IEnumerable<PlcAddressChannelChoice> availableChannels,
            bool restrictHardwareIo,
            PlcCpuFamily? fixedCpuFamily)
        {
            _project = project ?? throw new ArgumentNullException("project");
            _sheetId = sheetId;
            _direction = direction;
            _fixedCpuFamily = fixedCpuFamily;
            InitializeComponent();

            var allTags = ((_project.Plant ?? new PlantProject()).Tags ?? new List<TagDefinition>())
                .Where(tag => tag != null)
                .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var selectionContext = new HardwareIoSelectionContext(
                fixedCpuFamily,
                availableChannels,
                "Аппаратный каталог PLC недоступен.");
            var tags = restrictHardwareIo
                ? allTags.Where(selectionContext.IsStoredTagSelectable).ToList()
                : allTags;
            ExistingTagComboBox.ItemsSource = tags;
            ExistingTagComboBox.SelectedIndex = tags.Count == 0 ? -1 : 0;
            ExistingTagRadioButton.IsEnabled = tags.Count != 0;
            DirectionBadgeText.Text = direction == TerminalDirection.Source ? "SOURCE" : "SINK";
            TagNameTextBox.Text = CreateDefaultTagName(allTags);

            AddressEditor.ValueChanged += delegate { UpdateLiveValidation(); };
            var projectCpu = fixedCpuFamily ?? (_project.Plant ?? new PlantProject()).CpuFamily;
            AddressEditor.LoadValue(projectCpu, "Bool", "%I0.0");
            if (fixedCpuFamily == PlcCpuFamily.S71200 || fixedCpuFamily == PlcCpuFamily.S71500)
            {
                AddressEditor.IsCpuSelectionEnabled = false;
            }
            AddressEditor.ConfigureAvailableChannels(availableChannels, restrictHardwareIo);

            _initialized = true;
            UpdateModeVisibility();
            Loaded += delegate
            {
                TagNameTextBox.Focus();
                TagNameTextBox.SelectAll();
            };
            UpdateLiveValidation();
        }

        internal NodeCreationResult Result { get; private set; }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (NewTagPanel != null)
            {
                UpdateModeVisibility();
            }
        }

        private void UpdateModeVisibility()
        {
            var useExisting = ExistingTagRadioButton.IsChecked == true;
            ExistingTagPanel.Visibility = useExisting ? Visibility.Visible : Visibility.Collapsed;
            NewTagPanel.Visibility = useExisting ? Visibility.Collapsed : Visibility.Visible;
            UpdateLiveValidation();
        }

        private void NewTagField_Changed(object sender, RoutedEventArgs e)
        {
            UpdateLiveValidation();
        }

        private void UpdateLiveValidation()
        {
            if (!_initialized || ValidationText == null || OkButton == null)
            {
                return;
            }

            if (ExistingTagRadioButton.IsChecked == true)
            {
                var hasSelection = ExistingTagComboBox.SelectedItem is TagDefinition;
                OkButton.IsEnabled = hasSelection;
                if (hasSelection)
                {
                    ShowReady("Будет добавлен ещё один терминал выбранного PLC-тега.");
                }
                else
                {
                    ShowErrors(new[] { "Выберите существующий PLC-тег." });
                }

                return;
            }

            PlcCpuFamily cpuFamily;
            string dataType;
            string address;
            string addressError;
            if (!AddressEditor.TryGetValue(
                out cpuFamily,
                out dataType,
                out address,
                out addressError))
            {
                OkButton.IsEnabled = false;
                ShowErrors(new[] { addressError });
                return;
            }

            var candidate = NodeCreationLogic.CreateNewTag(
                TagNameTextBox.Text,
                dataType,
                address,
                TagCommentTextBox.Text,
                _direction,
                cpuFamily);
            var errors = NodeCreationLogic.Validate(_project, _sheetId, candidate);
            OkButton.IsEnabled = errors.Count == 0;
            if (errors.Count > 0)
            {
                ShowErrors(errors);
                return;
            }

            ShowReady(address + "  ·  " + dataType + "  ·  " + CpuLabel(cpuFamily));
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            NodeCreationResult candidate;
            if (ExistingTagRadioButton.IsChecked == true)
            {
                var tag = ExistingTagComboBox.SelectedItem as TagDefinition;
                if (tag == null)
                {
                    ShowErrors(new[] { "Выберите существующий PLC-тег." });
                    return;
                }

                candidate = NodeCreationLogic.CreateExistingTag(
                    tag.Id,
                    _direction,
                    _fixedCpuFamily);
            }
            else
            {
                PlcCpuFamily cpuFamily;
                string dataType;
                string address;
                string addressError;
                if (!AddressEditor.TryGetValue(
                    out cpuFamily,
                    out dataType,
                    out address,
                    out addressError))
                {
                    ShowErrors(new[] { addressError });
                    return;
                }

                candidate = NodeCreationLogic.CreateNewTag(
                    TagNameTextBox.Text,
                    dataType,
                    address,
                    TagCommentTextBox.Text,
                    _direction,
                    cpuFamily);
            }

            var errors = NodeCreationLogic.Validate(_project, _sheetId, candidate);
            if (errors.Count != 0)
            {
                ShowErrors(errors);
                return;
            }

            Result = candidate;
            DialogResult = true;
        }

        private void ShowErrors(IEnumerable<string> errors)
        {
            ValidationText.Text = string.Join(
                Environment.NewLine,
                errors.Where(error => !string.IsNullOrWhiteSpace(error)).Select(error => "• " + error));
            ValidationText.Foreground = FindResource("ErrorBrush") as Brush;
        }

        private void ShowReady(string message)
        {
            ValidationText.Text = message ?? string.Empty;
            ValidationText.Foreground = FindResource("SuccessBrush") as Brush;
        }

        private static string CpuLabel(PlcCpuFamily cpuFamily)
        {
            return cpuFamily == PlcCpuFamily.S71500 ? "S7-1500" : "S7-1200";
        }

        private static string CreateDefaultTagName(IEnumerable<TagDefinition> tags)
        {
            var used = new HashSet<string>(
                tags.Select(tag => (tag.Name ?? string.Empty).Trim()),
                StringComparer.OrdinalIgnoreCase);
            for (var number = 1; ; number++)
            {
                var candidate = "New_Tag_" + number;
                if (!used.Contains(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
