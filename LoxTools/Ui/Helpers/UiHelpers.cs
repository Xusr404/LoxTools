using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LoxTools.UI.Helpers {
	internal static class UiHelpers {
		public static ScrollViewer FindScrollViewer(DependencyObject parent) {
			if (parent == null) {
				return null;
			}

			if (parent is ScrollViewer scrollViewer) {
				return scrollViewer;
			}

			int childCount = VisualTreeHelper.GetChildrenCount(parent);
			for (int i = 0; i < childCount; i++) {
				var child = VisualTreeHelper.GetChild(parent, i);
				var result = FindScrollViewer(child);
				if (result != null) {
					return result;
				}
			}

			return null;
		}

		public static ScrollViewer FindAncestorScrollViewer(DependencyObject child) {
			var current = VisualTreeHelper.GetParent(child);
			while (current != null) {
				if (current is ScrollViewer scrollViewer) {
					return scrollViewer;
				}

				current = VisualTreeHelper.GetParent(current);
			}

			return null;
		}
	}
}
