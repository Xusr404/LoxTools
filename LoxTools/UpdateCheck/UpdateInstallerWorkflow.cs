using LoxTools.Models;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LoxTools.UpdateCheck {
    internal sealed class UpdateInstallerWorkflow {
        private readonly UpdateInstallerPipeline pipeline;
        private readonly Action<string> shortcutWriter;
        private readonly Func<string, Process> processLauncher;

        public UpdateInstallerWorkflow(
            UpdateInstallerPipeline pipeline,
            Action<string> shortcutWriter,
            Func<string, Process> processLauncher) {
            this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            this.shortcutWriter = shortcutWriter ?? throw new ArgumentNullException(nameof(shortcutWriter));
            this.processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        }

        public async Task<Process> ExecuteAsync(
            UpdateInfo target,
            string workingFolder,
            CancellationToken cancellationToken) {
            string installerPath = await pipeline.PrepareAsync(target, workingFolder, cancellationToken).ConfigureAwait(false);
            shortcutWriter(installerPath);
            return processLauncher(installerPath);
        }
    }
}
