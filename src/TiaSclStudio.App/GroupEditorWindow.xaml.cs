using System;
using System.Windows;
using System.Windows.Media;
using TiaSclStudio.Diagram.Model;

namespace TiaSclStudio.App
{
    public partial class GroupEditorWindow : Window
    {
        public GroupEditorWindow(string title, string comment)
        {
            InitializeComponent();
            TitleTextBox.Text = title ?? string.Empty;
            CommentTextBox.Text = comment ?? string.Empty;
            Loaded += delegate
            {
                TitleTextBox.Focus();
                TitleTextBox.SelectAll();
            };
        }

        public string ApprovedTitle { get; private set; }

        public string ApprovedComment { get; private set; }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var probe = GroupEditingLogic.CreateDraft(
                    TitleTextBox.Text,
                    CommentTextBox.Text,
                    0.0,
                    0.0,
                    GroupEditingLogic.MinimumWidth,
                    GroupEditingLogic.MinimumHeight);
                ApprovedTitle = probe.Title;
                ApprovedComment = probe.Comment;
                DialogResult = true;
            }
            catch (Exception exception)
            {
                ValidationText.Text = exception.Message;
                ValidationText.Foreground = FindResource("ErrorBrush") as Brush;
            }
        }
    }
}
