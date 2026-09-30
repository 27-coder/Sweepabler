# Technical notes

Sweepable’r is a Windows x64 WPF app on .NET 10. The portable EXE includes its runtime and app catalog.

## Scanning

The app reads Windows uninstall entries, checks its catalog, and queries WinGet, Chocolatey, and Scoop. Up to four catalog checks run together. Successful package-manager maintenance is cached for 30 minutes.

Pirate Lover! exclusions, Discord exclusion, and temporary blocks for repeated failures are applied before results are shown. Related results are grouped using names, publishers, package IDs, and source information. A family header needs at least two rows; each app keeps its own version, source, and selection.

## Downloads and installation

Up to three direct installers download together, starting with the smallest known sizes. GitHub supplies asset sizes; other sources may need a five-second HEAD request. Unknown sizes go last.

Direct downloads of at least 100 MiB are limited to roughly 1 MiB/s while smaller or unknown-size transfers remain. The limit ends when those transfers finish or fail. WinGet, Chocolatey, and Scoop manage their own downloads, so this limit doesn’t apply to them.

Only one installer runs at a time. A ready installer can run while the others download. Cached bytes are reused only if they match the current feed checksum. Hashes and signing details are checked again before execution.

A hash mismatch blocks the update. Signature problems can produce a warning under the current warn-and-allow policy. Package-manager results are checked with a fresh inventory query. Direct installers use accepted exit codes, with registry checks for other codes.

Closing the app cancels its queue, removes partial downloads, and stops active installer or package-manager child processes. Download timeouts are 30 minutes, installer timeouts are 20 minutes, and the installer size limit is 2 GiB.

## First run

The language chooser uses English text and offers Turkish or English. Setup checks writable folders and requires WinGet, Git, Chocolatey, and Scoop. Missing tools are installed, checked, and refreshed before the app opens. A failed step offers retry; cancelled setup isn’t saved as complete.

WinGet comes from Microsoft’s WinGet client repair command. Git and Chocolatey use exact WinGet packages. Scoop uses the official ScoopInstaller script with `-RunAsAdmin`; the bounded download is held read-only during execution and removed afterward.

The main window is 430 × 340; setup is 390 × 220. Both keep the same size when language changes. Setup version 2 is stored with the selected language and maintenance timestamp.

## Self-update and removal

**Sweep the Sweepable’r** uses `27-coder/Sweepabler` by default. Old settings with a missing or empty `UpdateRepository` use that default too. Explicit `owner/repository` overrides are preserved.

The button checks published releases, including development pre-releases, and skips drafts. Each click refreshes GitHub metadata. If GitHub rate-limits the request, the existing fallback can use cached metadata up to seven days old.

A newer release needs a matching EXE asset, its GitHub SHA-256 digest, and app product metadata. ZIP-only releases are skipped. Accepted EXE names start with Süpürücü, Supurucu, Sweepabler, or Sweepable’r and may have a version suffix.

A hidden helper waits for the app to close, checks paths and hashes again, then replaces the current EXE and restarts it. It keeps a recovery copy during replacement. Immediate restart failures trigger recovery; helper errors go to `self-maintenance.log`. If the release isn’t newer, the button reports that the app is up to date.

**Delete Sweepable’r** confirms before removing only the current portable EXE. It keeps app data and installed tools. Both operations reject unrelated executable names and linked paths.

## Local data

Data stays in `%LOCALAPPDATA%\Supurucu`.

| File or folder | Contents |
| --- | --- |
| `settings.json` | Language, setup version, maintenance timestamp, update repository |
| `history.json` | Update results and trust warnings |
| `catalog.user.json` | Local catalog overrides |
| `unsupported-apps.json` | Apps missing from supported sources |
| `unsupported.ignore.json` | Pirate Lover! exclusions |
| `blocked-updates.json` | Repeated failures; blocks expire after seven days |
| `Downloads` | Installers and partial downloads |
| `Icons` / `GitHubCache` | Cached icons and release metadata |
| `assistant-scale.txt` | Broom assistant size |
| `crash.log` / `self-maintenance.log` | App and helper errors |

JSON writes use temporary files and atomic replacement. Data and download paths reject existing junctions and symbolic links. Cleanup stays inside Downloads and skips linked entries. Package IDs are checked before commands are built.

`SWEEPABLER_DATA_ROOT` changes the app-data folder for isolated checks. It does not isolate tool installations or updates to real apps.

## Catalog and checks

The catalog is embedded in the EXE. It falls back to a local `catalog.json` and accepts local overrides from `catalog.user.json`. Supported providers are GitHub releases, Electron feeds, official web pages, appcasts, WinGet, Chocolatey, and Scoop.

Catalog entries identify the app, allowed download hosts, installer type, and available checksum or publisher information. HTTPS and redirect rules are checked. Authenticode uses Windows trust, chain, and revocation checks.

The regression checks cover release selection, trust, storage, processes, downloads, setup orchestration, app families, and self-update channel discovery. They use simulated installers and tools. Real setup, replacement, restart, and UI behavior also need manual testing.
