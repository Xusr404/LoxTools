using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using LoxTools.Core.Settings;
using SingleInstanceCore;

namespace LoxTools {
    static class Program {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args) {
#if DELETEREGISTRYENTRIES
			InitialSetup.FirstStartSetup();
			return;
#endif
#if DEBUG
            CultureInfo ci = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentCulture = ci;
            Thread.CurrentThread.CurrentUICulture = ci;
#endif
			bool shiftPressed = LoxToolsApplication.ShiftPressed();
            bool onboardingTestRequested = LoxToolsApplication.IsTrayOnboardingTestRequested(args);
#if DEBUG
            Console.WriteLine($"Onboarding test requested: {onboardingTestRequested}");
#endif

            if (onboardingTestRequested) {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new LoxToolsApplication(args, shiftPressed));
                return;
            }

            SingleInstanceHandler app = new SingleInstanceHandler();

#if DEBUG
			string singleInstanceName = "LoxTools.Debug";
#else
			string singleInstanceName = nameof(LoxTools);
#endif
			var isFirstInstance = app.InitializeAsFirstInstance(singleInstanceName);
            if (isFirstInstance) {
                try {
                    app.Run(args, shiftPressed);
                } finally {
                    SingleInstance.Cleanup();
                }
            }
        }
    }

    class SingleInstanceHandler : ISingleInstance {
        LoxToolsApplication ApplicationContext { get; set; }
        public void Run(string[] args, bool shiftPressed) {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(ApplicationContext = new LoxToolsApplication(args, shiftPressed));
        }

        public void OnInstanceInvoked(string[] args) {
            ApplicationContext.OnInstanceInvoked(args);
        }
    }
}
