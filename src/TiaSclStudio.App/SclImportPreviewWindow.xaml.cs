using System.Linq;
using System.Windows;

namespace TiaSclStudio.App
{
    public partial class SclImportPreviewWindow : Window
    {
        private readonly SclLibraryImportPlan _withoutReplacement;
        private readonly SclLibraryImportPlan _withReplacement;

        internal SclImportPreviewWindow(
            SclLibraryImportPlan withoutReplacement,
            SclLibraryImportPlan withReplacement)
        {
            _withoutReplacement = withoutReplacement;
            _withReplacement = withReplacement;
            InitializeComponent();

            ReplaceExistingCheckBox.IsEnabled = withoutReplacement.Items.Any(item =>
                item.Action == SclLibraryImportAction.Skip);
            RefreshPlan();
        }

        internal SclLibraryImportPlan SelectedPlan
        {
            get
            {
                return ReplaceExistingCheckBox.IsChecked == true
                    ? _withReplacement
                    : _withoutReplacement;
            }
        }

        private void ReplaceExistingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (ObjectsGrid != null)
            {
                RefreshPlan();
            }
        }

        private void RefreshPlan()
        {
            var plan = SelectedPlan;
            ObjectsGrid.ItemsSource = null;
            ObjectsGrid.ItemsSource = plan.Items;
            MessagesList.ItemsSource = null;
            MessagesList.ItemsSource = plan.Messages;
            SummaryText.Text = "Добавить: " + plan.AddCount +
                " · обновить: " + plan.UpdateCount +
                " · пропустить: " + plan.SkipCount +
                " · всего объектов: " + plan.Items.Count;
            ImportButton.IsEnabled = plan.CanApply;
            BlockingText.Text = plan.HasBlockingErrors
                ? "Импорт заблокирован: исправьте ошибки исходника или конфликты."
                : plan.CanApply
                    ? "Все изменения будут применены одной операцией Undo/Redo."
                    : "Нет объектов для применения.";
            BlockingText.Foreground = plan.HasBlockingErrors
                ? (System.Windows.Media.Brush)FindResource("ErrorBrush")
                : (System.Windows.Media.Brush)FindResource("TextMutedBrush");
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPlan.CanApply)
            {
                DialogResult = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
