using System;
using System.Windows;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    public partial class NoteEditorWindow : Window
    {
        public NoteEditorWindow(NoteNode source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            InitializeComponent();
            NoteTextBox.Text = source.Text ?? string.Empty;
            Loaded += delegate
            {
                NoteTextBox.Focus();
                NoteTextBox.SelectAll();
            };
        }

        public string ApprovedText { get; private set; }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string error;
            if (!NoteEditingLogic.TryValidateText(NoteTextBox.Text, out error))
            {
                ValidationText.Text = error;
                ValidationText.Foreground = FindResource("ErrorBrush") as System.Windows.Media.Brush;
                return;
            }

            ApprovedText = NoteTextBox.Text.Trim();
            DialogResult = true;
        }
    }
}
