using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TiaSclStudio.App
{
    public partial class GroupEditorWindow : Window
    {
        private readonly bool _geometryEnabled;

        public GroupEditorWindow(string title, string comment)
            : this(
                title,
                comment,
                0.0,
                0.0,
                GroupEditingLogic.MinimumWidth,
                GroupEditingLogic.MinimumHeight,
                false)
        {
        }

        public GroupEditorWindow(
            string title,
            string comment,
            double x,
            double y,
            double width,
            double height)
            : this(title, comment, x, y, width, height, true)
        {
        }

        private GroupEditorWindow(
            string title,
            string comment,
            double x,
            double y,
            double width,
            double height,
            bool geometryEnabled)
        {
            InitializeComponent();
            _geometryEnabled = geometryEnabled;
            TitleTextBox.Text = title ?? string.Empty;
            CommentTextBox.Text = comment ?? string.Empty;
            XTextBox.Text = FormatNumber(x);
            YTextBox.Text = FormatNumber(y);
            WidthTextBox.Text = FormatNumber(width);
            HeightTextBox.Text = FormatNumber(height);

            if (_geometryEnabled)
            {
                GeometryPanel.Visibility = Visibility.Visible;
                Height = 570.0;
                MinHeight = 500.0;
                ValidationText.Text =
                    "Название до 80 символов; размеры области не меньше " +
                    FormatNumber(GroupEditingLogic.MinimumWidth) + "×" +
                    FormatNumber(GroupEditingLogic.MinimumHeight) + ".";
            }

            Loaded += delegate
            {
                TitleTextBox.Focus();
                TitleTextBox.SelectAll();
            };
        }

        public string ApprovedTitle { get; private set; }

        public string ApprovedComment { get; private set; }

        public double ApprovedX { get; private set; }

        public double ApprovedY { get; private set; }

        public double ApprovedWidth { get; private set; }

        public double ApprovedHeight { get; private set; }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            double x = 0.0;
            double y = 0.0;
            double width = GroupEditingLogic.MinimumWidth;
            double height = GroupEditingLogic.MinimumHeight;
            if (_geometryEnabled &&
                (!TryReadNumber(XTextBox, "X", false, out x) ||
                 !TryReadNumber(YTextBox, "Y", false, out y) ||
                 !TryReadNumber(WidthTextBox, "Ширина", true, out width) ||
                 !TryReadNumber(HeightTextBox, "Высота", true, out height)))
            {
                return;
            }

            try
            {
                var probe = GroupEditingLogic.CreateDraft(
                    TitleTextBox.Text,
                    CommentTextBox.Text,
                    x,
                    y,
                    width,
                    height);
                ApprovedTitle = probe.Title;
                ApprovedComment = probe.Comment;
                ApprovedX = probe.X;
                ApprovedY = probe.Y;
                ApprovedWidth = probe.Width;
                ApprovedHeight = probe.Height;
                DialogResult = true;
            }
            catch (Exception exception)
            {
                ShowValidationError(exception.Message, null);
            }
        }

        private bool TryReadNumber(
            TextBox textBox,
            string fieldName,
            bool strictlyPositive,
            out double value)
        {
            if (TryParseFiniteNumber(textBox.Text, out value) &&
                (strictlyPositive ? value > 0.0 : value >= 0.0))
            {
                return true;
            }

            ShowValidationError(
                "Поле «" + fieldName + "» должно содержать конечное " +
                (strictlyPositive ? "положительное" : "неотрицательное") +
                " число.",
                textBox);
            return false;
        }

        private void ShowValidationError(string message, TextBox textBox)
        {
            ValidationText.Text = message;
            ValidationText.Foreground = FindResource("ErrorBrush") as Brush;
            if (textBox == null)
            {
                return;
            }

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
