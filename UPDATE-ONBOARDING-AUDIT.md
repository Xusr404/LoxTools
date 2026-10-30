# Update and onboarding audit — 2026-10-29

## Review corrections — 2026-10-30

The table below records the earlier implementation pass. Subsequent review changed the
feed to HTTPS only, removed the tray cancellation action during silent installation,
and registered successful installation roots before refreshing discovery and watchers.
Rejected installer close requests now retain the Installing status rather than asking
the user to confirm in a hidden installer. Real installation and visual smoke tests
remain outstanding.

This is a **partial completion pass**, not a declaration that the requested task is complete.
Existing uncommitted onboarding, Settings, themes, and project changes were preserved.

## Requirement audit

| Requirement | Initial status | Final status | Changes made / outstanding work |
|---|---|---|---|
| HTTP primary, HTTPS fallback | Implemented but needs correction | Implemented; simulated response tests pass | Restored HTTP primary; separate 15-second endpoint timeouts; HTTPS on transport errors, timeouts and non-success responses. External cancellation does not initiate fallback. Timeout/transport cases need dedicated regression tests. |
| Download cancellation and retry | Partially implemented | Partially implemented | Active item cancels actual token; per-operation folder cleanup; retry after cancellation; timeout distinguished from user cancellation. Rapid UI interactions require manual testing. |
| Download integrity and existing files | Implemented but needs correction | Implemented and verified | Failed downloads and extraction collisions preserve pre-existing files. CRC mismatch prevents signature verification/execution. |
| Silent EXE/MSI installation | Missing for Config updates | Implemented for current EXE workflow; real installation unverified | EXE runs elevated with very-silent, prompt suppression, startup-prompt suppression, no restart, explicit destination/group and retained installer log. UAC cancellation is distinguished from failure. MSI remains unsupported. |
| Fixed/versioned destinations | Missing | Implemented for current EXE workflow; real installation unverified | Settings now saves VersionedInstallationFolders (default false). Installer destination and Start Menu group use the selected mode and channel. Existing folders are preserved; MSI remains unsupported. |
| Status and diagnostics | Partially implemented | Partially implemented | Added download percentage, verifying, extracting, cancelled, installing, completed states; persistent update.log; middle-click works outside download state. Failure is still a generic visible state rather than distinct download/install failure labels. |
| Background/concurrency | Implemented but needs correction | Partially implemented | Preserve visible failure across background checks and local scan notifications; hold operation lock while installer runs; avoid disposing semaphore while tasks can release it. Full shutdown/timer and multi-process stress tests remain. |
| Leading-zero checksums | Implemented but needs correction | Implemented and verified | Accept 1–8 hex digits, optional 0x, case insensitive; normalize to eight digits. Reject malformed/overlong checksums. Regression coverage added. |
| First-start Settings | Implemented but needs correction | Implemented; end-to-end first-launch smoke test outstanding | First-run setup persists InitialSettingsPending before its registry marker. The introduction's primary button opens Settings; opening Settings clears pending state and completes onboarding independently of saving. Manual guide reopens do not open Settings. Button behavior and marker serialization have regression tests. |
| Debug onboarding reset profile | Partially implemented | Partially implemented | Existing profile previews tray onboarding without reset. Destructive reset of settings, update preferences, marker and autostart is not implemented, and profile activation is not Debug-only. |
| Tray / Settings interactions | Partially implemented | Partially implemented | Download item enabled for cancellation and click requests menu retention; middle-click opens diagnostics. Manual tray and Settings smoke tests not performed. |
| Installer cleanup / shortcuts | Implemented but needs correction | Implemented; legacy shortcuts not migrated | ZIP deleted after successful installer verification; payload folder deleted after installer exit on success, failure or cancellation. Logs retained outside working folders. Startup cleanup protects active file leases and recorded installer process identities. Existing older shortcuts are not migrated. |
| Localization / documentation | Partially implemented | Partially implemented | Added English/German cancellation, extracting, installing and completed labels; README describes current behavior and missing features. No visual/theme QA performed. |

## Implementation summary

- `UpdateCheckXmlClient.cs`, `UpdateSecurityPolicy.cs`: HTTP primary and HTTPS fallback with per-endpoint limits.
- `UpdateCheckXmlParser.cs`: leading-zero CRC32 normalization with strict validation.
- `BoundedFileDownloader.cs`: report actual byte percentage; delete only files created by this attempt.
- `SecureInstallerExtractor.cs`: preserve existing installer on extraction collision.
- `UpdateInstallerPipeline.cs`, `UpdateInstallerWorkflow.cs`: phase reporting and cancellation checks before verification, extraction and execution.
- `UpdateCheckService.cs`: actual cancellation, installer process waiting/exit-code evaluation, persistent logs, sticky background failures, stable diagnostics folder and shortcuts.
- `UpdateLog.cs`: serialized persistent update.log writer.
- `ContextMenuManager.cs`: cancellation enabled, phase/progress labels, menu retention request, diagnostics access.
- Both language resource files and generated accessor: new localized statuses.
- Four existing test files plus extractor tests: regression coverage; existing metadata endpoint expectation updated.
- `README.md`: documented actual behavior and limitations.

## Validation

MSBuild is used because of COM interop. The normal Debug output is locked by a running
LoxTools instance, so validation builds use `artifacts/audit/Debug` and `artifacts/audit/Release`.
No running user application was terminated.

No real installer, elevation, production settings reset, autostart modification or production
installation-directory change was performed. Automated tests use mocked HTTP responses,
temporary files, synthetic archives and test signature verification. Test runner IPC needed
execution outside the restricted environment; the restricted runs were interrupted.

Build/test results are reported in the accompanying chat and TRX files under
`artifacts/audit/`. Existing nullable-context and unused-field compiler warnings remain.
One restore reported unavailable NuGet vulnerability metadata; cached dependencies restored.

## Remaining validation and implementation

The missing/partial rows above are outstanding implementation work, not merely manual QA.
EXE destination arguments and all six channel/folder-mode combinations now have regression tests.
MSI argument verification remains outstanding because the current package pipeline accepts EXE only.
No manual Settings add/remove/save, selector launch, tray click/middle-click, cancellation/retry,
theme-switch, or first-start smoke testing was performed. Onboarding window construction is
not evidence of onboarding persistence or real tray behavior.

Installer exit zero is observed, but installed payload/version is not independently verified;
bootstrapper child-process behavior requires real package investigation. Cancellable extraction
and CRC calculation currently check cancellation between phases, not within their synchronous loops.
