using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TiaSclStudio.App
{
    public partial class App : Application
    {
        private static readonly object CrashLogLock = new object();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Without these, any exception escaping an event handler ends the
            // process through Windows Error Reporting: the editor disappears,
            // the diagram that was open is gone, and the engineer has nothing
            // to report but "it closed". Keeping the window alive lets them
            // save their work to a new file before restarting.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            var logPath = WriteCrashLog("UI", e.Exception);

            // The UI thread is still usable here, so the edit that failed is
            // abandoned and everything else keeps working.
            e.Handled = true;

            MessageBox.Show(
                MainWindowOrNull(),
                "Операция не выполнена из-за непредвиденной ошибки.\n\n" +
                Describe(e.Exception) + "\n\n" +
                "Проект не изменён этой операцией. Сохраните работу в НОВЫЙ файл " +
                "и перезапустите приложение.\n\n" +
                (logPath == null ? string.Empty : "Подробности: " + logPath),
                "Непредвиденная ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        /// <summary>
        /// A failure on a background thread cannot be recovered from — the CLR
        /// is already tearing the process down — so the only useful action left
        /// is to leave a record of what happened.
        /// </summary>
        private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            WriteCrashLog(e.IsTerminating ? "Fatal" : "Background", e.ExceptionObject as Exception);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            WriteCrashLog("Task", e.Exception);

            // An unobserved task exception is a defect worth logging, but it
            // must not take the editor down with it.
            e.SetObserved();
        }

        private Window MainWindowOrNull()
        {
            try
            {
                return MainWindow != null && MainWindow.IsLoaded ? MainWindow : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static string Describe(Exception exception)
        {
            return exception == null
                ? "Неизвестная ошибка."
                : exception.GetType().Name + ": " + exception.Message;
        }

        /// <summary>
        /// Appends to a log next to the user's local application data. Returns
        /// the path, or null when even logging failed — at which point there is
        /// nothing further this handler can usefully do.
        /// </summary>
        private static string WriteCrashLog(string category, Exception exception)
        {
            try
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TiaSclStudio");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "crash.log");

                var entry = new StringBuilder()
                    .AppendLine("---- " + DateTime.Now.ToString("u", CultureInfo.InvariantCulture) +
                                " [" + category + "] ----")
                    .AppendLine(exception == null ? "(no exception object)" : exception.ToString())
                    .AppendLine()
                    .ToString();

                lock (CrashLogLock)
                {
                    File.AppendAllText(path, entry, new UTF8Encoding(true));
                }

                return path;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
