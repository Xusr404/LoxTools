using System;
using System.Threading;
using System.Threading.Tasks;

namespace LoxTools.UpdateCheck {
    internal static class InstallerProcessMonitor {
        // Request the installer's normal cancellation/rollback path. Never kill it or
        // stop waiting: its files must remain available until it actually exits.
        internal static async Task WaitAsync(Func<CancellationToken, Task> waitForExit,
            Func<bool> requestClose, Action<bool> cancellationReported, CancellationToken cancellationToken) {
            try {
                await waitForExit(cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                bool requested = false;
                try { requested = requestClose(); }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
                cancellationReported(requested);
                await waitForExit(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
