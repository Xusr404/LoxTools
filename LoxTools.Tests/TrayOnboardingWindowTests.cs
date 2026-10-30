using Microsoft.VisualStudio.TestTools.UnitTesting;
using LoxTools.Language;
using System.Windows;
using System.Windows.Controls;
using System.Configuration;

namespace LoxTools.Tests {
    [TestClass]
    public class TrayOnboardingWindowTests {
        [TestMethod]
        public void TestArgumentEnablesOnboardingMode() {
            bool enabled = LoxToolsApplication.IsTrayOnboardingTestRequested(
                new[] { "--test-tray-onboarding" });

            Assert.IsTrue(enabled);
        }

        [STATestMethod]
        public void WindowCanBeConstructed() {
            var window = new TrayOnboardingWindow();

            Assert.IsNotNull(window.Content);
            Assert.AreSame(window, window.DataContext);
            Assert.IsTrue(window.Width > 0);
            Assert.IsTrue(window.Height > 0);

            window.Close();
        }

        [STATestMethod]
        public void InitialGuidePrimaryActionAcknowledgesSettingsStep() {
            var window = new TrayOnboardingWindow(opensSettings: true);
            Assert.AreEqual(Lang.TrayOnboarding_DoneOpenSettings, window.DoneText);
            Assert.IsTrue(window.OpensSettings);
            Assert.IsFalse(window.WasAcknowledged);
            var button = (Button)window.FindName("DoneButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsTrue(window.WasAcknowledged);
        }

        [STATestMethod]
        public void ManualGuideDoesNotRequestSettings() {
            var window = new TrayOnboardingWindow();
            Assert.AreEqual(Lang.TrayOnboarding_Done, window.DoneText);
            Assert.IsFalse(window.OpensSettings);
            ((Button)window.FindName("DoneButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsTrue(window.WasAcknowledged);
        }

        [STATestMethod]
        public void DismissingInitialGuideDoesNotAcknowledgeIt() {
            var window = new TrayOnboardingWindow(opensSettings: true);
            window.Close();
            Assert.IsFalse(window.WasAcknowledged);
        }

        [TestMethod]
        public void PendingSettingsMarkerDefaultsFalseAndRoundTripsWithoutSavingUserSettings() {
            var property = new LoxTools.Properties.Settings().Properties["InitialSettingsPending"];
            Assert.AreEqual("False", property.DefaultValue);
            foreach (bool pending in new[] { true, false }) {
                var stored = new SettingsPropertyValue(property) { PropertyValue = pending };
                var loaded = new SettingsPropertyValue(property) { SerializedValue = stored.SerializedValue };
                Assert.AreEqual(pending, loaded.PropertyValue);
            }
        }
    }
}
