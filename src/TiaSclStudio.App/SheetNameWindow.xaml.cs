using System;
using System.Windows;
using System.Windows.Input;

namespace TiaSclStudio.App
{
    public partial class SheetNameWindow : Window
    {
        private readonly Func<string, string> _validate;

        internal SheetNameWindow(
            string title,
            string prompt,
            string initialName,
            Func<string, string> validate)
        {
            InitializeComponent();
            Title = title ?? "Имя листа";
            PromptText.Text = prompt ?? string.Empty;
            NameTextBox.Text = initialName ?? string.Empty;
            _validate = validate;
            Loaded += OnLoaded;
        }

        internal string SheetName { get; private set; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            TryAccept();
        }

        private void NameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            TryAccept();
            e.Handled = true;
        }

        private void TryAccept()
        {
            var candidate = NameTextBox.Text ?? string.Empty;
            var error = _validate == null ? string.Empty : _validate(candidate);
            if (!string.IsNullOrEmpty(error))
            {
                ErrorText.Text = error;
                NameTextBox.Focus();
                NameTextBox.SelectAll();
                return;
            }

            SheetName = candidate;
            DialogResult = true;
        }
    }
}
