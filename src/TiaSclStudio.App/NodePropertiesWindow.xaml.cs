using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    public partial class NodePropertiesWindow : Window
    {
        private readonly DiagramProject _project;
        private readonly CallNode _workingNode;
        private readonly TagDefinition _workingTag;
        private readonly bool _blockSupportsMultiInstance;
        private readonly IList<PlcAddressChannelChoice> _availableChannels;
        private readonly bool _restrictHardwareIo;
        private readonly PlcCpuFamily? _fixedCpuFamily;

        public NodePropertiesWindow(CallNode source, DiagramProject project)
            : this(source, project, null, false, null)
        {
        }

        internal NodePropertiesWindow(
            CallNode source,
            DiagramProject project,
            IEnumerable<PlcAddressChannelChoice> availableChannels,
            bool restrictHardwareIo,
            PlcCpuFamily? fixedCpuFamily)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            _project = project;
            _availableChannels = (availableChannels ?? Enumerable.Empty<PlcAddressChannelChoice>()).ToList();
            _restrictHardwareIo = restrictHardwareIo;
            _fixedCpuFamily = fixedCpuFamily;
            _workingNode = NodeEditingLogic.CloneSupportedNode(source);

            var sourceTag = _workingNode as TagNode;
            if (sourceTag != null)
            {
                var tagDefinitions = ((project.Plant ?? new PlantProject()).Tags ?? new List<TagDefinition>())
                    .Where(tag => tag != null && tag.Id == sourceTag.TagDefinitionId)
                    .ToList();
                if (tagDefinitions.Count == 1)
                {
                    _workingTag = NodeEditingLogic.CloneTag(tagDefinitions[0]);
                }
                else
                {
                    _workingTag = new TagDefinition
                    {
                        Id = sourceTag.TagDefinitionId,
                        Name = sourceTag.TagName ?? string.Empty,
                        DataType = sourceTag.DataType ?? string.Empty,
                        Address = string.Empty,
                        Comment = string.Empty
                    };
                }
            }

            var sourceBlock = _workingNode as BlockCallNode;
            if (sourceBlock != null)
            {
                var definitions = ((project.Plant ?? new PlantProject()).Blocks ?? new List<BlockDefinition>())
                    .Where(block => block != null && block.Id == sourceBlock.BlockDefinitionId)
                    .ToList();
                _blockSupportsMultiInstance =
                    definitions.Count == 1 && definitions[0].Kind == BlockKind.FunctionBlock;
            }

            InitializeComponent();
            PopulateControls();
        }

        /// <summary>
        /// Immutable values approved by the user. The project is still untouched;
        /// the caller applies this result explicitly through NodeEditingLogic.
        /// </summary>
        public NodeEditResult Result { get; private set; }

        private void PopulateControls()
        {
            NodeIdText.Text = "ID " + _workingNode.Id.ToString("D");

            var block = _workingNode as BlockCallNode;
            if (block != null)
            {
                PopulateBlockCall(block);
                return;
            }

            var constant = _workingNode as ConstantNode;
            if (constant != null)
            {
                PopulateConstant(constant);
                return;
            }

            PopulateTag((TagNode)_workingNode);
        }

        private void PopulateBlockCall(BlockCallNode node)
        {
            EditorTitleText.Text = "СВОЙСТВА ВЫЗОВА БЛОКА";
            NodeKindText.Text = "BLOCK CALL";
            BlockCallPanel.Visibility = Visibility.Visible;
            InstanceNameTextBox.Text = node.InstanceName ?? string.Empty;

            var definitions = ((_project.Plant ?? new PlantProject()).Blocks ?? new List<BlockDefinition>())
                .Where(block => block != null && block.Id == node.BlockDefinitionId)
                .ToList();
            if (definitions.Count == 1)
            {
                var definition = definitions[0];
                var prefix = definition.Kind == BlockKind.FunctionBlock ? "FB" : "FC";
                ObjectSummaryText.Text = prefix + "  " + definition.Name;
                Title = "Свойства вызова — " + definition.Name;
            }
            else
            {
                ObjectSummaryText.Text = "Блок " + node.BlockDefinitionId.ToString("D");
                ShowLoadWarning(
                    definitions.Count == 0
                        ? "Связанный библиотечный блок не найден. Сохранение будет заблокировано."
                        : "Идентификатор библиотечного блока не уникален. Сохранение будет заблокировано.");
            }

            UseMultiInstanceCheckBox.IsChecked = _blockSupportsMultiInstance && node.UseMultiInstance;
            UseMultiInstanceCheckBox.IsEnabled = _blockSupportsMultiInstance;
            MultiInstanceHintText.Text = _blockSupportsMultiInstance
                ? "Включённый режим хранит экземпляр внутри DB листа вызовов."
                : "FC не имеет instance DB, поэтому multi-instance недоступен.";

            Loaded += delegate { InstanceNameTextBox.Focus(); InstanceNameTextBox.SelectAll(); };
        }

        private void PopulateConstant(ConstantNode node)
        {
            EditorTitleText.Text = "СВОЙСТВА КОНСТАНТЫ";
            NodeKindText.Text = "CONST";
            ObjectSummaryText.Text = (node.Literal ?? string.Empty) + "  ·  " + (node.DataType ?? string.Empty);
            Title = "Свойства константы";
            ConstantPanel.Visibility = Visibility.Visible;
            LiteralTextBox.Text = node.Literal ?? string.Empty;
            ConstantDataTypeTextBox.Text = node.DataType ?? string.Empty;
            Loaded += delegate { LiteralTextBox.Focus(); LiteralTextBox.SelectAll(); };
        }

        private void PopulateTag(TagNode node)
        {
            EditorTitleText.Text = "СВОЙСТВА PLC-ТЕГА";
            NodeKindText.Text = node.TerminalDirection == TerminalDirection.Source
                ? "TAG · SOURCE"
                : "TAG · SINK";
            ObjectSummaryText.Text = _workingTag.Name + "  ·  " + _workingTag.DataType;
            Title = "Свойства PLC-тега — " + _workingTag.Name;
            TagPanel.Visibility = Visibility.Visible;
            TagNameTextBox.Text = _workingTag.Name;
            TagAddressEditor.LoadValue(
                _fixedCpuFamily ?? (_project.Plant ?? new PlantProject()).CpuFamily,
                _workingTag.DataType,
                _workingTag.Address);
            TagAddressEditor.ConfigureAvailableChannels(
                _availableChannels,
                _restrictHardwareIo,
                _workingTag.Address,
                _workingTag.DataType);
            // Online hardware discovery fixes the exact CPU. Offline edits may
            // change it explicitly; NodeEditingLogic revalidates every stored tag
            // before committing the project-wide profile change.
            TagAddressEditor.IsCpuSelectionEnabled = !_fixedCpuFamily.HasValue;
            TagCommentTextBox.Text = _workingTag.Comment;
            TerminalDirectionTextBox.Text = node.TerminalDirection == TerminalDirection.Source
                ? "Источник (выход)"
                : "Приёмник (вход)";

            var tagCount = ((_project.Plant ?? new PlantProject()).Tags ?? new List<TagDefinition>())
                .Count(tag => tag != null && tag.Id == node.TagDefinitionId);
            if (tagCount != 1)
            {
                ShowLoadWarning(
                    tagCount == 0
                        ? "Связанный PLC-тег не найден. Сохранение будет заблокировано."
                        : "Идентификатор PLC-тега не уникален. Сохранение будет заблокировано.");
            }

            Loaded += delegate { TagNameTextBox.Focus(); TagNameTextBox.SelectAll(); };
        }

        private void ShowLoadWarning(string message)
        {
            LoadWarningText.Text = message ?? string.Empty;
            LoadWarningText.Visibility = Visibility.Visible;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var candidate = BuildResultFromControls();
            var errors = NodeEditingLogic.ValidateCandidate(_project, candidate);
            if (errors.Count > 0)
            {
                ValidationHintText.Text = "Исправьте ошибки перед сохранением. Исходная модель не изменена.";
                ValidationHintText.Foreground = FindResource("ErrorBrush") as System.Windows.Media.Brush;
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, errors),
                    "Узел не прошёл проверку",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Result = candidate;
            DialogResult = true;
        }

        private NodeEditResult BuildResultFromControls()
        {
            var block = _workingNode as BlockCallNode;
            if (block != null)
            {
                return NodeEditingLogic.CreateBlockCallResult(
                    block,
                    InstanceNameTextBox.Text,
                    _blockSupportsMultiInstance && UseMultiInstanceCheckBox.IsChecked == true);
            }

            var constant = _workingNode as ConstantNode;
            if (constant != null)
            {
                return NodeEditingLogic.CreateConstantResult(
                    constant,
                    LiteralTextBox.Text,
                    ConstantDataTypeTextBox.Text);
            }

            _workingTag.Name = TagNameTextBox.Text ?? string.Empty;
            PlcCpuFamily cpuFamily;
            string dataType;
            string address;
            string addressError;
            TagAddressEditor.TryGetValue(
                out cpuFamily,
                out dataType,
                out address,
                out addressError);
            _workingTag.DataType = dataType;
            _workingTag.Address = address;
            _workingTag.Comment = TagCommentTextBox.Text ?? string.Empty;
            return NodeEditingLogic.CreateTagResult(
                (TagNode)_workingNode,
                _workingTag,
                cpuFamily);
        }
    }
}
