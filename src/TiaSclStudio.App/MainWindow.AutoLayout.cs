using System;
using System.Windows;
using System.Windows.Input;

namespace TiaSclStudio.App
{
    public partial class MainWindow
    {
        public static RoutedUICommand AutoLayoutCommand { get; } =
            new RoutedUICommand(
                "Автоматическая раскладка",
                "AutoLayout",
                typeof(MainWindow));

        private void AutoLayoutWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CommandManager.InvalidateRequerySuggested();
        }

        private void AutoLayoutCommand_CanExecute(
            object sender,
            CanExecuteRoutedEventArgs e)
        {
            e.CanExecute =
                !_onlineBusy &&
                _project != null &&
                _sheet != null &&
                _dragNode == null &&
                !IsGroupDragActive() &&
                !IsCanvasViewGestureActive();
            e.Handled = true;
        }

        private void AutoLayoutCommand_Executed(
            object sender,
            ExecutedRoutedEventArgs e)
        {
            e.Handled = true;

            if (IsTypingFocus())
            {
                return;
            }

            if (_pendingSource != null)
            {
                SetStatus("Сначала завершите или отмените создание связи");
                return;
            }

            if (!EnsureModelEditingAvailable() || _sheet == null)
            {
                return;
            }

            SheetAutoLayoutResult layoutResult = null;
            var sheetName = _sheet.Name;
            if (!TryCommitSemanticEdit(
                "Автоматическая раскладка листа " + sheetName,
                () =>
                {
                    layoutResult = SheetAutoLayoutLogic.Apply(
                        _sheet,
                        GetNodeWidth,
                        GetNodeHeight);
                }) || layoutResult == null)
            {
                return;
            }

            if (layoutResult.Changed)
            {
                DiagramCanvas.Width = Math.Max(900.0, _sheet.Width);
                DiagramCanvas.Height = Math.Max(560.0, _sheet.Height);
                RenderDiagram();
                RefreshCompilation(false);
            }

            FitDiagramToAll();
            SetStatus(
                layoutResult.Status +
                " · показано всё: " +
                Math.Round(_sheet.Zoom * 100.0) +
                "%");
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
