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
