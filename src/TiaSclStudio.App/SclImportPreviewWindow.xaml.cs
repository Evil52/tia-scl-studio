using System.Linq;
using System.Windows;

namespace TiaSclStudio.App
{
    internal sealed class SclImportPreviewPresentation
    {
        internal SclImportPreviewPresentation(
            string windowTitle,
            string heading,
            string safetyText,
            string warningText = null)
        {
            WindowTitle = windowTitle ?? string.Empty;
            Heading = heading ?? string.Empty;
            SafetyText = safetyText ?? string.Empty;
            WarningText = warningText ?? string.Empty;
        }

        internal string WindowTitle { get; private set; }

        internal string Heading { get; private set; }

        internal string SafetyText { get; private set; }

        internal string WarningText { get; private set; }

        internal static SclImportPreviewPresentation FromFile
        {
            get
            {
                return new SclImportPreviewPresentation(
                    "Предпросмотр импорта SCL",
                    "ИМПОРТ FB / FC / UDT ИЗ SCL",
                    "Импортируется только объявление и интерфейс. Код между BEGIN и END_FUNCTION/END_FUNCTION_BLOCK " +
                    "не копируется и не выполняется; блок помечается ImportedInterfaceOnly.");
            }
        }

        internal static SclImportPreviewPresentation FromConnectedTia
        {
            get
            {
                return new SclImportPreviewPresentation(
                    "Предпросмотр импорта из TIA Portal",
                    "ИМПОРТ FB / FC / UDT ИЗ TIA PORTAL",
                    "Показан снимок подключённого PLC, полученный только для чтения. TIA Portal не изменяется. " +
                    "В локальную библиотеку попадают только объявления и интерфейсы; тела FB/FC не копируются.");
            }
        }
    }

    public partial class SclImportPreviewWindow : Window
    {
        private readonly SclLibraryImportPlan _withoutReplacement;
        private readonly SclLibraryImportPlan _withReplacement;

        internal SclImportPreviewWindow(
            SclLibraryImportPlan withoutReplacement,
            SclLibraryImportPlan withReplacement)
            : this(withoutReplacement, withReplacement, SclImportPreviewPresentation.FromFile)
        {
        }

        internal SclImportPreviewWindow(
            SclLibraryImportPlan withoutReplacement,
            SclLibraryImportPlan withReplacement,
            SclImportPreviewPresentation presentation)
        {
            _withoutReplacement = withoutReplacement ?? throw new System.ArgumentNullException("withoutReplacement");
            _withReplacement = withReplacement ?? throw new System.ArgumentNullException("withReplacement");
            if (presentation == null) throw new System.ArgumentNullException("presentation");
            InitializeComponent();

            Title = presentation.WindowTitle;
            ImportHeadingText.Text = presentation.Heading;
            ImportSafetyText.Text = presentation.SafetyText;
            ImportWarningText.Text = presentation.WarningText;
            ImportWarningPanel.Visibility = string.IsNullOrWhiteSpace(presentation.WarningText)
                ? Visibility.Collapsed
                : Visibility.Visible;

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
            MessagesList.ItemsSource = plan.Messages
                .OrderBy(item => item.Severity == TiaSclStudio.Core.Importing.SclImportDiagnosticSeverity.Error ? 0 : 1)
                .ThenBy(item => string.Equals(
                    item.Code,
                    "SCL_IMPORT_ATOMIC_BATCH_BLOCKED",
                    System.StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(item => item.Line)
                .ToList();
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
