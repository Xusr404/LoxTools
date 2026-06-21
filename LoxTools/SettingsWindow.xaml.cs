using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LoxTools.Language;
using LoxTools.Core.Settings;
using LoxTools.Models;
using LoxTools.UI.Helpers;
using LoxTools.UI.Tray;
using LoxTools.UpdateCheck;
using LoxTools.AppUpdates;
using WinForms = System.Windows.Forms;

namespace LoxTools {
	public partial class SettingsWindow : Window {
		internal SettingsVm Vm { get; }
		public event EventHandler SettingsSaved;

		public SettingsWindow() {
			InitializeComponent();

			Vm = new SettingsVm();
			DataContext = Vm;
			if (ContextMenuManager.AppUpdateService != null) {
				Vm.ApplyAppUpdateSnapshot(ContextMenuManager.AppUpdateService.CurrentSnapshot);
				ContextMenuManager.AppUpdateService.StateChanged += AppUpdateService_StateChanged;
			}
			Closed += SettingsWindow_Closed;

			Title = Vm.LangSettings;
			Loaded += SettingsWindow_Loaded;
			Activated += SettingsWindow_Activated;

			try {
				Icon = Imaging.CreateBitmapSourceFromHIcon(
					Properties.Resources.LoxTools.Handle,
					Int32Rect.Empty,
					BitmapSizeOptions.FromEmptyOptions());
			}
			catch { }

			TryApplyRadialBackground();
		}

		private void SettingsWindow_Loaded(object sender, RoutedEventArgs e) {
			Vm.RefreshLaunchModeBindings();
			Vm.RefreshFileAssociations();
			ContextMenuManager.AppUpdateService?.CheckIfStale();
		}

		private void SettingsWindow_Activated(object sender, EventArgs e) {
			Vm.RefreshFileAssociations();
		}

		public SettingsWindow(IEnumerable<string> configPaths, IEnumerable<string> projectPaths)
			: this() {
			Vm.LoadPaths(configPaths, projectPaths);
		}

		private void TryApplyRadialBackground() {
			try {
				Background = RadialBackgroundProvider.RadialBg;
			}
			catch {
				Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x11, 0x13));
			}
		}

		private void BrowseConfig_Click(object sender, RoutedEventArgs e) {
			using var fbd = new WinForms.FolderBrowserDialog {
				RootFolder = Environment.SpecialFolder.MyComputer,
				SelectedPath = @"C:\Program Files (x86)\"
			};

			if (fbd.ShowDialog() == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath)) {
				if (!Vm.ConfigPaths.Contains(fbd.SelectedPath)) {
					Vm.ConfigPaths.Add(fbd.SelectedPath);
					Vm.SelectedConfigPath = fbd.SelectedPath;
				}
				else {
					_ = Vm.ShowConfigExistsMessage();
				}
			}
		}

		private void BrowseProjects_Click(object sender, RoutedEventArgs e) {
			string documentsPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}\\";
			using var fbd = new WinForms.FolderBrowserDialog {
				RootFolder = Environment.SpecialFolder.MyDocuments,
				SelectedPath = documentsPath
			};

			if (fbd.ShowDialog() == WinForms.DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath)) {
				if (!Vm.ProjectPaths.Contains(fbd.SelectedPath)) {
					Vm.ProjectPaths.Add(fbd.SelectedPath);
					Vm.SelectedProjectPath = fbd.SelectedPath;
				}
				else {
					_ = Vm.ShowProjectsExistsMessage();
				}
			}
		}

		private void RemovePath_Click(object sender, RoutedEventArgs e) {
			var button = sender as Button;
			var path = button?.DataContext as string;
			if (button == null || path == null)
				return;

			DependencyObject current = button;
			while (current != null && !(current is ListBox))
				current = VisualTreeHelper.GetParent(current);

			var listBox = current as ListBox;
			if (listBox == null)
				return;

			if (ReferenceEquals(listBox.ItemsSource, Vm.ConfigPaths))
				Vm.RemoveConfigPath(path);
			else if (ReferenceEquals(listBox.ItemsSource, Vm.ProjectPaths))
				Vm.RemoveProjectPath(path);

			e.Handled = true;
		}

		private void UseSuggestedConfigPath_Click(object sender, RoutedEventArgs e) => Vm.UseSuggestedConfigPath();
		private void UseSuggestedProjectPath_Click(object sender, RoutedEventArgs e) => Vm.UseSuggestedProjectPath();

		private void ConfigPaths_KeyDown(object sender, KeyEventArgs e) {
			if (e.Key == Key.Delete)
				Vm.RemoveSelectedConfig();
		}

		private void ProjectPaths_KeyDown(object sender, KeyEventArgs e) {
			if (e.Key == Key.Delete)
				Vm.RemoveSelectedProject();
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

		private void Save_Click(object sender, RoutedEventArgs e) {
			Vm.SaveToSettings();
			SettingsSaved?.Invoke(this, EventArgs.Empty);
			Close();
		}

		private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

		private void AppUpdateCheck_Click(object sender, RoutedEventArgs e) => ContextMenuManager.AppUpdateService?.CheckNow();
		private void AppUpdateAction_Click(object sender, RoutedEventArgs e) => ContextMenuManager.AppUpdateService?.HandlePrimaryAction();
		private void OpenLoxToolsGitHub_Click(object sender, RoutedEventArgs e) {
			try {
				Process.Start(new ProcessStartInfo {
					FileName = "https://github.com/Xusr404/LoxTools",
					UseShellExecute = true
				});
			}
			catch { }
		}

		private void AppUpdateService_StateChanged(object sender, AppUpdateSnapshot snapshot) => Vm.ApplyAppUpdateSnapshot(snapshot);

		private void SettingsWindow_Closed(object sender, EventArgs e) {
			if (ContextMenuManager.AppUpdateService != null) {
				ContextMenuManager.AppUpdateService.StateChanged -= AppUpdateService_StateChanged;
			}
		}

		private void OpenDefaultApps_Click(object sender, RoutedEventArgs e) {
			string registeredName = InitialSetup.RegisteredAppName;
			string deepLink = $"ms-settings:defaultapps?registeredAppUser={Uri.EscapeDataString(registeredName)}";

			try {
				InitialSetup.RefreshDefaultAppCapabilities();
				Process.Start(new ProcessStartInfo(deepLink) { UseShellExecute = true });
				return;
			}
			catch {
			}

			try {
				Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
			}
			catch {
			}
		}

	}

	internal sealed class SettingsVm : INotifyPropertyChanged {
		private static readonly EditSettings SettingsEditor = new EditSettings();
		private const string UpdateChannelRegistryValueName = "UpdateChannel";
		private const bool SortConfigVersionsByFileVersion = VersionDisplayHelper.SortByFileVersionOnly;
		private const string IncludeAlphaRegistryValueName = "IncludeAlpha";
		private UpdateChannel initialUpdateChannel;
		private AppUpdateChannel initialAppUpdateChannel;
		private bool initialAppUpdateAutoCheckEnabled;
		public SettingsVm() {
			RefreshUpdateChannelOptions();
			RefreshAppUpdateChannelOptions();
			SettingsGuard.Execute(() => {
				Autostartup = Properties.Settings.Default.Autostartup;
				MiddleMouseButtonEvent = Properties.Settings.Default.middleMouseButtonEvent;
				SelectedUpdateChannel = LoadUpdateChannelSetting();
				initialUpdateChannel = SelectedUpdateChannel;
				AppUpdateAutoCheckEnabled = Properties.Settings.Default.AppUpdateAutoCheckEnabled;
				initialAppUpdateAutoCheckEnabled = AppUpdateAutoCheckEnabled;
				SelectedAppUpdateChannel = LoadAppUpdateChannelSetting();
				initialAppUpdateChannel = SelectedAppUpdateChannel;
				AlwaysShowSelectionDialog = Properties.Settings.Default.alwaysShowSelectionDialog;
				UseLatestVersion = Properties.Settings.Default.useLatestVersion;
				LoadInstalledVersions();

				LoadPaths(
					DeserializePaths(Properties.Settings.Default.SavedPaths),
					DeserializePaths(Properties.Settings.Default.projectsFolderPath));
			}, () => {
				Autostartup = true;
				MiddleMouseButtonEvent = false;
				SelectedUpdateChannel = UpdateChannel.Release;
				initialUpdateChannel = SelectedUpdateChannel;
				AppUpdateAutoCheckEnabled = true;
				initialAppUpdateAutoCheckEnabled = AppUpdateAutoCheckEnabled;
				SelectedAppUpdateChannel = AppUpdateChannel.Stable;
				initialAppUpdateChannel = SelectedAppUpdateChannel;
				AlwaysShowSelectionDialog = false;
				UseLatestVersion = false;
				LoadInstalledVersions();
				LoadPaths(Array.Empty<string>(), Array.Empty<string>());
			});
		}

		public string LangSettings => Lang.Settings;
		public string LangWindowsAutostartup => Lang.WindowsAutostartup;
		public string LangRunAtWindowsStartup => Lang.RunAtWindowsStartup;
		public string LangAlwaysShowVersionDialog => Lang.AlwaysShowVersionDialog;
		public string LangInstallationpaths => Lang.Installationpaths;
		public string LangConfigDirectoryPaths => Lang.ConfigDirectoryPaths;
		public string LangProjectDirectorypaths => Lang.ProjectDirectorypaths;
		public string LangSectionGeneral => Lang.SettingsSectionGeneral;
		public string LangSectionPaths => Lang.SettingsSectionPaths;
		public string LangSectionAdvanced => Lang.SettingsSectionAdvanced;
		public string LangAlwaysShowDialogHint => Lang.SettingsAlwaysShowDialogHint;
		public string LangProjectFoldersHint => Lang.SettingsProjectFoldersHint;
		public string LangNoInstallationPathTitle => Lang.NoInstallationPath_Title;
		public string LangNoInstallationPathDescription => Lang.NoInstallationPath_Description;
		public string LangNoInstallationPathUseSuggestedButton => Lang.NoInstallationPath_UseSuggestedButton;
		public string DefaultConfigPathLabel => Lang.DefaultConfigPathLabel;
		public string LangNoProjectPathTitle => Lang.NoProjectPath_Title;
		public string LangNoProjectPathDescription => Lang.NoProjectPath_Description;
		public string LangNoProjectPathUseSuggestedButton => Lang.NoProjectPath_UseSuggestedButton;
		public string DefaultProjectPathLabel => Lang.DefaultProjectPathLabel;
		public string LangBrowse => Lang.Browse;
		public string LangDelete => Lang.Delete;
		public string LangMiddleMousePressing => Lang.TerminateAllLoxoneConfigInstances_Title;
		public string LangCloseAllOpenConfig => Lang.TerminateAllLoxoneConfigInstances_Description;
		public string LangUpdateChannelTitle => Lang.SettingsUpdateChannelTitle;
		public string LangUpdateChannelHint => Lang.SettingsUpdateChannelHint;
		public string LangUpdateChannelRelease => Lang.SettingsUpdateChannel_Release;
		public string LangUpdateChannelBeta => Lang.SettingsUpdateChannel_Beta;
		public string LangUpdateChannelAlpha => Lang.SettingsUpdateChannel_Alpha;
		public string AppUpdatesSectionTitle => Lang.AppUpdate_SettingsSection;
		public string AppUpdatesAutomaticTitle => Lang.AppUpdate_AutomaticTitle;
		public string AppUpdatesAutomaticHint => Lang.AppUpdate_AutomaticHint;
		public string AppUpdatesChannelTitle => Lang.AppUpdate_ChannelTitle;
		public string AppUpdatesChannelHint => Lang.AppUpdate_ChannelHint;
		public string AppUpdatesCheckNow => Lang.AppUpdate_CheckNow;
		public string LangSave => Lang.Save;
		public string LangCancel => Lang.Cancel;
		public string ApplicationName => GetApplicationName();
		public string ApplicationVersionDisplay => string.Format(Lang.ApplicationVersionFormat, GetApplicationVersion());
		public string LaunchModeTitle => Lang.LaunchModeTitle;
		public string LaunchModeHint => Lang.LaunchModeHint;
		public string UseLatestVersionOptionTitle => Lang.UseLatestVersionOptionTitle;
		public string UseLatestVersionOptionHint => Lang.UseLatestVersionOptionHint;
		public string UseFixedVersionOptionTitle => Lang.UseFixedVersionOptionTitle;
		public string UseFixedVersionOptionHint => Lang.UseFixedVersionOptionHint;
		public string FileAssociationTitle => Lang.Settings_FileAssociation_Title;
		public string FileAssociationDescription => Lang.Settings_FileAssociation_Description;
		public string FileAssociationOpenWindowsButton => Lang.Settings_FileAssociation_OpenWindowsButton;
		public string FileAssociationStatusLabel => Lang.Settings_FileAssociation_Current;

		public ObservableCollection<string> ConfigPaths { get; } = new ObservableCollection<string>();
		public ObservableCollection<string> ProjectPaths { get; } = new ObservableCollection<string>();
		public ObservableCollection<InstalledVersionOption> InstalledVersions { get; } = new ObservableCollection<InstalledVersionOption>();
		public ObservableCollection<UpdateChannelOption> UpdateChannels { get; } = new ObservableCollection<UpdateChannelOption>();
		public ObservableCollection<AppUpdateChannelOption> AppUpdateChannels { get; } = new ObservableCollection<AppUpdateChannelOption>();

		private static string GetApplicationVersion() {
			Assembly assembly = typeof(SettingsVm).Assembly;
			string informationalVersion = assembly
				.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
				.InformationalVersion;

			if (!string.IsNullOrWhiteSpace(informationalVersion)) {
				int metadataSeparator = informationalVersion.IndexOf('+');
				return metadataSeparator >= 0
					? informationalVersion.Substring(0, metadataSeparator)
					: informationalVersion;
			}

			Version assemblyVersion = assembly.GetName().Version;
			return assemblyVersion == null
				? string.Empty
				: $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{Math.Max(assemblyVersion.Build, 0)}";
		}

		private static string GetApplicationName() {
			Assembly assembly = typeof(SettingsVm).Assembly;
			string productName = assembly
				.GetCustomAttribute<AssemblyProductAttribute>()?
				.Product;

			return string.IsNullOrWhiteSpace(productName)
				? assembly.GetName().Name ?? string.Empty
				: productName;
		}

		private static readonly string DefaultConfigPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
			InitialSetup.LoxoneFolderName);
		private static readonly string DefaultProjectPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
			InitialSetup.LoxoneFolderName,
			InitialSetup.LoxoneConfigFolderName,
			InitialSetup.ProjectsFolderName);

		public string DefaultConfigPathValue => DefaultConfigPath;
		public string DefaultProjectPathValue => DefaultProjectPath;
		public bool CanUseSuggestedConfigPath => Directory.Exists(DefaultConfigPath);
		public bool CanUseSuggestedProjectPath => Directory.Exists(DefaultProjectPath);

		private string _selectedConfigPath;
		public string SelectedConfigPath {
			get => _selectedConfigPath;
			set { _selectedConfigPath = value; OnPropertyChanged(); }
		}

		private string _selectedProjectPath;
		public string SelectedProjectPath {
			get => _selectedProjectPath;
			set { _selectedProjectPath = value; OnPropertyChanged(); }
		}

		private bool _autostartup;
		public bool Autostartup { get => _autostartup; set { _autostartup = value; OnPropertyChanged(); } }

		private bool _middleMouseButtonEvent;
		public bool MiddleMouseButtonEvent { get => _middleMouseButtonEvent; set { _middleMouseButtonEvent = value; OnPropertyChanged(); } }

		private UpdateChannel _selectedUpdateChannel;
		public UpdateChannel SelectedUpdateChannel {
			get => _selectedUpdateChannel;
			set {
				if (_selectedUpdateChannel == value) {
					return;
				}

				_selectedUpdateChannel = value;
				OnPropertyChanged();
			}
		}

		private bool _appUpdateAutoCheckEnabled;
		public bool AppUpdateAutoCheckEnabled { get => _appUpdateAutoCheckEnabled; set { _appUpdateAutoCheckEnabled = value; OnPropertyChanged(); } }

		private AppUpdateChannel _selectedAppUpdateChannel;
		public AppUpdateChannel SelectedAppUpdateChannel {
			get => _selectedAppUpdateChannel;
			set {
				if (_selectedAppUpdateChannel == value) return;
				_selectedAppUpdateChannel = value;
				OnPropertyChanged();
			}
		}

		private string _appUpdateStatus = Lang.AppUpdate_StatusIdle;
		public string AppUpdateStatus { get => _appUpdateStatus; private set { _appUpdateStatus = value; OnPropertyChanged(); } }
		private string _appUpdateLastCheck;
		public string AppUpdateLastCheck { get => _appUpdateLastCheck; private set { _appUpdateLastCheck = value; OnPropertyChanged(); } }
		private string _appUpdateActionLabel = Lang.AppUpdate_CheckNow;
		public string AppUpdateActionLabel { get => _appUpdateActionLabel; private set { _appUpdateActionLabel = value; OnPropertyChanged(); } }
		private string _appUpdateFooterText;
		public string AppUpdateFooterText { get => _appUpdateFooterText; private set { _appUpdateFooterText = value; OnPropertyChanged(); } }
		private Visibility _appUpdateFooterVisibility = Visibility.Collapsed;
		public Visibility AppUpdateFooterVisibility { get => _appUpdateFooterVisibility; private set { _appUpdateFooterVisibility = value; OnPropertyChanged(); } }
		private bool _appUpdateFooterActionEnabled;
		public bool AppUpdateFooterActionEnabled { get => _appUpdateFooterActionEnabled; private set { _appUpdateFooterActionEnabled = value; OnPropertyChanged(); } }
		private Visibility _appUpdateActionVisibility = Visibility.Collapsed;
		public Visibility AppUpdateActionVisibility { get => _appUpdateActionVisibility; private set { _appUpdateActionVisibility = value; OnPropertyChanged(); } }

		public void ApplyAppUpdateSnapshot(AppUpdateSnapshot snapshot) {
			if (snapshot == null) return;
			string version = snapshot.Release?.Version.ToString();
			switch (snapshot.State) {
				case AppUpdateState.Checking: AppUpdateStatus = Lang.AppUpdate_Checking; break;
				case AppUpdateState.UpToDate: AppUpdateStatus = string.Format(Lang.AppUpdate_UpToDate, snapshot.CurrentVersion); break;
				case AppUpdateState.UpdateAvailable: AppUpdateStatus = string.Format(Lang.AppUpdate_StatusAvailable, version); break;
				case AppUpdateState.Downloading: AppUpdateStatus = Lang.AppUpdate_Downloading; break;
				case AppUpdateState.Verifying: AppUpdateStatus = Lang.AppUpdate_Verifying; break;
				case AppUpdateState.StartingInstaller: AppUpdateStatus = Lang.AppUpdate_StartingInstaller; break;
				case AppUpdateState.Failed: AppUpdateStatus = Lang.AppUpdate_Failed; break;
				default: AppUpdateStatus = Lang.AppUpdate_StatusIdle; break;
			}
			AppUpdateLastCheck = snapshot.LastCheckUtc == DateTime.MinValue ? Lang.AppUpdate_NeverChecked : string.Format(Lang.AppUpdate_LastChecked, snapshot.LastCheckUtc.ToLocalTime());
			AppUpdateActionLabel = Lang.AppUpdate_UpdateNow;
			switch (snapshot.State) {
				case AppUpdateState.UpdateAvailable:
					AppUpdateFooterText = string.Format(Lang.AppUpdate_AvailableInstall, version);
					break;
				case AppUpdateState.Downloading:
					AppUpdateFooterText = string.Format(Lang.AppUpdate_FooterDownloading, version);
					break;
				case AppUpdateState.Verifying:
					AppUpdateFooterText = Lang.AppUpdate_Verifying;
					break;
				case AppUpdateState.StartingInstaller:
					AppUpdateFooterText = Lang.AppUpdate_StartingInstaller;
					break;
				default:
					AppUpdateFooterText = string.Empty;
					break;
			}
			AppUpdateFooterVisibility = snapshot.State == AppUpdateState.UpdateAvailable
				|| snapshot.State == AppUpdateState.Downloading
				|| snapshot.State == AppUpdateState.Verifying
				|| snapshot.State == AppUpdateState.StartingInstaller
				? Visibility.Visible
				: Visibility.Collapsed;
			AppUpdateFooterActionEnabled = snapshot.State == AppUpdateState.UpdateAvailable;
			AppUpdateActionVisibility = snapshot.State == AppUpdateState.UpdateAvailable
				? Visibility.Visible
				: Visibility.Collapsed;
		}

		private bool _alwaysShowSelectionDialog;
		public bool AlwaysShowSelectionDialog { get => _alwaysShowSelectionDialog; set { _alwaysShowSelectionDialog = value; OnPropertyChanged(); } }

		private bool _useLatestVersion;
		public bool UseLatestVersion {
			get => _useLatestVersion;
			set {
				if (_useLatestVersion == value)
					return;
				_useLatestVersion = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(UseFixedVersion));
			}
		}

		public bool UseFixedVersion {
			get => !_useLatestVersion;
			set {
				if (value == !_useLatestVersion)
					return;
				UseLatestVersion = !value;
			}
		}

		public void RefreshLaunchModeBindings() {
			OnPropertyChanged(nameof(UseLatestVersion));
			OnPropertyChanged(nameof(UseFixedVersion));
		}

		private InstalledVersionOption _selectedFixedVersion;
		public InstalledVersionOption SelectedFixedVersion { get => _selectedFixedVersion; set { _selectedFixedVersion = value; OnPropertyChanged(); } }

		public string LatestVersionLabel => Lang.LatestInstalledVersionLabel;

		private string _latestVersionValue;
		public string LatestVersionValue { get => _latestVersionValue; private set { _latestVersionValue = value; OnPropertyChanged(); } }

		private string _fileAssociationStatus;
		public string FileAssociationStatus { get => _fileAssociationStatus; private set { _fileAssociationStatus = value; OnPropertyChanged(); } }

		private string _fileAssociationStatusValue;
		public string FileAssociationStatusValue { get => _fileAssociationStatusValue; private set { _fileAssociationStatusValue = value; OnPropertyChanged(); } }

		private bool _isFileAssociationMixed;
		public bool IsFileAssociationMixed { get => _isFileAssociationMixed; private set { _isFileAssociationMixed = value; OnPropertyChanged(); } }

		private bool _isFileAssociationCurrent;
		public bool IsFileAssociationCurrent { get => _isFileAssociationCurrent; private set { _isFileAssociationCurrent = value; OnPropertyChanged(); } }

		private string _configMessage;
		public string ConfigMessage { get => _configMessage; private set { _configMessage = value; OnPropertyChanged(); } }

		private Visibility _configMessageVisibility = Visibility.Collapsed;
		public Visibility ConfigMessageVisibility { get => _configMessageVisibility; private set { _configMessageVisibility = value; OnPropertyChanged(); } }

		private string _projectsMessage;
		public string ProjectsMessage { get => _projectsMessage; private set { _projectsMessage = value; OnPropertyChanged(); } }

		private Visibility _projectsMessageVisibility = Visibility.Collapsed;
		public Visibility ProjectsMessageVisibility { get => _projectsMessageVisibility; private set { _projectsMessageVisibility = value; OnPropertyChanged(); } }

		private UpdateChannel LoadUpdateChannelSetting() {
			int storedValue = RegistryFlagReader.GetDwordValue(UpdateChannelRegistryValueName, defaultValue: int.MinValue);
			if (Enum.IsDefined(typeof(UpdateChannel), storedValue)) {
				return (UpdateChannel)storedValue;
			}

			bool includeAlpha = RegistryFlagReader.GetDwordFlag(IncludeAlphaRegistryValueName, defaultValue: false);
			return includeAlpha ? UpdateChannel.Alpha : UpdateChannel.Release;
		}

		private static AppUpdateChannel LoadAppUpdateChannelSetting() {
			int value = Properties.Settings.Default.AppUpdateChannel;
			return Enum.IsDefined(typeof(AppUpdateChannel), value) ? (AppUpdateChannel)value : AppUpdateChannel.Stable;
		}

		private void RefreshUpdateChannelOptions() {
			UpdateChannels.Clear();
			UpdateChannels.Add(new UpdateChannelOption(LangUpdateChannelRelease, UpdateChannel.Release));
			UpdateChannels.Add(new UpdateChannelOption(LangUpdateChannelBeta, UpdateChannel.Beta));
			UpdateChannels.Add(new UpdateChannelOption(LangUpdateChannelAlpha, UpdateChannel.Alpha));
		}

		private void RefreshAppUpdateChannelOptions() {
			AppUpdateChannels.Clear();
			AppUpdateChannels.Add(new AppUpdateChannelOption(Lang.AppUpdate_ChannelStable, AppUpdateChannel.Stable));
			AppUpdateChannels.Add(new AppUpdateChannelOption(Lang.AppUpdate_ChannelBeta, AppUpdateChannel.Beta));
			AppUpdateChannels.Add(new AppUpdateChannelOption(Lang.AppUpdate_ChannelAlpha, AppUpdateChannel.Alpha));
		}

		public void LoadPaths(IEnumerable<string> configPaths, IEnumerable<string> projectPaths) {
			ConfigPaths.Clear();
			if (configPaths != null) {
				foreach (var path in configPaths.Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p)))
					ConfigPaths.Add(path);
			}

			ProjectPaths.Clear();
			if (projectPaths != null) {
				foreach (var path in projectPaths.Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p)))
					ProjectPaths.Add(path);
			}

			SelectedConfigPath = ConfigPaths.FirstOrDefault();
			SelectedProjectPath = ProjectPaths.FirstOrDefault();
		}

		public void RemoveSelectedConfig() => RemoveConfigPath(SelectedConfigPath);

		public void RemoveConfigPath(string path) {
			if (path == null)
				return;

			var idx = ConfigPaths.IndexOf(path);
			if (idx < 0)
				return;

			var wasSelected = SelectedConfigPath == path;
			ConfigPaths.RemoveAt(idx);
			if (wasSelected)
				SelectedConfigPath = ConfigPaths.Count > 0 ? ConfigPaths[Math.Min(idx, ConfigPaths.Count - 1)] : null;
		}

		public void RemoveSelectedProject() => RemoveProjectPath(SelectedProjectPath);

		public void RemoveProjectPath(string path) {
			if (path == null)
				return;

			var idx = ProjectPaths.IndexOf(path);
			if (idx < 0)
				return;

			var wasSelected = SelectedProjectPath == path;
			ProjectPaths.RemoveAt(idx);
			if (wasSelected)
				SelectedProjectPath = ProjectPaths.Count > 0 ? ProjectPaths[Math.Min(idx, ProjectPaths.Count - 1)] : null;
		}

		public void UseSuggestedConfigPath() {
			AddConfigPath(DefaultConfigPath);
		}

		public void UseSuggestedProjectPath() {
			AddProjectPath(DefaultProjectPath);
		}

		private void AddConfigPath(string path) {
			if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
				return;

			if (!ConfigPaths.Contains(path)) {
				ConfigPaths.Add(path);
				SelectedConfigPath = path;
			}
			else {
				SelectedConfigPath = path;
				_ = ShowConfigExistsMessage();
			}
		}

		private void AddProjectPath(string path) {
			if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
				return;

			if (!ProjectPaths.Contains(path)) {
				ProjectPaths.Add(path);
				SelectedProjectPath = path;
			}
			else {
				SelectedProjectPath = path;
				_ = ShowProjectsExistsMessage();
			}
		}

		public async Task ShowConfigExistsMessage() {
			ConfigMessage = Lang.PathAlreadyExists;
			ConfigMessageVisibility = Visibility.Visible;
			await Task.Delay(2500);
			ConfigMessageVisibility = Visibility.Collapsed;
		}

		public async Task ShowProjectsExistsMessage() {
			ProjectsMessage = Lang.PathAlreadyExists;
			ProjectsMessageVisibility = Visibility.Visible;
			await Task.Delay(2500);
			ProjectsMessageVisibility = Visibility.Collapsed;
		}

		public void RefreshFileAssociations() {
			FileAssociationStatusValue = BuildCombinedAssociationStatus(".loxone", ".loxone.backup");
			FileAssociationStatus = $"{Lang.Settings_FileAssociation_Current} {FileAssociationStatusValue}";
			IsFileAssociationMixed = string.Equals(FileAssociationStatusValue, Lang.Settings_FileAssociation_Mixed, StringComparison.OrdinalIgnoreCase);
			IsFileAssociationCurrent = IsAssociatedWithCurrentApp(".loxone")
				&& IsAssociatedWithCurrentApp(".loxone.backup");
		}

		public void SaveToSettings() {
			bool updateChannelChanged = SelectedUpdateChannel != initialUpdateChannel;
			bool updateChannelPersisted = !updateChannelChanged;
			bool appUpdateSettingsChanged = SelectedAppUpdateChannel != initialAppUpdateChannel
				|| AppUpdateAutoCheckEnabled != initialAppUpdateAutoCheckEnabled;

			if (updateChannelChanged) {
				bool wrote = RegistryFlagReader.SetDwordValue(UpdateChannelRegistryValueName, (int)SelectedUpdateChannel);
				if (wrote) {
					int persistedValue = RegistryFlagReader.GetDwordValue(UpdateChannelRegistryValueName, defaultValue: (int)initialUpdateChannel);
					updateChannelPersisted = persistedValue == (int)SelectedUpdateChannel;
					RegistryFlagReader.SetDwordFlag(IncludeAlphaRegistryValueName, SelectedUpdateChannel == UpdateChannel.Alpha);
				}
			}
			SettingsGuard.Execute(() => {
				Properties.Settings.Default.Autostartup = Autostartup;
				Properties.Settings.Default.middleMouseButtonEvent = MiddleMouseButtonEvent;
				Properties.Settings.Default.alwaysShowSelectionDialog = AlwaysShowSelectionDialog;
				Properties.Settings.Default.useLatestVersion = UseLatestVersion;
				Properties.Settings.Default.AppUpdateAutoCheckEnabled = AppUpdateAutoCheckEnabled;
				Properties.Settings.Default.AppUpdateChannel = (int)SelectedAppUpdateChannel;

				if (!UseLatestVersion && SelectedFixedVersion != null) {
					Properties.Settings.Default.defaultVersion = SelectedFixedVersion.Path;
				}

				Properties.Settings.Default.SavedPaths = SerializePaths(ConfigPaths);
				Properties.Settings.Default.projectsFolderPath = SerializePaths(ProjectPaths);

				Properties.Settings.Default.Save();
			});
			if (appUpdateSettingsChanged) {
				initialAppUpdateChannel = SelectedAppUpdateChannel;
				initialAppUpdateAutoCheckEnabled = AppUpdateAutoCheckEnabled;
				ContextMenuManager.AppUpdateService?.ApplySettings(AppUpdateAutoCheckEnabled, SelectedAppUpdateChannel);
			}

			if (updateChannelChanged && updateChannelPersisted) {
				initialUpdateChannel = SelectedUpdateChannel;
				ContextMenuManager.RefreshUpdateChannelMenuItem(SelectedUpdateChannel);
				ContextMenuManager.UpdateCheckService?.RequestCheck(SelectedUpdateChannel);
			} else if (updateChannelChanged && !updateChannelPersisted) {
				SelectedUpdateChannel = initialUpdateChannel;
			}
		}

		private void LoadInstalledVersions() {
			InstalledVersions.Clear();

			var allVersions = ContextMenuManager.AllConfigVersions;
			if (allVersions != null) {
				var entries = new List<(string Name, string Path, string DisplayName, Version ParsedVersion)>();
				foreach (var entry in allVersions) {
					string exePath = Path.Combine(entry.Value ?? string.Empty, "LoxoneConfig.exe");
					var versions = VersionDisplayHelper.ReadExeVersions(exePath);
					string displayName = VersionDisplayHelper.BuildDisplayName(entry.Key, versions.Item1, versions.Item2);
					Version parsed = VersionDisplayHelper.GetSortVersion(versions.Item1, versions.Item2, SortConfigVersionsByFileVersion);
					entries.Add((entry.Key, entry.Value, displayName, parsed));
				}

				IEnumerable<(string Name, string Path, string DisplayName, Version ParsedVersion)> ordered = SortConfigVersionsByFileVersion
					? entries.OrderBy(e => e.ParsedVersion == null)
						.ThenBy(e => e.ParsedVersion ?? new Version(0, 0))
						.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
					: entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

				foreach (var entry in ordered) {
					InstalledVersions.Add(new InstalledVersionOption(entry.DisplayName, entry.Path));
				}
			}

			UpdateLatestVersionDisplay();
			SelectDefaultFixedVersion();
		}

		private void UpdateLatestVersionDisplay() {
			if (InstalledVersions.Count == 0) {
				LatestVersionValue = Lang.NoInstalledConfigVersionsFound;
				return;
			}

			InstalledVersionOption latest = InstalledVersions
				.OrderByDescending(v => v.ParsedVersion != null)
				.ThenByDescending(v => v.ParsedVersion ?? new Version(0, 0))
				.ThenByDescending(v => v.DisplayName)
				.First();

			LatestVersionValue = latest.DisplayName;
		}

		private void SelectDefaultFixedVersion() {
			if (InstalledVersions.Count == 0)
				return;

			string savedDefault = SettingsGuard.Execute(
				() => Properties.Settings.Default.defaultVersion ?? string.Empty,
				string.Empty);
			var match = InstalledVersions.FirstOrDefault(v =>
				string.Equals(v.Path, savedDefault, StringComparison.OrdinalIgnoreCase));

			SelectedFixedVersion = match ?? InstalledVersions[0];
		}

		public sealed class UpdateChannelOption {
			public UpdateChannelOption(string displayName, UpdateChannel value) {
				DisplayName = displayName ?? string.Empty;
				Value = value;
			}

			public string DisplayName { get; }
			public UpdateChannel Value { get; }
		}

		public sealed class AppUpdateChannelOption {
			internal AppUpdateChannelOption(string displayName, AppUpdateChannel value) {
				DisplayName = displayName ?? string.Empty;
				Value = value;
			}

			public string DisplayName { get; }
			public object Value { get; }
		}

		private string BuildCombinedAssociationStatus(string primaryExtension, string secondaryExtension) {
			string primaryApp = QueryAssociationDisplayName(primaryExtension);
			string secondaryApp = QueryAssociationDisplayName(secondaryExtension);

			string statusValue;
			if (string.IsNullOrWhiteSpace(primaryApp) && string.IsNullOrWhiteSpace(secondaryApp)) {
				statusValue = Lang.Settings_FileAssociation_NotSet;
			}
			else if (string.Equals(primaryApp, secondaryApp, StringComparison.OrdinalIgnoreCase)) {
				statusValue = primaryApp ?? secondaryApp;
			}
			else {
				statusValue = Lang.Settings_FileAssociation_Mixed;
			}

			return statusValue;
		}

		private static string QueryAssociationDisplayName(string extension) {
			string appName = QueryAssociationString(AssocStr.FriendlyAppName, extension);
			if (string.IsNullOrWhiteSpace(appName)) {
				appName = QueryAssociationString(AssocStr.Executable, extension);
			}

			return string.IsNullOrWhiteSpace(appName) ? null : appName;
		}

		private static string QueryAssociationString(AssocStr assocStr, string extension) {
			try {
				uint length = 0;
				_ = AssocQueryString(AssocF.None, assocStr, extension, null, null, ref length);
				if (length == 0)
					return null;

				var builder = new StringBuilder((int)length);
				uint result = AssocQueryString(AssocF.None, assocStr, extension, null, builder, ref length);
				if (result != 0)
					return null;

				return builder.ToString();
			}
			catch {
				return null;
			}
		}

		private static bool IsAssociatedWithCurrentApp(string extension) {
			string associatedExecutable = QueryAssociationString(AssocStr.Executable, extension);
			if (string.IsNullOrWhiteSpace(associatedExecutable)) {
				return false;
			}

			string currentExecutable = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
			return string.Equals(associatedExecutable, currentExecutable, StringComparison.OrdinalIgnoreCase);
		}

		[Flags]
		private enum AssocF : uint {
			None = 0x00000000
		}

		private enum AssocStr {
			Executable = 2,
			FriendlyAppName = 4
		}

		[DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
		private static extern uint AssocQueryString(
			AssocF flags,
			AssocStr str,
			string pszAssoc,
			string pszExtra,
			StringBuilder pszOut,
			ref uint pcchOut);

		private static string SerializePaths(IEnumerable<string> values) =>
			SettingsEditor.serializeString(values?.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray());

		private static IEnumerable<string> DeserializePaths(string serialized) =>
			SettingsEditor.deserializeString(serialized ?? string.Empty);

		public event PropertyChangedEventHandler PropertyChanged;
		private void OnPropertyChanged([CallerMemberName] string name = null) =>
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}

	public sealed class InstalledVersionOption {
		public InstalledVersionOption(string displayName, string path) {
			DisplayName = displayName ?? string.Empty;
			Path = path ?? string.Empty;
			ParsedVersion = ParseVersion(displayName);
		}

		public string DisplayName { get; }
		public string Path { get; }
		public Version ParsedVersion { get; }

		private static Version ParseVersion(string input) {
			if (string.IsNullOrWhiteSpace(input))
				return null;

			string trimmed = input.Trim();
			Match match = Regex.Match(trimmed, @"\d+(\.\d+){0,3}");
			if (!match.Success)
				return null;

			if (Version.TryParse(match.Value, out Version version))
				return version;

			return null;
		}
	}

}
