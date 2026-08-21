using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace TiaSclStudio.App
{
    public partial class TiaProjectInspectorWindow : Window
    {
        private readonly string _tagStatusText;
        private readonly string _hardwareStatusText;
        private readonly ICollectionView _tagView;
        private readonly ICollectionView _hardwareView;
        private readonly Predicate<object> _tagFilter;
        private readonly Predicate<object> _hardwareFilter;
        private readonly DispatcherTimer _filterTimer;
        private readonly TiaProjectInspectorFilterDebounceState _filterSchedule =
            new TiaProjectInspectorFilterDebounceState();
        private IReadOnlyList<string> _activeFilterTokens = new string[0];
        private int _scheduledFilterGeneration;
        private bool _isClosed;

        internal TiaProjectInspectorWindow(TiaProjectInspectorModel model)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (!model.CanOpen)
            {
                throw new ArgumentException(
                    "A blocked TIA inspector model cannot be displayed.",
                    "model");
            }

            InitializeComponent();
            TargetText.Text = model.TargetText;
            ProjectPathText.Text = model.ProjectPath;
            ConnectionText.Text = model.ConnectionText;
            CpuText.Text = model.CpuText;
            TagStatusText.Text = model.TagStatusText;
            HardwareStatusText.Text = model.HardwareStatusText;
            _tagStatusText = model.TagStatusText;
            _hardwareStatusText = model.HardwareStatusText;
            _tagView = CollectionViewSource.GetDefaultView(model.Tags);
            _hardwareView = CollectionViewSource.GetDefaultView(model.HardwareChannels);
            _tagFilter = item => TiaProjectInspectorLogic.MatchesFilter(
                item as TiaProjectInspectorTagRow,
                _activeFilterTokens);
            _hardwareFilter = item => TiaProjectInspectorLogic.MatchesFilter(
                item as TiaProjectInspectorHardwareRow,
                _activeFilterTokens);
            _filterTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _filterTimer.Tick += FilterTimer_Tick;
            TagsGrid.ItemsSource = _tagView;
            HardwareGrid.ItemsSource = _hardwareView;
            InspectorWarningText.Text = model.WarningText;
            InspectorWarningPanel.Visibility = string.IsNullOrWhiteSpace(model.WarningText)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void FilterTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_filterTimer == null || _isClosed)
            {
                return;
            }

            _scheduledFilterGeneration = _filterSchedule.Schedule(FilterTextBox.Text);
            _filterTimer.Stop();
            _filterTimer.Start();
        }

        private void FilterTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            ApplyFilterImmediately();
            e.Handled = true;
        }

        private void FilterTimer_Tick(object sender, EventArgs e)
        {
            _filterTimer.Stop();

            string query;
            if (_filterSchedule.TryTake(_scheduledFilterGeneration, out query))
            {
                ApplyFilter(query);
            }
        }

        internal void ApplyFilterImmediately()
        {
            if (_isClosed)
            {
                return;
            }

            _filterTimer.Stop();

            string query;
            if (!_filterSchedule.TryTake(_scheduledFilterGeneration, out query))
            {
                query = FilterTextBox.Text;
            }

            ApplyFilter(query);
        }

        private void ApplyFilter(string query)
        {
            var tokens = TiaProjectInspectorLogic.TokenizeFilter(query);
            _activeFilterTokens = tokens;
            RefreshViewFilter(_tagView, _tagFilter, tokens.Length != 0);
            RefreshViewFilter(_hardwareView, _hardwareFilter, tokens.Length != 0);

            if (tokens.Length == 0)
            {
                TagStatusText.Text = _tagStatusText;
                HardwareStatusText.Text = _hardwareStatusText;
                return;
            }

            TagStatusText.Text = _tagStatusText + " По фильтру: " +
                _tagView.Cast<object>().Count() + ".";
            HardwareStatusText.Text = _hardwareStatusText + " По фильтру: " +
                _hardwareView.Cast<object>().Count() + ".";
        }

        private static void RefreshViewFilter(
            ICollectionView view,
            Predicate<object> filter,
            bool enabled)
        {
            if (!enabled)
            {
                if (view.Filter != null)
                {
                    view.Filter = null;
                }

                return;
            }

            if (!ReferenceEquals(view.Filter, filter))
            {
                view.Filter = filter;
                return;
            }

            view.Refresh();
        }

        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;
            _filterTimer.Stop();
            _filterTimer.Tick -= FilterTimer_Tick;
            _filterSchedule.Cancel();
            base.OnClosed(e);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
