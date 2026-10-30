using LoxTools.Language;
using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LoxTools {
	public partial class TrayOnboardingWindow : Window {
		public TrayOnboardingWindow() : this(false) { }

		internal TrayOnboardingWindow(bool opensSettings) {
			OpensSettings = opensSettings;
			AppIcon = LoadAppIcon();
			InitializeComponent();
			DataContext = this;
			Loaded += TrayOnboardingWindow_Loaded;
		}

		public ImageSource AppIcon { get; }
		public string WindowTitle => Lang.TrayOnboarding_WindowTitle;
		public string TitleText => Lang.TrayOnboarding_Title;
		public string DescriptionText => Lang.TrayOnboarding_Description;
		public string StepOneTitle => Lang.TrayOnboarding_StepOneTitle;
		public string StepOneDescription => Lang.TrayOnboarding_StepOneDescription;
		public string StepTwoTitle => Lang.TrayOnboarding_StepTwoTitle;
		public string StepTwoDescription => Lang.TrayOnboarding_StepTwoDescription;
		public string ReopenHintText => Lang.TrayOnboarding_ReopenHint;
		internal bool OpensSettings { get; }
		internal bool WasAcknowledged { get; private set; }
		public string DoneText => OpensSettings ? Lang.TrayOnboarding_DoneOpenSettings : Lang.TrayOnboarding_Done;
		public string CloseText => Lang.TrayOnboarding_Close;
		public string IllustrationDescription => Lang.TrayOnboarding_IllustrationDescription;
		public string DropHereText => Lang.TrayOnboarding_DropHere;
		public string TaskbarClockText => DateTime.Now.ToString("t");
		public string TaskbarDateText => DateTime.Now.ToString("d");

		private void TrayOnboardingWindow_Loaded(object sender, RoutedEventArgs e) {
			BringToFront();
		}

		public void BringToFront() {
			if (WindowState == WindowState.Minimized) {
				WindowState = WindowState.Normal;
			}

			Topmost = true;
			Activate();
			Focus();

			Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
				Topmost = false;
				Activate();
				Focus();
			}));
		}

		private static ImageSource LoadAppIcon() {
			try {
				BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
					Properties.Resources.LoxTools.Handle,
					Int32Rect.Empty,
					BitmapSizeOptions.FromEmptyOptions());
				source.Freeze();
				return source;
			}
			catch {
				return null;
			}
		}

		private void Done_Click(object sender, RoutedEventArgs e) {
			WasAcknowledged = true;
			Close();
		}

		private void Close_Click(object sender, RoutedEventArgs e) {
			Close();
		}

		private void WindowSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
			if (e.ButtonState == MouseButtonState.Pressed
				&& FindVisualParent<ButtonBase>(e.OriginalSource as DependencyObject) == null) {
				DragMove();
			}
		}

		private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject {
			while (child != null) {
				if (child is T match) {
					return match;
				}

				DependencyObject parent = null;
				try {
					parent = VisualTreeHelper.GetParent(child);
				}
				catch (InvalidOperationException) {
				}

				if (parent == null && child is FrameworkElement element) {
					parent = element.Parent;
				}

				if (parent == null && child is FrameworkContentElement contentElement) {
					parent = contentElement.Parent;
				}

				child = parent;
			}

			return null;
		}
	}
}
