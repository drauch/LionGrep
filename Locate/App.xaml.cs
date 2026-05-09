using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Locate
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            // Parse command-line flags BEFORE InitializeComponent so anything that touches the
            // registry during XAML init / MainWindow construction (settings load, last-form
            // restore, presets) sees the right RootPath.
            ApplyCommandLineOverrides(Environment.GetCommandLineArgs());
            InitializeComponent();
        }

        /// <summary>
        /// Inspects argv for app-level flags. Currently:
        ///   --alternate-registry-key &lt;HKCU subpath&gt;
        ///       Redirects all persisted state (Settings, Recents, Presets, LastForm) to the
        ///       given subpath under HKCU. Used by Locate.UI.Tests to run end-to-end smoke
        ///       tests against an isolated registry tree, so the developer's real settings
        ///       (and recents, and "remember-me" toggles) survive a test run untouched.
        /// </summary>
        private static void ApplyCommandLineOverrides(string[] args)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--alternate-registry-key", StringComparison.OrdinalIgnoreCase))
                {
                    var path = args[i + 1].Trim().TrimStart('\\');
                    if (!string.IsNullOrEmpty(path))
                        Services.RegistryStore.RootPath = path;
                    return;
                }
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }
    }
}
