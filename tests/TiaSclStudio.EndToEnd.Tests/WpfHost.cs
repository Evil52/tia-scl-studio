using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace TiaSclStudio.EndToEnd.Tests
{
    /// <summary>
    /// Hosts the real WPF application on one dedicated STA thread with a live
    /// dispatcher, so tests can build and drive the shipped windows exactly the
    /// way the running program does. The application object is created once per
    /// process because WPF allows only one.
    /// </summary>
    public sealed class WpfHost : IDisposable
    {
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(30);

        private readonly Thread _thread;
        private Dispatcher _dispatcher;
        private Exception _startupFailure;
        private bool _disposed;

        public WpfHost()
        {
            var ready = new ManualResetEventSlim(false);

            _thread = new Thread(() =>
            {
                try
                {
                    _dispatcher = Dispatcher.CurrentDispatcher;
                    if (Application.Current == null)
                    {
                        var application = new TiaSclStudio.App.App();
                        application.InitializeComponent();
                        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    }
                }
                catch (Exception exception)
                {
                    _startupFailure = exception;
                }
                finally
                {
                    ready.Set();
                }

                if (_startupFailure == null)
                {
                    Dispatcher.Run();
                }
            });

            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Name = "WPF end-to-end host";
            _thread.Start();

            if (!ready.Wait(StartupTimeout))
            {
                throw new TimeoutException("The WPF host thread did not start within " + StartupTimeout + ".");
            }

            if (_startupFailure != null)
            {
                throw new InvalidOperationException(
                    "The WPF application could not be created: " + _startupFailure.Message,
                    _startupFailure);
            }
        }

        public void Invoke(Action action)
        {
            Invoke<object>(() =>
            {
                action();
                return null;
            });
        }

        public T Invoke<T>(Func<T> action)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(typeof(WpfHost).Name);
            }

            return _dispatcher.Invoke(action, DispatcherPriority.Normal);
        }

        /// <summary>
        /// Lets the dispatcher finish the work the application queued for later,
        /// which is where WPF does layout and most deferred UI updates.
        /// </summary>
        public void DrainDispatcher()
        {
            _dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_dispatcher != null)
            {
                _dispatcher.InvokeShutdown();
            }

            _thread.Join(ShutdownTimeout);
        }
    }

    [CollectionDefinition(Name)]
    public sealed class WpfCollection : ICollectionFixture<WpfHost>
    {
        public const string Name = "wpf";
    }

    /// <summary>
    /// Reaches the parts of MainWindow that are private by design. An
    /// end-to-end test has to see the same state the window renders from, and
    /// widening production visibility purely for tests would be worse.
    /// </summary>
    internal static class MainWindowProbe
    {
        private const BindingFlags Instance =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static T Field<T>(Window window, string name)
        {
            var field = window.GetType().GetField(name, Instance);
            Assert.True(field != null, "MainWindow has no field named " + name + ".");
            return (T)field.GetValue(window);
        }

        public static void SetField<T>(Window window, string name, T value)
        {
            var field = window.GetType().GetField(name, Instance);
            Assert.True(field != null, "MainWindow has no field named " + name + ".");
            field.SetValue(window, value);
        }

        public static object Call(Window window, string name, params object[] arguments)
        {
            var method = window.GetType().GetMethod(name, Instance);
            Assert.True(method != null, "MainWindow has no method named " + name + ".");
            return method.Invoke(window, arguments);
        }

        public static T Element<T>(Window window, string name)
            where T : FrameworkElement
        {
            var element = window.FindName(name) as T;
            Assert.True(element != null, "The window has no " + typeof(T).Name + " named " + name + ".");
            return element;
        }

        public static void Click(Window window, string buttonName)
        {
            Element<Button>(window, buttonName)
                .RaiseEvent(new RoutedEventArgs(ButtonBase_ClickEvent));
        }

        private static readonly RoutedEvent ButtonBase_ClickEvent =
            System.Windows.Controls.Primitives.ButtonBase.ClickEvent;
    }
}
