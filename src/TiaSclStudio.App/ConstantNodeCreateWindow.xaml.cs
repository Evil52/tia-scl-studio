using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    public partial class ConstantNodeCreateWindow : Window
    {
        private readonly DiagramProject _project;
        private readonly Guid _sheetId;

        public ConstantNodeCreateWindow(DiagramProject project, Guid sheetId)
        {
            _project = project ?? throw new ArgumentNullException("project");
            _sheetId = sheetId;
            InitializeComponent();
            LiteralTextBox.Text = "TRUE";
            DataTypeTextBox.Text = "Bool";
            Loaded += delegate
            {
                LiteralTextBox.Focus();
                LiteralTextBox.SelectAll();
            };
        }

        internal NodeCreationResult Result { get; private set; }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var candidate = NodeCreationLogic.CreateConstant(
                LiteralTextBox.Text,
                DataTypeTextBox.Text);
            var errors = NodeCreationLogic.Validate(_project, _sheetId, candidate);
            if (errors.Count != 0)
            {
                ValidationText.Text = string.Join(Environment.NewLine, errors.Select(error => "• " + error));
                ValidationText.Foreground = FindResource("ErrorBrush") as Brush;
                return;
            }

            Result = candidate;
            DialogResult = true;
        }
    }
}
