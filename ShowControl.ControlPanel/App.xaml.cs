using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace ShowControl.ControlPanel
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var message = $"Unhandled exception (IsTerminating={e.IsTerminating}): {e.ExceptionObject}";
                Debug.WriteLine(message);
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                var message = $"Unobserved task exception: {e.Exception}";
                Debug.WriteLine(message);
                e.SetObserved();
            };

            DispatcherUnhandledException += (sender, e) =>
            {
                Debug.WriteLine($"Unhandled dispatcher exception: {e.Exception}");
                e.Handled = true;
            };
        }
    }
}
