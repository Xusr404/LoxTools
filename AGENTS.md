# Agent Instructions (AGENTS.md)

This project is a WPF (.NET) desktop application for managing and launching multiple
Loxone Config installations. The application provides a custom dark-green UI theme
(glassmorphism style) and focuses on clean UX, non-intrusive guidance, and maintainable code.

Agents working on this repository must strictly follow the rules below.

---

## PROJECT OVERVIEW

- Main application: `LoxTools/` (single .csproj, WPF/WinForms hybrid)
- Key windows:
  - `SettingsWindow.xaml` – application settings
  - `VersionSelectorWindow.xaml` – version selection & launch logic
- Themes and styling:
  - `LoxTools/Themes/Theme.xaml`
  - `LoxTools/Themes/AppThemes.xaml`
- Localization:
  - `LoxTools/Language/Lang.resx`
  - `LoxTools/Language/Lang.de-DE.resx`
  - Generated: `Lang.Designer.cs`
- Utilities live under `LoxTools/Utilities/`

---

## GENERAL RULES

- Preserve all existing functionality unless explicitly instructed otherwise
- Do NOT change business logic or ViewModel contracts without request
- Prefer minimal, incremental changes over large refactorings
- Avoid unnecessary complexity
- Assume the user is an experienced Windows/WPF user

---

## BEST PRACTICES

- Follow WPF and MVVM best practices
- Prefer readable, maintainable XAML over clever or compact markup
- Keep responsibilities clearly separated (View vs ViewModel)
- Use appropriate controls for intent (e.g. RadioButtons for mutually exclusive options)
- Avoid duplicated logic or duplicated UI state

---

## CLEAN CODE RULES

- Do not introduce unused bindings, converters, styles, or resources
- Remove obsolete UI elements when replacing functionality
- Avoid dead code or legacy UI remnants
- Maintain consistent naming, indentation, and structure
- Avoid magic values; use named resources instead
- Prefer clarity over brevity

---

## UI / UX GUIDELINES (VERY IMPORTANT)

- The UI uses a dark-green glassmorphism style:
  - Rounded corners
  - Soft gradients
  - Subtle separators
  - No harsh borders
- New UI elements must visually integrate into existing Cards/Containers
- Do NOT introduce visually detached or “floating” elements
- Avoid UI clutter and visual noise

### Warnings & Guidance
- Do NOT use modal dialogs (MessageBox, popups) unless explicitly requested
- Prefer:
  - Inline warning banners
  - Empty states
  - Helper text
- Warnings must be:
  - Visible
  - Non-blocking
  - Automatically disappearing when resolved

---

## LAYOUT & STRUCTURE RULES

- Related functionality must be grouped under a clear section title
- Sections should be visually separated using spacing or subtle separators
- Mutually exclusive logic must be represented with RadioButtons
- ToggleButtons are only for independent on/off behavior
- Avoid splitting related controls into separate visual containers

---

## VERSION SELECTION RULES

- `UseLatestVersion` is the single source of truth
- UI must clearly represent exactly one active mode:
  - Automatic (latest version)
  - Manual (fixed version)
- When automatic mode is active:
  - Manual selection actions must be disabled or visually de-emphasized
- When manual mode is active:
  - Version selection must be fully interactive
- Never duplicate state or logic across multiple controls

---

## PATH MANAGEMENT RULES

- At least one installation path is required for the app to function correctly
- If no paths are configured:
  - Show an inline warning banner inside the Paths section
  - Include a clear call-to-action (e.g. “Add path…”)
- Do NOT block deletion of the last path
- Do NOT show popups for missing paths
- Warnings must disappear automatically once a valid path exists

---

## THEMING & APP THEMES (CRITICAL)

- The application uses both `Theme.xaml` and `AppThemes.xaml`
- New controls must respect the active AppTheme
- Do NOT hardcode:
  - Colors
  - Fonts
  - Font sizes
  - Spacing values
- Theme-specific resources belong in `AppThemes.xaml`
- Use `DynamicResource` where theme switching is expected
- New controls must visually adapt to all existing themes

---

## LOCALIZATION (STRICT)

- ALL user-visible text must be localized
- NO hardcoded strings in XAML or code-behind
- Every new text requires:
  - German translation
  - English translation
- Add new keys to:
  - `Lang.resx`
  - `Lang.de-DE.resx`
- Use the existing localization binding mechanism consistently
- Prefer concise, technical wording

---

## BUILD & DEVELOPMENT NOTES

- Build with MSBuild (COM reference required):
  - `msbuild LoxTools.sln /p:Configuration=Release`
- `dotnet build` may fail due to COM interop
- Use a Visual Studio Developer Command Prompt

---

## TESTING GUIDELINES

- No automated tests are currently configured
- After UI changes, perform a manual smoke test:
  - Open Settings
  - Add/remove paths
  - Save settings
  - Open Version Selector
  - Launch with automatic and fixed versions
  - Verify tray icon behavior

---

## COMMIT & PULL REQUEST GUIDELINES

- Keep commit messages short and imperative
  - Example: `Add inline warning for missing install paths`
- Mention affected window(s) in commit body
- For UI changes, include screenshots in PRs
- Clearly describe behavioral changes

---

## QUALITY BAR

Before considering a change complete, ensure:
- UI looks integrated, not bolted-on
- No modal interruptions were introduced
- All texts are localized
- The UI accurately reflects the underlying state
- The result feels natural to an experienced Windows/WPF user
