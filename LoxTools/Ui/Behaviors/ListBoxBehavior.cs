using System.Windows;

namespace LoxTools.UI.Behaviors {
	public static class ListBoxBehavior {
		public static readonly DependencyProperty KeepSelectedWhenDisabledProperty =
			DependencyProperty.RegisterAttached(
				"KeepSelectedWhenDisabled",
				typeof(bool),
				typeof(ListBoxBehavior),
				new FrameworkPropertyMetadata(false));

		public static void SetKeepSelectedWhenDisabled(DependencyObject element, bool value) {
			element.SetValue(KeepSelectedWhenDisabledProperty, value);
		}

		public static bool GetKeepSelectedWhenDisabled(DependencyObject element) {
			return (bool)element.GetValue(KeepSelectedWhenDisabledProperty);
		}
	}
}
