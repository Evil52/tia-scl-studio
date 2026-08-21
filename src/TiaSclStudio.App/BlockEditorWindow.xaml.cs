using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TiaSclStudio.Core.Model;
using TiaSclStudio.Core.Validation;

namespace TiaSclStudio.App
{
    public partial class BlockEditorWindow : Window
    {
        private readonly BlockDefinition _workingCopy;
        private readonly PlantProject _plant;
        private readonly ObservableCollection<InterfaceMember> _members;

        public BlockEditorWindow(
            BlockDefinition source,
            PlantProject plant,
            bool isNew)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            InitializeComponent();

            _workingCopy = CloneBlock(source);
            _plant = plant ?? new PlantProject();
            CanonicalizeKnownTypes(_workingCopy, _plant.DataTypes);
            _members = new ObservableCollection<InterfaceMember>(
                _workingCopy.Interface.Select(CloneMember));

            SectionColumn.ItemsSource = Enum.GetValues(typeof(InterfaceSection));
            var memberTypes = BuildTypeChoices(false, _workingCopy, _plant.DataTypes);
            var returnTypes = BuildTypeChoices(true, _workingCopy, _plant.DataTypes);
            DataTypeColumn.ItemsSource = memberTypes;
            ReturnTypeComboBox.ItemsSource = returnTypes;
            InterfaceGrid.ItemsSource = _members;

            EditorTitleText.Text = isNew ? "НОВЫЙ БЛОК" : "РЕДАКТОР БЛОКА";
            Title = isNew ? "Новый блок" : "Редактор блока — " + _workingCopy.Name;
            BlockIdText.Text = "ID " + _workingCopy.Id.ToString("D");
            NameTextBox.Text = _workingCopy.Name;
            KindComboBox.SelectedIndex = _workingCopy.Kind == BlockKind.Function ? 1 : 0;
            ReturnTypeComboBox.SelectedItem = string.IsNullOrWhiteSpace(_workingCopy.ReturnType)
                ? "Void"
                : _workingCopy.ReturnType;
            VersionTextBox.Text = _workingCopy.Version;
            DescriptionTextBox.Text = _workingCopy.Description;
            OptimizedAccessCheckBox.IsChecked = _workingCopy.OptimizedAccess;
            ImportedInterfaceOnlyCheckBox.IsChecked = _workingCopy.ImportedInterfaceOnly;
            SclBodyTextBox.Text = _workingCopy.SclBody;
            UpdateKindState();
        }

        public BlockDefinition EditedBlock { get; private set; }

        private void KindComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateKindState();
        }

        private void UpdateKindState()
        {
            if (ReturnTypeComboBox == null || KindComboBox == null)
            {
                return;
            }

            var isFunction = KindComboBox.SelectedIndex == 1;
            ReturnTypeComboBox.IsEnabled = isFunction;
            ReturnTypeComboBox.ToolTip = isFunction
                ? "Тип результата FC: базовый тип или UDT. Выберите Void, если значение не возвращается."
                : "FB не возвращает значение.";
        }

        private void AddMember_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();
            var member = new InterfaceMember
            {
                Section = InterfaceSection.Input,
                Name = CreateUniqueMemberName(),
                DataType = "Bool"
            };
            _members.Add(member);
            InterfaceGrid.SelectedItem = member;
            InterfaceGrid.ScrollIntoView(member);
            InterfaceGrid.Focus();
        }

        private void DeleteMember_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();
            var member = InterfaceGrid.SelectedItem as InterfaceMember;
            if (member == null)
            {
                MessageBox.Show(
                    this,
                    "Сначала выберите строку интерфейса.",
                    "Редактор блока",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var index = _members.IndexOf(member);
            _members.Remove(member);
            if (_members.Count > 0)
            {
                InterfaceGrid.SelectedIndex = Math.Min(index, _members.Count - 1);
            }
        }

        private void MoveMemberUp_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedMember(-1);
        }

        private void MoveMemberDown_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedMember(1);
        }

        private void MoveSelectedMember(int offset)
        {
            CommitGridEdit();
            var member = InterfaceGrid.SelectedItem as InterfaceMember;
            if (member == null)
            {
                return;
            }

            var oldIndex = _members.IndexOf(member);
            var newIndex = oldIndex + offset;
            if (newIndex < 0 || newIndex >= _members.Count)
            {
                return;
            }

            _members.Move(oldIndex, newIndex);
            InterfaceGrid.SelectedItem = member;
            InterfaceGrid.ScrollIntoView(member);
        }

        private string CreateUniqueMemberName()
        {
            var usedNames = new HashSet<string>(
                _members.Select(member => member.Name),
                StringComparer.OrdinalIgnoreCase);
            var sequence = 1;
            string candidate;
            do
            {
                candidate = "Parameter" + sequence;
                sequence++;
            }
            while (usedNames.Contains(candidate));

            return candidate;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();
            ApplyControlsToWorkingCopy();

            var validationMessages = ValidateWorkingCopy();
            if (validationMessages.Count > 0)
            {
                ValidationHintText.Text = "Исправьте ошибки перед сохранением.";
                ValidationHintText.Foreground = FindResource("ErrorBrush") as System.Windows.Media.Brush;
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, validationMessages),
                    "Блок не прошёл проверку",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            EditedBlock = CloneBlock(_workingCopy);
            DialogResult = true;
        }

        private void CommitGridEdit()
        {
            InterfaceGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            InterfaceGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void ApplyControlsToWorkingCopy()
        {
            _workingCopy.Name = (NameTextBox.Text ?? string.Empty).Trim();
            _workingCopy.Kind = KindComboBox.SelectedIndex == 1
                ? BlockKind.Function
                : BlockKind.FunctionBlock;
            _workingCopy.ReturnType = _workingCopy.Kind == BlockKind.Function
                ? ((ReturnTypeComboBox.SelectedItem as string) ?? string.Empty).Trim()
                : "Void";
            _workingCopy.Version = (VersionTextBox.Text ?? string.Empty).Trim();
            _workingCopy.Description = DescriptionTextBox.Text ?? string.Empty;
            _workingCopy.OptimizedAccess = OptimizedAccessCheckBox.IsChecked == true;
            _workingCopy.ImportedInterfaceOnly = ImportedInterfaceOnlyCheckBox.IsChecked == true;
            _workingCopy.SclBody = SclBodyTextBox.Text ?? string.Empty;
            _workingCopy.Interface = _members.Select(CloneMember).ToList();
        }

        private IList<string> ValidateWorkingCopy()
        {
            var messages = new List<string>();
            var projectBlocks = _plant.Blocks ?? new List<BlockDefinition>();
            var duplicate = projectBlocks.FirstOrDefault(block =>
                block != null &&
                block.Id != _workingCopy.Id &&
                string.Equals(block.Name, _workingCopy.Name, StringComparison.OrdinalIgnoreCase));
            if (duplicate != null)
            {
                messages.Add("• Блок с именем «" + _workingCopy.Name + "» уже существует в библиотеке.");
            }

            var temporaryProject = new PlantProject
            {
                Id = _plant.Id,
                Name = _plant.Name ?? "BlockEditorValidation",
                CpuFamily = _plant.CpuFamily,
                Blocks = projectBlocks
                    .Where(block => block != null && block.Id != _workingCopy.Id)
                    .Select(CloneBlock)
                    .ToList(),
                DataTypes = new List<UdtDefinition>(_plant.DataTypes ?? new List<UdtDefinition>()),
                Tags = new List<TagDefinition>(_plant.Tags ?? new List<TagDefinition>()),
                CallBlocks = new List<CallBlockDefinition>(_plant.CallBlocks ?? new List<CallBlockDefinition>())
            };
            temporaryProject.Blocks.Add(CloneBlock(_workingCopy));

            var result = new ProjectValidator().Validate(temporaryProject);
            foreach (var issue in result.Errors)
            {
                if (issue.Code == "DUPLICATE_NAME" &&
                    string.Equals(issue.Path, "Blocks", StringComparison.Ordinal))
                {
                    continue;
                }

                messages.Add("• " + TranslateValidationIssue(issue));
            }

            return messages.Distinct(StringComparer.Ordinal).ToList();
        }

        private static string TranslateValidationIssue(ValidationIssue issue)
        {
            string text;
            switch (issue.Code)
            {
                case "INVALID_NAME":
                    text = "Недопустимое имя PLC. Используйте корректный SCL-идентификатор.";
                    break;
                case "INVALID_VERSION":
                    text = "Версия должна иметь формат «число.число», например 0.1 или 1.0.";
                    break;
                case "RETURN_TYPE_REQUIRED":
                    text = "Для FC необходимо указать возвращаемый тип; используйте Void, если результата нет.";
                    break;
                case "DATA_TYPE_REQUIRED":
                    text = "Для каждого элемента интерфейса необходимо указать тип данных.";
                    break;
                case "UNKNOWN_DATA_TYPE":
                    text = "Выберите базовый тип TIA или существующий пользовательский UDT из списка.";
                    break;
                case "UDT_REFERENCE_CYCLE":
                    text = "Пользовательские типы не должны образовывать циклические ссылки.";
                    break;
                case "DUPLICATE_NAME":
                    text = "Имена элементов интерфейса не должны повторяться.";
                    break;
                case "NAME_COLLISION":
                    text = "Имя блока конфликтует с другим глобальным символом проекта.";
                    break;
                case "FC_STATIC_NOT_ALLOWED":
                    text = "Секция Static допустима только для FB.";
                    break;
                case "INVALID_INTERFACE_SECTION":
                    text = "Выбрана неподдерживаемая секция интерфейса.";
                    break;
                case "INVALID_BLOCK_KIND":
                    text = "Выбран неподдерживаемый тип блока.";
                    break;
                case "EMPTY_ID":
                case "DUPLICATE_ID":
                    text = "Нарушена целостность идентификаторов модели.";
                    break;
                default:
                    text = "Ошибка модели «" + issue.Code + "».";
                    break;
            }

            return text + " Поле: " + LocalizePath(issue.Path);
        }

        private static string LocalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? "блок" : path;
        }

        internal static BlockDefinition CloneBlock(BlockDefinition source)
        {
            var clone = new BlockDefinition
            {
                Id = source.Id,
                Name = source.Name ?? string.Empty,
                Kind = source.Kind,
                ReturnType = source.ReturnType ?? string.Empty,
                Version = source.Version ?? string.Empty,
                OptimizedAccess = source.OptimizedAccess,
                ImportedInterfaceOnly = source.ImportedInterfaceOnly,
                Description = source.Description ?? string.Empty,
                SclBody = source.SclBody ?? string.Empty,
                Interface = new List<InterfaceMember>()
            };

            foreach (var member in source.Interface ?? new List<InterfaceMember>())
            {
                clone.Interface.Add(CloneMember(member));
            }

            return clone;
        }

        private static InterfaceMember CloneMember(InterfaceMember source)
        {
            return new InterfaceMember
            {
                Id = source.Id,
                Name = source.Name ?? string.Empty,
                DataType = source.DataType ?? string.Empty,
                Section = source.Section,
                InitialValue = source.InitialValue ?? string.Empty,
                Comment = source.Comment ?? string.Empty
            };
        }

        private static IList<string> BuildTypeChoices(
            bool includeVoid,
            BlockDefinition block,
            IEnumerable<UdtDefinition> dataTypes)
        {
            var choices = PlcDataTypes.GetSelectableTypes(dataTypes, includeVoid).ToList();
            var existing = (block.Interface ?? new List<InterfaceMember>())
                .Select(member => member == null ? string.Empty : member.DataType)
                .Concat(new[] { block.ReturnType });
            foreach (var value in existing)
            {
                string canonical;
                Guid udtId;
                if (PlcDataTypes.TryResolve(value, dataTypes, includeVoid, out canonical, out udtId) &&
                    !choices.Contains(canonical, StringComparer.OrdinalIgnoreCase))
                {
                    choices.Add(canonical);
                }
            }

            return choices;
        }

        private static void CanonicalizeKnownTypes(
            BlockDefinition block,
            IEnumerable<UdtDefinition> dataTypes)
        {
            string canonical;
            Guid udtId;
            if (PlcDataTypes.TryResolve(block.ReturnType, dataTypes, true, out canonical, out udtId))
            {
                block.ReturnType = canonical;
            }

            foreach (var member in block.Interface ?? new List<InterfaceMember>())
            {
                if (member != null &&
                    PlcDataTypes.TryResolve(member.DataType, dataTypes, false, out canonical, out udtId))
                {
                    member.DataType = canonical;
                }
            }
        }
    }
}
