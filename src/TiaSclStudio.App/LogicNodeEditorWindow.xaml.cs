using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    public partial class LogicNodeEditorWindow : Window
    {
        private static readonly IList<OperationChoice> Choices = new[]
        {
            new OperationChoice(LogicOperation.And, "AND — логическое И"),
            new OperationChoice(LogicOperation.Or, "OR — логическое ИЛИ"),
            new OperationChoice(LogicOperation.Not, "NOT — логическое НЕ"),
            new OperationChoice(LogicOperation.GreaterThan, "> — больше"),
            new OperationChoice(LogicOperation.LessThan, "< — меньше"),
            new OperationChoice(LogicOperation.Equal, "= — равно")
        };

        private readonly DiagramProject _project;
        private readonly LogicNode _source;

        public LogicNodeEditorWindow(LogicNode source, DiagramProject project)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            _source = source;
            _project = project;
            InitializeComponent();
            OperationComboBox.ItemsSource = Choices;
            OperationComboBox.DisplayMemberPath = "Caption";
            OperationComboBox.SelectedItem = Choices.FirstOrDefault(
                item => item.Operation == source.Operation) ?? Choices[0];
            OperandDataTypeTextBox.Text = LogicNodeEditingLogic.GetOperandDataType(source);
            UpdateOperandTypeState();
        }

        internal LogicNodeEditResult Result { get; private set; }

        private void OperationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateOperandTypeState();
        }

        private void UpdateOperandTypeState()
        {
            if (OperandDataTypeTextBox == null)
            {
                return;
            }

            var choice = OperationComboBox.SelectedItem as OperationChoice;
            var isBoolean = choice != null && LogicNodeEditingLogic.IsBooleanOperation(choice.Operation);
            OperandDataTypeTextBox.IsEnabled = !isBoolean;
            if (isBoolean)
            {
                OperandDataTypeTextBox.Text = "Bool";
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var choice = OperationComboBox.SelectedItem as OperationChoice;
            if (choice == null)
            {
                ValidationText.Text = "Выберите операцию.";
                ValidationText.Foreground = FindResource("ErrorBrush") as System.Windows.Media.Brush;
                return;
            }

            var candidate = new LogicNodeEditResult(
                _source.Id,
                choice.Operation,
                OperandDataTypeTextBox.Text);
            var errors = LogicNodeEditingLogic.ValidateCandidate(_project, candidate);
            if (errors.Count > 0)
            {
                ValidationText.Text = string.Join(Environment.NewLine, errors);
                ValidationText.Foreground = FindResource("ErrorBrush") as System.Windows.Media.Brush;
                return;
            }

            Result = candidate;
            DialogResult = true;
        }

        private sealed class OperationChoice
        {
            internal OperationChoice(LogicOperation operation, string caption)
            {
                Operation = operation;
                Caption = caption;
            }

            internal LogicOperation Operation { get; private set; }

            public string Caption { get; private set; }
        }
    }
}
