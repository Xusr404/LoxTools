# LoxTools

LoxTools is a Windows system-tray application that brings useful Loxone-related tools together in one place.

It provides quick access to Loxone projects, manages multiple Loxone Config installations, controls which version opens a project, and checks for available Loxone Config updates.

## Features

- Detect multiple Loxone Config installations
- Open Loxone projects from the system tray
- Use the latest installed or a fixed Loxone Config version
- Select a version for an individual project launch
- Monitor installation and project directories automatically
- Support `.loxone` and `.loxone.backup` file associations
- Check for Release, Beta, and Alpha Loxone Config updates
- Start automatically with Windows
- Close all running Loxone Config instances from the tray
- English and German user interfaces

## Getting started

Start LoxTools and find its icon in the Windows system tray.

On first start, LoxTools checks the common Loxone installation and project locations and adds them automatically when available.

The tray introduction appears first. Select **Got it — Open Settings** to review paths,
launch behavior, update channel, and installation folder mode in the existing Settings window.
Opening Settings completes onboarding even if you close it without saving. Later launches
do not automatically reopen either window. If you dismiss the initial guide with its close
button, setup remains pending and the guide returns on the next launch.
Reopening the guide manually uses **Got it** and does not open Settings afterward.
The **Onboarding Test** launch profile previews the introduction → Settings sequence without
resetting saved settings or changing the onboarding completion markers.

If your files are stored elsewhere:

1. Double-click the tray icon to open Settings.
2. Add the required installation and project paths.
3. Select **Save**.

## Opening Loxone Config

Open the tray menu to see the detected Loxone Config installations.

- Click a version to start it.
- Middle-click a version to open its installation directory.

## Opening projects

Discovered projects are available from the tray menu.

- Left-click a project to open it using the configured launch mode.
- Hold `Shift` while clicking to choose a Loxone Config version.
- Right-click a project to open the version selection dialog.
- Middle-click a project to open its containing directory.

## Launch modes

### Latest installed version

Projects are opened with the latest detected Loxone Config installation.

### Fixed version

Projects are opened with one selected Loxone Config installation.

The launch mode and fixed version can be changed in Settings.

The version selection dialog can also be enabled for every project launch.

## File associations

LoxTools supports:

- `.loxone`
- `.loxone.backup`

Use the file-association section in Settings to open Windows Default Apps and assign LoxTools to the desired file types.

When LoxTools is already running, project files opened from Windows are forwarded to the existing application instance.

## Update checks

LoxTools can check for available Loxone Config versions from these channels:

- Release
- Beta
- Alpha

The selected channel and current update status are shown in the application.

When an update is available, it can be downloaded and started from the tray menu.

The Loxone Config feed is requested over HTTPS only; failed requests never fall back to HTTP.
Packages are downloaded over HTTPS and checked
for size, CRC32, archive contents, and installer signature before execution. Feed CRC32
values may omit leading zeros.

Click the active download item to cancel it; click again after cancellation to retry.
The download label shows a rotating activity character, percentage, and cancellation hint.
Silent installation cannot be cancelled from the tray; its progress item is disabled.
LoxTools waits for the installer to exit before allowing retry
and keeps its working files available during rollback. It never forcibly kills the installer.
Middle-click the update item to open `%LOCALAPPDATA%\LoxTools\Updates`, which contains
download working folders and persistent `update.log` diagnostics. ZIP archives are deleted
after extraction and successful installer verification. Working folders and extracted installers
are deleted as soon as the installer exits, including cancellation or failure. Logs remain outside
these folders. Failed downloads and cancelled elevation requests are also cleaned up immediately.
Startup cleanup retries abandoned working folders older than six hours, protecting active
operations with file locks and checking recorded installer process identities after an app crash.
New Downloads shortcuts open the stable diagnostics folder so they survive this cleanup.

Settings → Paths → Installation folders offers two saved modes:

- **One installation per channel** (default): `LoxoneConfig_Release`, `LoxoneConfig_Beta`, and `LoxoneConfig_Alpha`.
- **Keep each version**: names such as `LoxoneConfig_17031027_Beta`, including version and channel.

New installs use the first configured installation parent path. If no path is configured,
they use the standard Loxone folder under Windows' Program Files (x86) directory.
After successful installation, the installation parent is added to the configured paths
if needed, and version discovery and file watchers are refreshed automatically.
The mode applies to subsequent installations; switching it does not move or delete existing
installations. The installer receives the destination and matching Start Menu group explicitly.
Target paths and modes are recorded in the local update log.

In single-folder mode, upgrades reuse the same installation directory for that channel.
Loxone's installer handles replacement of installed files and cleanup of stale ProgramData
content during an existing-installation upgrade (see [Loxone Config changelog, BG-I47902](https://www.loxone.com/enen/support/changelog-config/)).
LoxTools does not bulk-delete `%ProgramData%\Loxone` version folders: other installed
versions may still need them, and they can contain custom templates. Versioned-folder mode
deliberately retains separate installations and their required data. Existing installations
created before selecting single-folder mode are not automatically uninstalled.

Loxone Config installation starts automatically after verification, using the Inno Setup EXE
installer's `/VERYSILENT /SUPPRESSMSGBOXES /SP-` options. Windows requests administrator
approval; the normal installation has no wizard or progress window. Automatic Windows restarts,
forced closing of running applications, and automatic application restarts are disabled.
Cancelling administrator approval leaves installation cancelled and available for retry.
Installer logs are retained as `installer-<unique-id>.log` alongside `update.log`, outside the
temporary folders cleaned up after installation. Exceptional vendor dialogs (including cancellation
confirmation) are not all suppressible by Inno Setup. MSI installation remains unsupported. The existing
`Onboarding Test` launch profile previews the tray introduction; it does not reset settings
or reproduce a clean first installation.

## Tray controls

| Action | Result |
|---|---|
| Left double-click | Open Settings |
| Right-click | Open the tray menu |
| Middle-click | Close running Loxone Config instances when enabled |

## Automatic monitoring

LoxTools monitors the configured installation and project paths while running.

The tray menu updates automatically when installations or projects are added, removed, or renamed.

## Troubleshooting

### No Loxone Config installations are shown

Open Settings and verify that the installation path points to the parent directory containing the individual Loxone Config installation folders.

### No projects are shown

Open Settings and verify that the correct project directory is configured.

### A project opens with the wrong version

Check the selected launch mode in Settings.

To choose a version for one launch, hold `Shift` or right-click the project.
