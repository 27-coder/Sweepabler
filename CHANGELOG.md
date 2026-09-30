# Changelog

## Unreleased

- Shortened the English and Turkish project descriptions.
- Removed the contribution guide and issue/pull request templates. Contributions are still welcome.

## 1.2.3-dev — 2026-09-30 · Development pre-release

- Connected Sweep the Sweepable’r to `27-coder/Sweepabler`, including development pre-releases and checksum-verified EXE assets.
- Migrated missing or empty saved release channels to the official repository while preserving explicit overrides.
- Refreshed GitHub metadata on each self-update click so newly published releases are visible immediately; other catalog caching behavior stays the same.

## 1.2.2-dev — 2026-09-30 · Development pre-release

- Made the language chooser and its menu English-only, with Turkish and English buttons, while preserving the fixed window sizes.
- Confirmed the existing app-family classifier and results-list wiring remain intact; added coverage for five Python-related apps across different sources and for header refresh after removal and language changes.
- Documented family grouping and retained each app's own version, provider, and selection.

## 1.2.1-dev — Local development candidate

- Added fixed-size first-run preparation that installs required WinGet, Git, Chocolatey, and Scoop tools, verifies them, refreshes sources, and opens the app automatically. Failed preparation offers retry.
- Added in-app language switching and Sweepable’r English branding.
- Renamed the English controls to Pirate Lover!, Detect geezer's, and Ready to Sweep!.
- Added adjacent self-update and portable-EXE removal controls. The GitHub release repository remains unconfigured.
- Improved Turkish/English wording and localized window-control accessibility names.
- Parallelized catalog checks and independent tool maintenance; reused successful maintenance for 30 minutes.
- Added three-slot direct downloads, known-size ordering, ready-installer scheduling, and temporary limits for large downloads.
- Rechecked prepared installers before execution and rejected truncated content.
- Reused cached installers only when the current feed checksum matches; changed downloads are fetched again.
- Hardened data/download paths against junctions and symbolic links, sanitized reserved Windows filenames, validated package IDs, and resolved tool commands explicitly.
- Waited for cancellation and child-process cleanup before main-window shutdown.
- Corrected the WinGet inventory command and Chocolatey 2 local-list command; ran independent inventory queries concurrently.
- Added MIT licensing, bilingual introductions, contribution guidance, a roadmap, and Windows CI.

## 1.1.0 — 2026-08-24

- Hardened official installer selection, cache fallback, signatures, publisher pins, and redirects.
- Verified package-manager outcomes with fresh queries before recording success.
- Improved version matching, ignore-list replacement, atomic state writes, and junction-safe cleanup.
