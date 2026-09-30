<p align="center"><img src="Assets/supurucu-polished.png" width="112" alt="Sweepable’r broom logo"></p>
<h1 align="center">Sweepable’r</h1>
<p align="center"><strong>Less Windows housekeeping. More getting on with your day.</strong></p>
<p align="center">The first branch of the <strong>Able’r</strong> tree · Native Windows app · MIT licensed</p>
<p align="center">English · <a href="README.tr.md">Türkçe</a></p>
<p align="center"><a href="https://github.com/27-coder/Sweepabler/releases/tag/v1.2.2-dev">Download Windows x64 · Development pre-release</a> · <a href="https://github.com/27-coder/Sweepabler/actions/workflows/build.yml">Build checks</a></p>

## Your environment took years to build. Keep it.

You like the way Linux and macOS handle administration. You have probably thought about switching. But your work, games, tools, shortcuts, and carefully assembled environment still live on Windows. Moving all of that is a project of its own.

Sweepable’r starts from a simpler question: **what if keeping your Windows PC in shape took less effort?**

Known as **Süpürücü** in Turkish, it brings app updates into one small, native desktop window. Find what is out of date, choose what deserves an update, and let the broom get to work. Official sources, a clear selection, and a record of what happened. No account to create. No web dashboard to babysit.

This is the first product in the Able’r family: practical tools for making the environment you already use easier to live with.

## One sweep, several update sources

Windows apps do not all update the same way. Sweepable’r combines a curated catalog with **WinGet, Chocolatey, and Scoop**. First-run setup installs missing tools and Git, which Scoop uses, then checks that all four work.

- **Prepare a new PC.** Choose Turkish or English in the English-language chooser. Setup prepares local data folders, installs required tools, refreshes their metadata, and opens the app automatically. A failed step stays visible with a retry button.
- **Find updates faster.** Independent catalog checks run together, alongside the package-manager inventory. Successful tool maintenance is reused for 30 minutes.
- **Finish the little jobs sooner.** Up to three direct installers download together. The smallest known downloads start first; ready installers can run while others download.
- **Give small downloads room.** Direct downloads of 100 MiB or more are limited to roughly 1 MiB/s each while smaller or unknown-size downloads remain. That limit lifts automatically when those downloads finish or fail.
- **Keep control.** Select your apps, use **Pirate Lover!** to keep selected apps out of updates, and decide whether an open app may be closed. Saying yes force-closes that app and its child processes, so save your work first.
- **Keep app families together.** Related update results share a family header such as **Python's** for Python, Anaconda, and Miniconda. Each app keeps its own name, version, source, and selection; a header appears when two or more related results are present.
- **Reuse a verified download.** An installer already in the cache is reused only when it matches the current publisher-provided checksum. Changed bytes are downloaded again.
- **See what happened.** Update history, verification results, and trust warnings stay on your PC. Repeated failures are paused for that version pair and can be reset.
- **Keep the desktop tidy.** New shortcuts belonging to a successfully updated app are removed. Existing shortcuts stay in place.

Installers run one at a time. WinGet, Chocolatey, and Scoop handle their own downloads; the direct-download limit does not control them. Unknown sizes cannot be ranked precisely. Discord is excluded because of its update/relaunch behavior.

There is also a small broom assistant. It works, complains, and occasionally loses a fight with an installer. Move it, resize it, or close it while the update job continues.

## Get started

Download the [Windows x64 ZIP](https://github.com/27-coder/Sweepabler/releases/download/v1.2.2-dev/Sweepabler-1.2.2-dev-windows-x64.zip), extract it, and open `Süpürücü.exe`, or build from source below. The portable build includes its .NET runtime and catalog, with no companion JSON file needed.

1. Open `Süpürücü.exe` and accept the Windows administrator prompt.
2. Choose **Turkish** or **English**. Setup completes and opens the app automatically.
3. Select **Detect geezer's**, review the results, and press **Sweep!** for the selected apps.

The English window is **Sweepable’r**; the Turkish window is **Süpürücü**, ready with **Ready to Sweep!** / **Süpürmeye hazır!**. Right-click the footer and choose **Language** to change language later. The chooser always uses English text; preparation uses your chosen language. The main window stays 430 × 340 and setup stays 390 × 220, including language selection and preparation. WinGet, Git, Chocolatey, and Scoop are required.

Beside **Pirate Lover!**, **Sweep the Sweepable’r** checks the configured release source, verifies the new EXE, replaces the current portable copy after it closes, and restarts it. **Delete Sweepable’r** asks for confirmation and removes only the running portable EXE after closing; saved settings and installed tools remain. The release repository is currently empty, so self-update reports that no channel is configured. See [release configuration](docs/TECHNICAL.md#self-update-and-removal).

The current build is **1.2.2-dev**, a [development pre-release](https://github.com/27-coder/Sweepabler/releases/tag/v1.2.2-dev). The portable EXE is unsigned, so Windows may show SmartScreen and UAC prompts. Broader clean-PC testing and the next increments remain on the roadmap.

## Where the broom goes next

App updates are the first branch. The wider goal is a Windows maintenance routine that helps with more of the jobs people put off.

| Direction | What we want to build | Status |
| --- | --- | --- |
| Backup and recovery | Back up selected data and settings, with an inspectable restore path | Planned |
| Cleanup previews | Show exact files and space involved before a cleanup | Planned |
| Secure deletion | Explicitly selected file shredding, with storage-specific limits explained | Exploring |
| Windows controls | Supported, reversible options for components such as Copilot and Edge | Exploring |
| Broader app coverage | More publisher-verified catalog entries and better package matching | Ongoing |
| Distribution | A proper installer, signed builds, and a published release channel | Planned; portable self-update code is ready for configuration |

These are development directions, not features hiding behind a button today. Sweepable’r currently updates selected supported apps and cleans its own old downloads. It does not claim to repair every Windows problem. Follow the [roadmap](ROADMAP.md), help shape a branch, or contribute a verified app entry.

## Built to stay understandable

This is a WPF app on .NET 10. The source is ordinary: installed-app inventory, release providers, download preparation, one installer at a time, and local JSON state. No service, scheduled task, telemetry pipeline, or required cloud account.

Downloads use HTTPS and catalog domain rules, with redirects checked again. Feed checksums are verified when available; a mismatch blocks execution. Authenticode and publisher pins are checked separately. Missing or invalid signatures produce a visible warning under the current warn-and-allow policy. A queued installer is checked again immediately before execution.

Data lives in `%LOCALAPPDATA%\Supurucu`. Automatic cleanup stays inside its `Downloads` directory and skips junctions and symbolic links. See [the technical guide](docs/TECHNICAL.md) for catalog fields, local files, and update policy.

## Build and contribute

Requires Windows and the **.NET 10 SDK**:

```powershell
dotnet build .\ProperAppUpdater.csproj -c Release
dotnet run --project .\Tests\ProperAppUpdater.Tests.csproj -c Release
dotnet run --project .\ProperAppUpdater.csproj
```

Publish a self-contained portable build:

```powershell
dotnet publish .\ProperAppUpdater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
```

The EXE is written to `bin\Release\net10.0-windows\win-x64\publish`. Windows CI builds and runs the regression harness; it does not install app updates on the runner.

Read [CONTRIBUTING.md](CONTRIBUTING.md) before adding an app or changing update behavior. Small fixes, translations, reproducible reports, and verified catalog additions are welcome. [CHANGELOG.md](CHANGELOG.md) records concrete changes as the product grows.

First-party source is available under the [MIT license](LICENSE). Third-party apps and installers retain their own licenses.
