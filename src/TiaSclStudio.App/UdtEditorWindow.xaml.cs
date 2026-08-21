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
    public partial class UdtEditorWindow : Window
    {
        private readonly UdtDefinition _workingCopy;
        private readonly List<UdtDefinition> _allDataTypes;
        private readonly ObservableCollection<UdtMember> _members;

        public UdtEditorWindow(
            UdtDefinition source,
            IEnumerable<UdtDefinition> allDataTypes,
            bool isNew)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            InitializeComponent();
            _workingCopy = CloneDataType(source);
            _allDataTypes = (allDataTypes ?? new UdtDefinition[0])
                .Where(item => item != null)
                .Select(CloneDataType)
                .ToList();
            if (_allDataTypes.All(item => item.Id != _workingCopy.Id))
            {
                _allDataTypes.Add(CloneDataType(_workingCopy));
            }

            CanonicalizeKnownMemberTypes(_workingCopy, _allDataTypes);
            _members = new ObservableCollection<UdtMember>(
                (_workingCopy.Members ?? new List<UdtMember>()).Select(CloneMember));

            var selectableUdts = _allDataTypes.Where(item => item.Id != _workingCopy.Id);
            var choices = PlcDataTypes.GetSelectableTypes(selectableUdts, false).ToList();
            foreach (var member in _members)
            {
                string canonical;
                Guid udtId;
                if (PlcDataTypes.TryResolve(
                        member.DataType,
                        selectableUdts,
                        false,
                        out canonical,
                        out udtId) &&
                    !choices.Contains(canonical, StringComparer.OrdinalIgnoreCase))
                {
                    choices.Add(canonical);
                }
            }

            MemberDataTypeColumn.ItemsSource = choices;
            MembersGrid.ItemsSource = _members;
            EditorTitleText.Text = isNew ? "НОВЫЙ ПОЛЬЗОВАТЕЛЬСКИЙ ТИП" : "РЕДАКТОР ПОЛЬЗОВАТЕЛЬСКОГО ТИПА";
            Title = isNew ? "Новый UDT" : "Редактор UDT — " + _workingCopy.Name;
            UdtIdText.Text = "ID " + _workingCopy.Id.ToString("D");
            NameTextBox.Text = _workingCopy.Name;
            VersionTextBox.Text = _workingCopy.Version;
            CommentTextBox.Text = _workingCopy.Comment;
        }

        public UdtDefinition EditedDataType { get; private set; }

        private void AddMember_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();
            var member = new UdtMember(CreateUniqueMemberName(), "Bool");
            _members.Add(member);
            MembersGrid.SelectedItem = member;
            MembersGrid.ScrollIntoView(member);
            MembersGrid.Focus();
        }

        private void DeleteMember_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();
            var member = MembersGrid.SelectedItem as UdtMember;
            if (member == null)
            {
                MessageBox.Show(
                    this,
                    "Сначала выберите поле UDT.",
                    "Редактор UDT",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var index = _members.IndexOf(member);
            _members.Remove(member);
            if (_members.Count > 0)
            {
                MembersGrid.SelectedIndex = Math.Min(index, _members.Count - 1);
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
            var member = MembersGrid.SelectedItem as UdtMember;
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
            MembersGrid.SelectedItem = member;
            MembersGrid.ScrollIntoView(member);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            CommitGridEdit();
            _workingCopy.Name = (NameTextBox.Text ?? string.Empty).Trim();
            _workingCopy.Version = (VersionTextBox.Text ?? string.Empty).Trim();
            _workingCopy.Comment = CommentTextBox.Text ?? string.Empty;
            _workingCopy.Members = _members.Select(CloneMember).ToList();

            var validationMessages = ValidateWorkingCopy();
            if (validationMessages.Count > 0)
            {
                ValidationHintText.Text = "Исправьте ошибки перед сохранением.";
                ValidationHintText.Foreground = FindResource("ErrorBrush") as System.Windows.Media.Brush;
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, validationMessages),
                    "UDT не прошёл проверку",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            EditedDataType = CloneDataType(_workingCopy);
            DialogResult = true;
        }

        private IList<string> ValidateWorkingCopy()
        {
            var candidateTypes = _allDataTypes
                .Where(item => item.Id != _workingCopy.Id)
                .Select(CloneDataType)
                .ToList();
            if (!string.Equals(
                    _allDataTypes.FirstOrDefault(item => item.Id == _workingCopy.Id)?.Name,
                    _workingCopy.Name,
                    StringComparison.Ordinal))
            {
                var oldName = _allDataTypes.FirstOrDefault(item => item.Id == _workingCopy.Id)?.Name;
                if (SclName.IsValid(oldName) && SclName.IsValid(_workingCopy.Name))
                {
                    RewriteDataTypeMembers(candidateTypes, oldName, _workingCopy.Name);
                }
            }

            candidateTypes.Add(CloneDataType(_workingCopy));
            var temporaryProject = new PlantProject
            {
                Name = "UdtEditorValidation",
                DataTypes = candidateTypes
            };
            var result = new ProjectValidator().Validate(temporaryProject);
            return result.Errors
                .Where(issue => issue.Path.StartsWith("DataTypes", StringComparison.Ordinal))
                .Select(issue => "• " + TranslateValidationIssue(issue))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string TranslateValidationIssue(ValidationIssue issue)
        {
            switch (issue.Code)
            {
                case "INVALID_NAME":
                    return "Недопустимое имя UDT или его поля.";
                case "INVALID_VERSION":
                    return "Версия должна иметь формат «число.число», например 0.1.";
                case "DUPLICATE_NAME":
                    return "Имя UDT или поля повторяется без учёта регистра.";
                case "UNKNOWN_DATA_TYPE":
                    return "Выберите базовый тип TIA или существующий UDT из списка.";
                case "UDT_REFERENCE_CYCLE":
                    return "Ссылки между UDT образуют цикл.";
                case "EMPTY_ID":
                case "DUPLICATE_ID":
                    return "Нарушена целостность стабильных идентификаторов UDT.";
                case "CONTROL_CHARACTER":
                    return "Однострочное поле содержит запрещённый управляющий символ.";
                default:
                    return "Ошибка модели «" + issue.Code + "» (" + issue.Path + ").";
            }
        }

        private void CommitGridEdit()
        {
            MembersGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            MembersGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private string CreateUniqueMemberName()
        {
            var names = new HashSet<string>(
                _members.Select(item => item.Name),
                StringComparer.OrdinalIgnoreCase);
            var sequence = 1;
            string candidate;
            do
            {
                candidate = "Member" + sequence;
                sequence++;
            }
            while (names.Contains(candidate));
            return candidate;
        }

        internal static UdtDefinition CloneDataType(UdtDefinition source)
        {
            return new UdtDefinition
            {
                Id = source.Id,
                Name = source.Name ?? string.Empty,
                Version = source.Version ?? string.Empty,
                Comment = source.Comment ?? string.Empty,
                Members = (source.Members ?? new List<UdtMember>()).Select(CloneMember).ToList()
            };
        }

        private static UdtMember CloneMember(UdtMember source)
        {
            return new UdtMember
            {
                Id = source.Id,
                Name = source.Name ?? string.Empty,
                DataType = source.DataType ?? string.Empty,
                InitialValue = source.InitialValue ?? string.Empty,
                Comment = source.Comment ?? string.Empty
            };
        }

        private static void CanonicalizeKnownMemberTypes(
            UdtDefinition dataType,
            IEnumerable<UdtDefinition> allDataTypes)
        {
            foreach (var member in dataType.Members ?? new List<UdtMember>())
            {
                string canonical;
                Guid udtId;
                if (member != null && PlcDataTypes.TryResolve(
                        member.DataType,
                        allDataTypes,
                        false,
                        out canonical,
                        out udtId))
                {
                    member.DataType = canonical;
                }
            }
        }

        private static void RewriteDataTypeMembers(
            IEnumerable<UdtDefinition> dataTypes,
            string oldName,
            string newName)
        {
            foreach (var dataType in dataTypes)
            {
                foreach (var member in dataType.Members ?? new List<UdtMember>())
                {
                    member.DataType = PlcDataTypes.RewriteUdtReference(
                        member.DataType,
                        oldName,
                        newName);
                }
            }
        }
    }
}
