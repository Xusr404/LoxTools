# Changelog

All notable changes to LoxTools are documented in this file.

The project uses Semantic Versioning and supports `alpha`, `beta`, `rc`, and stable releases.

## [Unreleased]

## [1.1.0] - 2026-06-21

### Added

- Versioned GitHub release automation for installer and portable packages.
- GitHub-backed LoxTools update checks at startup and every 12 hours, with stable, beta, and alpha channels.
- Localized LoxTools update controls and progress states in Settings, the footer, notifications, and the tray menu.
- Optional Authenticode signing and signature verification in the release pipeline.
- Automated coverage for release parsing, semantic-version precedence, channel selection, checksums, signatures, and installer launching.

### Changed

- Limited installer resources to English and German and removed release PDB and empty application-config artifacts while retaining the self-contained runtime.
- Added English and German Inno Setup localization.
- Verified application updates now download on demand, install silently, close the running instance safely, and restart LoxTools after installation.

### Security

- Application updates require exact release asset names, bounded downloads, matching SHA-256 checksums, a valid Authenticode chain, and the configured publisher identity before execution.

## [1.0.0] - 2026-06-19

### Added

- Initial public release of LoxTools.
