using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TiaSclStudio.App
{
    public partial class SheetPropertiesWindow : Window
    {
        private readonly Func<string, string> _validateName;

        public SheetPropertiesWindow(string name, double width, double height)
            : this(name, width, height, null)
        {
        }

        public SheetPropertiesWindow(
            string name,
            double width,
            double height,
            Func<string, string> validateName)
        {
            InitializeComponent();
            _validateName = validateName;
            NameTextBox.Text = name ?? string.Empty;
            WidthTextBox.Text = FormatNumber(width);
            HeightTextBox.Text = FormatNumber(height);
            Loaded += delegate
            {
                NameTextBox.Focus();
                NameTextBox.SelectAll();
            };
        }

        public string ApprovedName { get; private set; }

        public double ApprovedWidth { get; private set; }

        public double ApprovedHeight { get; private set; }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            var candidateName = NameTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(candidateName))
            {
                ShowValidationError("Имя листа не должно быть пустым.", NameTextBox);
                return;
            }

            var nameError = _validateName == null
                ? string.Empty
                : _validateName(candidateName);
            if (!string.IsNullOrWhiteSpace(nameError))
            {
                ShowValidationError(nameError, NameTextBox);
                return;
            }

            double width;
            if (!TryReadSheetExtent(
                WidthTextBox,
                "Ширина",
                SheetEditingLogic.MinimumSheetWidth,
                out width))
            {
                return;
            }

            double height;
            if (!TryReadSheetExtent(
                HeightTextBox,
                "Высота",
                SheetEditingLogic.MinimumSheetHeight,
                out height))
            {
                return;
            }

            ApprovedName = candidateName;
            ApprovedWidth = width;
            ApprovedHeight = height;
            DialogResult = true;
        }

        private bool TryReadSheetExtent(
            TextBox textBox,
            string fieldName,
            double minimum,
            out double value)
        {
            if (TryParseFiniteNumber(textBox.Text, out value) &&
                value >= minimum &&
                value <= SheetEditingLogic.MaximumSheetExtent)
            {
                return true;
            }

            ShowValidationError(
                "Поле «" + fieldName + "» должно быть от " +
                FormatNumber(minimum) + " до " +
                FormatNumber(SheetEditingLogic.MaximumSheetExtent) + ".",
                textBox);
            return false;
        }

        private void ShowValidationError(string message, TextBox textBox)
        {
            ValidationText.Text = message;
            ValidationText.Foreground = FindResource("ErrorBrush") as Brush;
            textBox.Focus();
            textBox.SelectAll();
        }

        private static bool TryParseFiniteNumber(string text, out double value)
        {
            var candidate = (text ?? string.Empty).Trim();
            var style = NumberStyles.Float;
            if (!double.TryParse(candidate, style, CultureInfo.CurrentCulture, out value) &&
                !double.TryParse(candidate, style, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.CurrentCulture);
        }
    }
}
