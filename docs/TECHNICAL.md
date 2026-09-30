# Technical guide

## Update pipeline

1. Refresh installed update tools unless successful maintenance is less than 30 minutes old.
2. Read HKLM/HKCU uninstall entries and the app’s identity.
3. Check up to four independent catalog sources while package-manager inventory runs in parallel.
4. Apply Pirate Lover! exclusions, Discord exclusion, and version-specific failure blocks.
5. Start up to three selected direct downloads, ordered by known size. GitHub supplies asset size; other sources use an optional five-second HEAD probe. Unknown sizes go last. Reuse existing installer bytes only when the current feed checksum matches.
6. Limit each direct transfer of at least 100 MiB to roughly 1 MiB/s while a smaller/unknown-size transfer remains pending. Completion or failure releases the limit within the next pacing interval. Buffering and server behavior make this an application-level approximation.
7. Take the smallest ready direct installer, or run a selected package-manager update while downloads continue. Prompt before force-closing an open target app. Install only one app at a time.
8. Recheck hashes and signing identity immediately before execution. Record the result. Package-manager success requires a fresh inventory query; direct installers use successful exit codes, with registry verification for nonstandard codes.

Package managers own their transfer behavior and are not throttled by this app. Closing/cancelling drains the queue, removes partial files, and kills active installer/package-manager child trees before exit.

## First launch

The language chooser always uses English text, including its Turkish and English buttons and the footer's Language menu. Select a language in the fixed 390 × 220 setup window; subsequent preparation uses that selected language. Preparation verifies writable data folders, checks WinGet, Git, Chocolatey, and Scoop, installs missing required tools, refreshes their metadata, and opens the fixed 430 × 340 main window automatically. Preparation failure leaves an error and retry button; cancellation does not save completion.

Setup uses Microsoft's `Microsoft.WinGet.Client` repair command for missing WinGet, exact WinGet packages for Git and Chocolatey, and the official ScoopInstaller installation script with `-RunAsAdmin` for Scoop. Downloaded setup scripts are bounded in size, held read-only during execution, and removed afterward. Commands run hidden with deadlines and cancellation cleanup. Setup may change the PC through those tools' installers; it does not remove Windows components.

Existing settings with `SetupVersion < 2`, or a missing required tool, receive preparation. `SetupVersion`, `Language`, and `LastMaintenanceUtc` are saved atomically. The footer language menu changes language later in the same compact setup layout.

## App families

`AppFamilyClassifier` groups the displayed update results using app names, publishers, source descriptions, and catalog package/repository identities. It recognizes Python (including Anaconda and Miniconda), Git, Java, and Node.js, plus related versioned names. Apps using the same package manager do not become a family solely because they share WinGet, Chocolatey, or Scoop.

A family header appears only when at least two related results are present: for example, **Python's** in English and **Pythongiller** in Turkish. Individual app names, versions, sources, and selected states remain separate. Grouping is refreshed after the scan, after a successful update removes a row, and when language changes. This groups eligible update results; it does not enumerate every installed Python component in the main list.

## Self-update and removal

`settings.json` has an `UpdateRepository` field, defaulting to `27-coder/Sweepabler`. Missing, null, blank, or whitespace values in older settings automatically use that default. An explicit trusted `owner/repository` override is preserved.

**Sweep the Sweepable’r** checks this repository's newest eligible GitHub release, including development pre-releases and excluding drafts. Each click revalidates release metadata with GitHub instead of relying on the 30-minute fresh-cache shortcut; rate-limit fallback still follows the existing cache policy. Other app catalog entries retain their existing pre-release and caching policies.

A newer release must include a standalone EXE asset named `Süpürücü`, `Supurucu`, `Sweepabler`, or `Sweepable'r` (an optional version suffix is accepted), a GitHub SHA-256 asset digest, and matching app product metadata. ZIP-only releases cannot update the portable copy. Download, domain, size, and checksum checks apply before replacement. The button reports that the app is up to date when the eligible release is not newer.

A hidden PowerShell helper waits for this process to close, rechecks paths and hashes, copies the new bytes beside the current EXE, atomically replaces it with a recovery copy, and restarts it. Immediate restart failures trigger recovery; other helper failures are logged in `self-maintenance.log`. A real release update still needs clean-PC validation after configuring the channel.

**Delete Sweepable’r** asks for confirmation, waits for this process to close, and deletes only that exact portable EXE. It keeps app data, installed tools, and other files. Both actions reject unrelated host executable names and linked paths; they are intended for the published portable build.

## Local files

Normal data is under `%LOCALAPPDATA%\Supurucu`:

| Path | Purpose |
| --- | --- |
| `settings.json` | Language, preparation version, recent maintenance, release repository |
| `history.json` | Results and trust warnings |
| `catalog.user.json` | Catalog additions/overrides |
| `unsupported-apps.json` | Local catalog-gap report |
| `unsupported.ignore.json` | Pirate Lover! exclusions |
| `blocked-updates.json` | Machine/version failures; expire after seven days |
| `Downloads` | Completed installers and temporary partial files |
| `Icons` | Cached installed-app icons |
| `GitHubCache` | Validated release metadata |
| `assistant-scale.txt` | Assistant size |
| `crash.log` | Diagnostics |
| `self-maintenance.log` | Self-update/removal helper failures |

JSON state uses same-directory temporary files and atomic replacement. Data/cache writes and downloads reject existing reparse points in their paths. Cleanup stays inside `Downloads`, skipping linked entries. Reserved device filenames are sanitized, package identities are validated before command construction, and system/tool commands use resolved executable paths. `SWEEPABLER_DATA_ROOT` is a developer override for isolated checks.

## Catalog

The EXE embeds `catalog.json`, with a local fallback and a user override. Providers: `github`, `electron-yml`, `official-web`/`web-page`, `appcast`, `winget`, `choco`/`chocolatey`, and `scoop`.

Bundled apps: Notepad++, Git for Windows, OBS Studio, KeePassXC, Signal, ShareX, AutoHotkey, Audacity, GitHub Desktop, LocalSend, PowerShell, Rainmeter, Zen Browser, qBittorrent, and GitHub CLI. PowerToys uses WinGet to preserve scope.

Example user entry:

```json
{
  "id": "example-app",
  "name": "Example App",
  "provider": "official-web",
  "versionUrl": "https://example.com/download",
  "directVersionRegex": "Version (?<version>\\d+\\.\\d+\\.\\d+)",
  "downloadRegex": "href=\"(?<url>[^\"]+x64\\.exe)\"",
  "installerType": "exe",
  "silentArgs": "/S",
  "matchNames": ["Example App"],
  "officialDomains": ["example.com"],
  "signaturePublisherContains": "Example"
}
```

This illustrates the schema, not a verified app. `officialDomains` limits hosts; `requireHttps` defaults true; `signaturePublisherContains` pins an Authenticode signer substring. Web sources support `sha512Regex`, `checksumUrl`, and `checksumRegex`. GitHub digests supply SHA-256 when available. Checksums accept hex or base64.

Hash mismatch blocks execution. Missing/untrusted signatures or unexpected publishers produce visible warnings under the warn-and-allow policy. Windows WinVerifyTrust checks the chain and revocation. The size ceiling is 2 GiB. Redirects follow catalog rules, with a narrow GitHub release-CDN exception.

GitHub metadata is fresh for 30 minutes; a cache up to seven days old is a fallback only on live rate limiting. Download deadlines: 30 minutes. Installer deadlines: 20 minutes. Installer heartbeats: 30 seconds.

## Verification and limits

The package-free regression harness covers catalog selection, package results, trust, redirects, safe paths, JSON writes, process termination, cleanup, concurrency/order, pacing release, cancellation, changed/truncated installer bytes, required-tool orchestration with simulated tools, cache reuse, and maintenance-helper syntax. It does not install tools or execute the self-removal helper.

UI checks and clean-machine trials complement those tests. Local tests do not establish universal app support or complete system recovery. The development candidate is portable Windows x64 and unsigned, with the official self-update channel configured and no full installer.
