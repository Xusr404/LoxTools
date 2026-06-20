using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LoxTools.Core.Settings;
using LoxTools.UI.Helpers;
using global::LoxTools.Language;

namespace LoxTools {

	// WPF replacement for WinForms VersionSelector (C# 8 compatible)
	public partial class VersionSelectorWindow : Window {

		private readonly VersionSelectorVm _vm;

		public VersionSelectorWindow(System.Windows.Forms.ApplicationContext applicationContext, Dictionary<string, string> configs) {
			InitializeComponent();

			// Match SettingsWindow: radial background is set in code-behind for performance and consistency
			TryApplyRadialBackground();

			_vm = new VersionSelectorVm(configs);
			DataContext = _vm;

			Loaded += VersionSelectorWindow_Loaded;
		}

		private void VersionSelectorWindow_Loaded(object sender, RoutedEventArgs e) {
			_vm.OnLoaded();
			ContentScrollViewer?.ScrollToTop();
		}

		private void TryApplyRadialBackground() {
			try {
				Background = RadialBackgroundProvider.RadialBg;
			}
			catch {
				Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x11, 0x13));
			}
		}

		public Dictionary<string, string> ConfigVersions {
			get { return _vm.ConfigVersions; }
			set { _vm.SetConfigVersions(value); }
		}

		public string FilePath {
			get { return _vm.FilePath; }
			set { _vm.FilePath = value; }
		}

		public void ExecuteArgs(string filePath, bool startWithLatestVersion) {
			_vm.ExecuteArgs(filePath, startWithLatestVersion);
		}

		private void Ok_Click(object sender, RoutedEventArgs e) {
			if (_vm.TryAcceptAndStart()) {
				Close();
			}
		}

		private void Cancel_Click(object sender, RoutedEventArgs e) {
			Close();
		}

		private void InstalledVersions_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
			if (_vm.TryDoubleClickStart()) {
				Close();
			}
		}

		private void ListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
			if (!(sender is ListBox listBox)) {
				return;
			}

			ScrollViewer scrollViewer = UiHelpers.FindScrollViewer(listBox);
			if (scrollViewer == null) {
				return;
			}

			if (scrollViewer.ExtentHeight <= scrollViewer.ViewportHeight + 0.5) {
				e.Handled = true;
				ScrollViewer parentScrollViewer = UiHelpers.FindAncestorScrollViewer(listBox);
				if (parentScrollViewer == null) {
					return;
				}

				var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) {
					RoutedEvent = UIElement.MouseWheelEvent,
					Source = listBox
				};
				parentScrollViewer.RaiseEvent(args);
			}
		}

		private void OpenSettings_Click(object sender, RoutedEventArgs e) {
			LoxToolsApplication.GetInstance()?.ShowSettingsWindow(modal: true);
		}
	}

	public sealed class VersionSelectorVm : INotifyPropertyChanged {

		private Dictionary<string, string> _configVersions = new Dictionary<string, string>();

		public event PropertyChangedEventHandler PropertyChanged;

		public VersionSelectorVm(Dictionary<string, string> configs) {
			SetConfigVersions(configs);

			SettingsGuard.Execute(() => {
				_useLatestVersion = Properties.Settings.Default.useLatestVersion;
				_alwaysShowSelectionDialog = Properties.Settings.Default.alwaysShowSelectionDialog;
			}, () => {
				_useLatestVersion = false;
				_alwaysShowSelectionDialog = false;
			});
			UpdateListEnabled();

		}

		public string TitleText { get { return Lang.VersionSelectorTitle; } }
		public string InstalledVersionsHeader { get { return Lang.InstalledVersions; } }
		public string LaunchModeTitle { get { return Lang.LaunchModeTitle; } }
		public string LaunchModeManagedHint { get { return Lang.LaunchModeManagedHint; } }
		public string OpenSettingsLabel { get { return Lang.OpenSettingsLabel; } }
		public string AlwaysShowVersionDialogLabel { get { return Lang.AlwaysShowVersionDialog; } }
		public string AlwaysShowVersionDialogHint { get { return Lang.SettingsAlwaysShowDialogHint; } }
		public string UseLatestLabel { get { return Lang.UseLatestVersion; } }
		public string UseLatestHint { get { return Lang.UseLatestVersionHint; } }

		public string OkText { get { return Lang.Start; } }
		public string CancelText { get { return Lang.Cancel; } }

		public ObservableCollection<InstalledVersionItem> InstalledVersions { get; } = new ObservableCollection<InstalledVersionItem>();

		private InstalledVersionItem _selectedInstalledVersion;
		public InstalledVersionItem SelectedInstalledVersion {
			get { return _selectedInstalledVersion; }
			set {
				if (_selectedInstalledVersion == value)
					return;
				_selectedInstalledVersion = value;
				OnPropertyChanged(nameof(SelectedInstalledVersion));
			}
		}

		private bool _useLatestVersion;
		public bool UseLatestVersion {
			get { return _useLatestVersion; }
			set {
				if (_useLatestVersion == value)
					return;
				_useLatestVersion = value;
				OnPropertyChanged(nameof(UseLatestVersion));

				UpdateListEnabled();

				if (_useLatestVersion && InstalledVersions.Count > 0) {
					SelectedInstalledVersion = InstalledVersions[0];
				}
			}
		}

		private bool _alwaysShowSelectionDialog;
		public bool AlwaysShowSelectionDialog {
			get { return _alwaysShowSelectionDialog; }
			set {
				if (_alwaysShowSelectionDialog == value)
					return;
				_alwaysShowSelectionDialog = value;
				SettingsGuard.Execute(() => {
					Properties.Settings.Default.alwaysShowSelectionDialog = value;
					Properties.Settings.Default.Save();
				});
				OnPropertyChanged(nameof(AlwaysShowSelectionDialog));
			}
		}

		private bool _isInstalledListEnabled = true;
		public bool IsInstalledListEnabled {
			get { return _isInstalledListEnabled; }
			private set {
				if (_isInstalledListEnabled == value)
					return;
				_isInstalledListEnabled = value;
				OnPropertyChanged(nameof(IsInstalledListEnabled));
			}
		}

		public string FilePath { get; set; }

		public Dictionary<string, string> ConfigVersions { get { return _configVersions; } }

		public void SetConfigVersions(Dictionary<string, string> value) {
			_configVersions = value ?? new Dictionary<string, string>();
			FillInstalledVersions(_configVersions.Keys.ToList(), true);
		}

		public void OnLoaded() {
			CheckSettings();
			SelectCorrectItem();
		}

		private void FillInstalledVersions(List<string> items, bool revertSorted) {
			InstalledVersions.Clear();

			if (revertSorted) {
				items.Sort();
				items.Reverse();
			}

			string defaultPath = SettingsGuard.Execute(
				() => Properties.Settings.Default.defaultVersion,
				string.Empty);
			foreach (string i in items) {
				string path = _configVersions.ContainsKey(i) ? _configVersions[i] : string.Empty;
				bool isDefault = !string.IsNullOrEmpty(defaultPath) && string.Equals(defaultPath, path, StringComparison.OrdinalIgnoreCase);
				InstalledVersions.Add(new InstalledVersionItem(i, path, isDefault));
			}
		}

		private void SelectCorrectItem() {
			if (InstalledVersions.Count == 0)
				return;

			if (SettingsGuard.Execute(() => Properties.Settings.Default.useLatestVersion, false)) {
				SelectedInstalledVersion = InstalledVersions[0];
				return;
			}

			try {
				string defaultPath = SettingsGuard.Execute(() => Properties.Settings.Default.defaultVersion, string.Empty);
				InstalledVersionItem item = InstalledVersions.FirstOrDefault(x => string.Equals(x.Path, defaultPath, StringComparison.OrdinalIgnoreCase));
				SelectedInstalledVersion = item ?? InstalledVersions[0];
			}
			catch {
				SelectedInstalledVersion = InstalledVersions[0];
			}
		}

		private void CheckSettings() {
			SettingsGuard.Execute(() => {
				if (string.IsNullOrEmpty(Properties.Settings.Default.defaultVersion)) {
					string first = _configVersions.Values.FirstOrDefault() ?? string.Empty;
					SaveSettings(first, UseLatestVersion);
				}
			});
		}

		private void SaveSettings(string defaultConfigVersionPath, bool useLatestVersion) {
			if (!string.IsNullOrEmpty(defaultConfigVersionPath)) {
				SettingsGuard.Execute(() => {
					Properties.Settings.Default.defaultVersion = defaultConfigVersionPath;
					Properties.Settings.Default.useLatestVersion = useLatestVersion;
					Properties.Settings.Default.Save();
				});
			}
		}

		public void ExecuteArgs(string filePath, bool startWithLatestVersion) {
			SettingsGuard.Execute(() => Properties.Settings.Default.Reload());

			if (startWithLatestVersion) {
				if (_configVersions.Count > 0) {
					StartConfigWithArgument(_configVersions.Values.Last(), filePath);
				}
				else {
					MessageBox.Show(Lang.NoInstalledConfigVersionsFound);
				}
			}
			else {
				string defaultVersion = SettingsGuard.Execute(
					() => Properties.Settings.Default.defaultVersion,
					string.Empty);
				StartConfigWithArgument(defaultVersion, filePath);
			}
		}

		public bool TryAcceptAndStart() {
			if (InstalledVersions.Count == 0) {
				MessageBox.Show(Lang.NoInstalledConfigVersionsFound);
				return false;
			}

			InstalledVersionItem selectedItem = SelectedInstalledVersion ?? InstalledVersions[0];
			string selectedPath = selectedItem != null ? selectedItem.Path : string.Empty;

			ApplyDefaultSelection(selectedPath, forceSave: !UseLatestVersion);

			StartConfigWithArgument(selectedPath, FilePath);
			return true;
		}

		public bool TryDoubleClickStart() {
			if (InstalledVersions.Count == 0)
				return false;

			InstalledVersionItem selectedItem = SelectedInstalledVersion ?? InstalledVersions[0];
			string selectedPath = selectedItem != null ? selectedItem.Path : string.Empty;

			StartConfigWithArgument(selectedPath, FilePath);
			ApplyDefaultSelection(selectedPath, forceSave: true);
			return true;
		}

		private void StartConfigWithArgument(string configPath, string pathArgs) {
			var startInfo = new ProcessStartInfo();
			startInfo.WindowStyle = ProcessWindowStyle.Normal;
			startInfo.FileName = configPath + "\\\\LoxoneConfig.exe";
			startInfo.Arguments = "\"" + (pathArgs ?? string.Empty) + "\"";

			try {
				Process.Start(startInfo);
			}
			catch (Exception ex) {
				MessageBox.Show(string.Format(Lang.StartConfigFailed, ex));
			}
		}

		private void UpdateListEnabled() {
			IsInstalledListEnabled = true;
		}

		private void ApplyDefaultSelection(string selectedPath, bool forceSave) {
			if (!forceSave && !UseLatestVersion)
				return;

			SaveSettings(selectedPath, UseLatestVersion);
		}


		private void OnPropertyChanged(string name) {
			var handler = PropertyChanged;
			if (handler != null)
				handler(this, new PropertyChangedEventArgs(name));
		}
	}

	public sealed class InstalledVersionItem : INotifyPropertyChanged {
		public event PropertyChangedEventHandler PropertyChanged;

		public InstalledVersionItem(string name, string path, bool isDefault) {
			Name = name;
			Path = path;
			_isDefault = isDefault;
		}

		public string Name { get; }
		public string Path { get; }

		private bool _isDefault;
		public bool IsDefault {
			get { return _isDefault; }
			set {
				if (_isDefault == value)
					return;
				_isDefault = value;
				OnPropertyChanged(nameof(IsDefault));
			}
		}

		private void OnPropertyChanged(string name) {
			var handler = PropertyChanged;
			if (handler != null)
				handler(this, new PropertyChangedEventArgs(name));
		}
	}

}
