using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using TiaSclStudio.Core.Importing;

namespace TiaSclStudio.App
{
    public partial class MainWindow
    {
        private void ImportSclLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureModelEditingAvailable() || _project == null || _project.Plant == null)
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Импортировать интерфейсы FB / FC / UDT из SCL",
                Filter = "Siemens SCL source (*.scl)|*.scl|Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var source = ReadSclSource(dialog.FileName);
                var parsed = new SclLibrarySourceParser().Parse(source);
                var withoutReplacement = SclImportApplicationLogic.BuildPlan(_project, parsed, false);
                var withReplacement = SclImportApplicationLogic.BuildPlan(_project, parsed, true);
                var preview = new SclImportPreviewWindow(withoutReplacement, withReplacement)
                {
                    Owner = this
                };
                if (preview.ShowDialog() != true || !preview.SelectedPlan.CanApply)
                {
                    SetStatus("Импорт SCL отменён; проект не изменён");
                    return;
                }

                var plan = preview.SelectedPlan;
                if (!TryCommitSemanticEdit(
                    "Импорт интерфейсов из " + Path.GetFileName(dialog.FileName),
                    () => SclImportApplicationLogic.Apply(_project, plan)))
                {
                    return;
                }

                var selectedBlock = plan.Items
                    .Where(item => item.CandidateBlock != null &&
                        (item.Action == SclLibraryImportAction.Add ||
                         item.Action == SclLibraryImportAction.Update))
                    .Select(item => item.CandidateBlock.Id)
                    .FirstOrDefault();
                RefreshLibrary(selectedBlock);
                _interactionDiagnostics.Clear();
                RenderDiagram();
                RefreshCompilation(false);
                SetStatus(
                    "Импорт SCL завершён: добавлено " + plan.AddCount +
                    ", обновлено " + plan.UpdateCount +
                    ". Тела FB/FC не копировались.");
            }
            catch (Exception exception)
            {
                AddInteractionError(
                    "UI_SCL_IMPORT",
                    "Не удалось импортировать SCL: " + exception.Message,
                    dialog.FileName);
            }
        }

        private static string ReadSclSource(string path)
        {
            var file = new FileInfo(path);
            if (file.Length > SclLibrarySourceParser.MaximumSourceLength * 4L)
            {
                throw new InvalidDataException("Файл превышает безопасный лимит импорта.");
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (DecoderFallbackException)
            {
                // Classic STEP 7/TIA exports may use the active Windows ANSI
                // code page. The parser still validates every resulting token.
                return File.ReadAllText(path, Encoding.Default);
            }
        }
    }
}
