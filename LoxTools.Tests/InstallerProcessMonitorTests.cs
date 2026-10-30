using LoxTools.UpdateCheck;
using LoxTools.UI.Tray;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LoxTools.Tests {
    [TestClass]
    public class InstallerProcessMonitorTests {
        [TestMethod]
        public async Task RejectedCloseReportsFailureAndKeepsWaitingForExit() {
            using (var cancellation = new CancellationTokenSource()) {
                var exit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var reported = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task operation = InstallerProcessMonitor.WaitAsync(
                    token => exit.Task.WaitAsync(token),
                    () => throw new System.ComponentModel.Win32Exception(5),
                    result => reported.SetResult(result), cancellation.Token);
                cancellation.Cancel();
                Assert.IsFalse(await reported.Task.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.IsFalse(operation.IsCompleted);
                exit.SetResult(true);
                await operation.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task CancellationRequestsCloseButWaitsForActualInstallerExit(bool acceptsClose) {
            using (var cancellation = new CancellationTokenSource()) {
                var exit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var reported = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                int closeRequests = 0;
                Task operation = InstallerProcessMonitor.WaitAsync(
                    token => exit.Task.WaitAsync(token),
                    () => { closeRequests++; return acceptsClose; },
                    result => reported.SetResult(result), cancellation.Token);
                cancellation.Cancel();
                Assert.AreEqual(acceptsClose, await reported.Task.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.IsFalse(operation.IsCompleted, "Cancellation must not release installer resources before exit.");
                exit.SetResult(true);
                await operation.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(1, closeRequests);
            }
        }

        [TestMethod]
        public async Task NormalExitDoesNotRequestCancellation() {
            int requests = 0;
            await InstallerProcessMonitor.WaitAsync(_ => Task.CompletedTask,
                () => { requests++; return true; }, _ => Assert.Fail(), CancellationToken.None);
            Assert.AreEqual(0, requests);
        }

        [TestMethod]
        public void DownloadLabelKeepsPercentageAcrossSpinnerFrames() {
            foreach (int percent in new[] { 0, 4, 100 }) {
                for (int frame = 0; frame < 4; frame++) {
                    string label = ContextMenuManager.FormatDownloadProgress(percent, frame);
                    StringAssert.Contains(label, percent + "%");
                    StringAssert.Contains(label, "/-\\|"[frame].ToString());
                    Assert.IsFalse(label.Contains("Loxone Config"));
                }
            }
        }
    }
}
