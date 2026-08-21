using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;

namespace TiaSclStudio.App
{
    internal sealed class PlcAddressChannelChoice
    {
        public PlcAddressChannelChoice(
            string displayName,
            PlcSignalKind signalKind,
            string dataType,
            string address)
        {
            DisplayName = displayName ?? string.Empty;
            SignalKind = signalKind;
            DataType = dataType ?? string.Empty;
            Address = address ?? string.Empty;
        }

        public string DisplayName { get; private set; }

        public PlcSignalKind SignalKind { get; private set; }

        public string DataType { get; private set; }

        public string Address { get; private set; }

        public override string ToString()
        {
            return DisplayName + "  ·  " + Address + "  ·  " + DataType;
        }
    }

    public partial class PlcAddressEditorControl : UserControl
    {
        private readonly List<PlcAddressChannelChoice> _configuredChannels =
            new List<PlcAddressChannelChoice>();
        private bool _updating;
        private bool _restrictHardwareIo;
        private bool _selectFirstConfiguredChannel = true;
        private string _preferredChannelAddress = string.Empty;
        private string _preferredChannelDataType = string.Empty;
        private string _canonicalAddress = string.Empty;
        private string _validationError = string.Empty;

        public PlcAddressEditorControl()
        {
            InitializeComponent();
            _updating = true;
            CpuComboBox.ItemsSource = new[]
            {
                new Choice<PlcCpuFamily>(PlcCpuFamily.Unknown, "Выберите CPU"),
                new Choice<PlcCpuFamily>(PlcCpuFamily.S71200, "S7-1200"),
                new Choice<PlcCpuFamily>(PlcCpuFamily.S71500, "S7-1500")
            };
            SignalComboBox.ItemsSource = new[]
            {
                new Choice<PlcSignalKind>(PlcSignalKind.DigitalInput, "DI · дискретный вход"),
                new Choice<PlcSignalKind>(PlcSignalKind.DigitalOutput, "DO · дискретный выход"),
                new Choice<PlcSignalKind>(PlcSignalKind.AnalogInput, "AI · аналоговый вход"),
                new Choice<PlcSignalKind>(PlcSignalKind.AnalogOutput, "AO · аналоговый выход"),
                new Choice<PlcSignalKind>(PlcSignalKind.Memory, "M · память")
            };
            BitOffsetComboBox.ItemsSource = Enumerable.Range(0, 8).ToList();
            SelectCpu(PlcCpuFamily.S71200);
            SignalComboBox.SelectedIndex = 0;
            BitOffsetComboBox.SelectedIndex = 0;
            ByteOffsetTextBox.Text = "0";
            RebuildDataTypes("Bool");
            _updating = false;
            UpdateAddress();
        }

        internal event EventHandler ValueChanged;

        internal bool IsCpuSelectionEnabled
        {
            get { return CpuComboBox.IsEnabled; }
            set { CpuComboBox.IsEnabled = value; }
        }

        internal void LoadValue(PlcCpuFamily cpuFamily, string dataType, string address)
        {
            _updating = true;
            var selectedCpu = cpuFamily == PlcCpuFamily.S71200 ||
                              cpuFamily == PlcCpuFamily.S71500
                ? cpuFamily
                : PlcCpuFamily.Unknown;
            SelectCpu(selectedCpu);

            PlcAddressSpec spec;
            string canonical;
            string error;
            var parsed = PlcAddressing.TryParse(
                cpuFamily,
                address,
                dataType,
                out spec,
                out canonical,
                out error);
            if (!parsed && cpuFamily != PlcCpuFamily.Unknown)
            {
                // Preserve a syntactically valid persisted address in the fields,
                // but never switch the CPU profile implicitly. The user must make
                // an explicit profile choice before the edit can be committed.
                parsed = PlcAddressing.TryParse(
                    PlcCpuFamily.Unknown,
                    address,
                    dataType,
                    out spec,
                    out canonical,
                    out error);
            }

            if (parsed)
            {
                SelectSignal(spec.SignalKind);
                RebuildDataTypes(spec.DataType);
                ByteOffsetTextBox.Text = spec.ByteOffset.ToString(CultureInfo.InvariantCulture);
                BitOffsetComboBox.SelectedItem = spec.BitOffset.HasValue ? (object)spec.BitOffset.Value : null;
            }
            else
            {
                SelectSignal(PlcSignalKind.DigitalInput);
                RebuildDataTypes("Bool");
                // An unsupported persisted address must stay visibly invalid. Falling
                // back to %I0.0 would let an innocent OK silently replace user data.
                ByteOffsetTextBox.Text = string.Empty;
                BitOffsetComboBox.SelectedItem = null;
            }

            _updating = false;
            RebuildConfiguredChannels();
            UpdateAddress();
        }

        internal void ConfigureAvailableChannels(
            IEnumerable<PlcAddressChannelChoice> channels,
            bool restrictHardwareIo)
        {
            ConfigureAvailableChannels(
                channels,
                restrictHardwareIo,
                null,
                null,
                true);
        }

        internal void ConfigureAvailableChannels(
            IEnumerable<PlcAddressChannelChoice> channels,
            bool restrictHardwareIo,
            string preferredAddress,
            string preferredDataType)
        {
            ConfigureAvailableChannels(
                channels,
                restrictHardwareIo,
                preferredAddress,
                preferredDataType,
                false);
        }

        private void ConfigureAvailableChannels(
            IEnumerable<PlcAddressChannelChoice> channels,
            bool restrictHardwareIo,
            string preferredAddress,
            string preferredDataType,
            bool selectFirst)
        {
            _configuredChannels.Clear();
            if (channels != null)
            {
                _configuredChannels.AddRange(channels.Where(channel => channel != null));
            }

            _restrictHardwareIo = restrictHardwareIo;
            _preferredChannelAddress = (preferredAddress ?? string.Empty).Trim();
            _preferredChannelDataType = (preferredDataType ?? string.Empty).Trim();
            _selectFirstConfiguredChannel = selectFirst;
            RebuildConfiguredChannels();
            UpdateAddress();
        }

        internal bool TryGetValue(
            out PlcCpuFamily cpuFamily,
            out string dataType,
            out string address,
            out string error)
        {
            cpuFamily = SelectedCpu;
            dataType = SelectedDataType;
            address = _canonicalAddress;
            error = _validationError;
            return string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(address);
        }

        private PlcCpuFamily SelectedCpu
        {
            get
            {
                var selected = CpuComboBox.SelectedItem as Choice<PlcCpuFamily>;
                return selected == null ? PlcCpuFamily.Unknown : selected.Value;
            }
        }

        private PlcSignalKind SelectedSignal
        {
            get
            {
                var selected = SignalComboBox.SelectedItem as Choice<PlcSignalKind>;
                return selected == null ? PlcSignalKind.DigitalInput : selected.Value;
            }
        }

        private string SelectedDataType
        {
            get { return DataTypeComboBox.SelectedItem as string ?? string.Empty; }
        }

        private void Cpu_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            RebuildConfiguredChannels();
            UpdateAddress();
        }

        private void Signal_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            _updating = true;
            RebuildDataTypes(null);
            _updating = false;
            RebuildConfiguredChannels();
            UpdateAddress();
        }

        private void AddressPart_Changed(object sender, EventArgs e)
        {
            if (!_updating)
            {
                UpdateAddress();
            }
        }

        private void ConfiguredChannel_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            var selected = ConfiguredChannelComboBox.SelectedItem as PlcAddressChannelChoice;
            if (selected != null)
            {
                _updating = true;
                SetConfiguredChannelDataType(selected.DataType);
                _updating = false;
            }

            UpdateAddress();
        }

        private void RebuildDataTypes(string preferred)
        {
            var allowed = PlcAddressing.GetAllowedDataTypes(SelectedSignal).ToList();
            var selected = allowed.FirstOrDefault(value =>
                string.Equals(value, preferred, StringComparison.OrdinalIgnoreCase));
            DataTypeComboBox.ItemsSource = allowed;
            DataTypeComboBox.SelectedItem = selected ?? allowed.First();
            BitOffsetComboBox.IsEnabled =
                string.Equals(DataTypeComboBox.SelectedItem as string, "Bool", StringComparison.Ordinal);
            if (!BitOffsetComboBox.IsEnabled)
            {
                BitOffsetComboBox.SelectedItem = null;
            }
            else if (BitOffsetComboBox.SelectedItem == null)
            {
                BitOffsetComboBox.SelectedItem = 0;
            }
        }

        private void RebuildConfiguredChannels()
        {
            var useCatalog = _restrictHardwareIo && SelectedSignal != PlcSignalKind.Memory;
            ConfiguredChannelPanel.Visibility = useCatalog ? Visibility.Visible : Visibility.Collapsed;
            ManualAddressPanel.Visibility = useCatalog ? Visibility.Collapsed : Visibility.Visible;
            if (!useCatalog)
            {
                DataTypeComboBox.IsEnabled = true;
                return;
            }

            _updating = true;
            var candidates = _configuredChannels
                .Where(channel => channel.SignalKind == SelectedSignal)
                .OrderBy(channel => channel.Address, StringComparer.OrdinalIgnoreCase)
                .ThenBy(channel => channel.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ConfiguredChannelComboBox.ItemsSource = candidates;
            var selectedChannel = candidates.FirstOrDefault(channel =>
                string.Equals(
                    NormalizeAddressForComparison(channel.Address),
                    NormalizeAddressForComparison(_preferredChannelAddress),
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    channel.DataType,
                    _preferredChannelDataType,
                    StringComparison.OrdinalIgnoreCase));
            if (selectedChannel == null && _selectFirstConfiguredChannel && candidates.Count > 0)
            {
                selectedChannel = candidates[0];
            }

            ConfiguredChannelComboBox.SelectedItem = selectedChannel;
            if (selectedChannel != null)
            {
                SetConfiguredChannelDataType(selectedChannel.DataType);
            }
            else
            {
                DataTypeComboBox.ItemsSource = new string[0];
                DataTypeComboBox.SelectedIndex = -1;
                DataTypeComboBox.IsEnabled = false;
            }
            _updating = false;
        }

        private void UpdateAddress()
        {
            string address;
            string error;
            if (_restrictHardwareIo && SelectedSignal != PlcSignalKind.Memory)
            {
                var channel = ConfiguredChannelComboBox.SelectedItem as PlcAddressChannelChoice;
                if (channel == null)
                {
                    address = string.Empty;
                    error = "Для выбранного сигнала нет доступных каналов PLC.";
                }
                else
                {
                    PlcAddressSpec spec;
                    if (!PlcAddressing.TryParse(
                        SelectedCpu,
                        channel.Address,
                        SelectedDataType,
                        out spec,
                        out address,
                        out error))
                    {
                        address = string.Empty;
                    }
                }
            }
            else
            {
                int byteOffset;
                if (!int.TryParse(
                    (ByteOffsetTextBox.Text ?? string.Empty).Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out byteOffset))
                {
                    address = string.Empty;
                    error = "Смещение байта должно быть целым числом.";
                }
                else
                {
                    int? bitOffset = string.Equals(SelectedDataType, "Bool", StringComparison.Ordinal)
                        ? BitOffsetComboBox.SelectedItem as int?
                        : null;
                    PlcAddressing.TryBuild(
                        SelectedCpu,
                        SelectedSignal,
                        SelectedDataType,
                        byteOffset,
                        bitOffset,
                        out address,
                        out error);
                }
            }

            _canonicalAddress = address ?? string.Empty;
            _validationError = error ?? string.Empty;
            AddressPreviewTextBox.Text = _canonicalAddress;
            AddressValidationText.Text = string.IsNullOrEmpty(_validationError)
                ? "Адрес корректен"
                : _validationError;
            AddressValidationText.Foreground = FindResource(
                string.IsNullOrEmpty(_validationError) ? "SuccessBrush" : "ErrorBrush") as Brush;

            if (_restrictHardwareIo && SelectedSignal != PlcSignalKind.Memory)
            {
                var channel = ConfiguredChannelComboBox.SelectedItem as PlcAddressChannelChoice;
                RangeHintText.Text = channel == null
                    ? "Выберите реально сконфигурированный канал PLC."
                    : "Аппаратный канал: " + channel.DisplayName;
            }
            else if (SelectedCpu == PlcCpuFamily.S71200 || SelectedCpu == PlcCpuFamily.S71500)
            {
                var maximum = PlcAddressing.GetMaximumByteOffset(
                    SelectedCpu,
                    SelectedSignal,
                    SelectedDataType);
                RangeHintText.Text = "Диапазон байта: 0.." +
                                     maximum.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                RangeHintText.Text = "Legacy-профиль: проверяется синтаксис без диапазона CPU.";
            }

            var handler = ValueChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void SelectCpu(PlcCpuFamily value)
        {
            CpuComboBox.SelectedItem = CpuComboBox.Items
                .OfType<Choice<PlcCpuFamily>>()
                .First(item => item.Value == value);
        }

        private void SetConfiguredChannelDataType(string dataType)
        {
            var normalized = PlcAddressing.GetAllowedDataTypes(SelectedSignal)
                .FirstOrDefault(value => string.Equals(value, dataType, StringComparison.OrdinalIgnoreCase));
            DataTypeComboBox.ItemsSource = normalized == null
                ? new string[0]
                : new[] { normalized };
            DataTypeComboBox.SelectedIndex = normalized == null ? -1 : 0;
            DataTypeComboBox.IsEnabled = false;
            BitOffsetComboBox.IsEnabled = string.Equals(normalized, "Bool", StringComparison.Ordinal);
        }

        private static string NormalizeAddressForComparison(string address)
        {
            var normalized = (address ?? string.Empty).Trim();
            return normalized.StartsWith("%", StringComparison.Ordinal)
                ? normalized.Substring(1)
                : normalized;
        }

        private void SelectSignal(PlcSignalKind value)
        {
            SignalComboBox.SelectedItem = SignalComboBox.Items
                .OfType<Choice<PlcSignalKind>>()
                .First(item => item.Value == value);
        }

        private sealed class Choice<T>
        {
            internal Choice(T value, string label)
            {
                Value = value;
                Label = label;
            }

            internal T Value { get; private set; }

            internal string Label { get; private set; }

            public override string ToString()
            {
                return Label;
            }
        }
    }
}
