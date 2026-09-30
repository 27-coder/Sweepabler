<p align="center"><img src="Assets/supurucu-polished.png" width="112" alt="Sweepable’r logo"></p>
<h1 align="center">Sweepable’r</h1>
<p align="center">An app updater for Windows. The first product in the Able’r family.</p>
<p align="center">English · <a href="README.tr.md">Türkçe</a> · <a href="https://github.com/27-coder/Sweepabler/releases/tag/v1.2.3-dev">Download</a></p>

Most of us started on Windows. Over the years, we built a setup around it: work, games, tools, and all the little things we’re used to. Switching to Linux or macOS sounds appealing, but taking that whole setup with us is a lot of work.

Sweepable’r is for those of us staying on Windows. It finds outdated apps and updates the ones you choose. Its Turkish name is **Süpürücü**.

## What it does

- Finds updates through WinGet, Chocolatey, Scoop, and a catalog of official download sources.
- Downloads up to three installers at once. Smaller downloads start first; large ones slow down while smaller jobs finish. Installation stays one app at a time.
- Groups related apps, such as Python, Anaconda, and Miniconda, while keeping their versions and selections separate.
- Keeps chosen apps out of updates through **Pirate Lover!**.
- Checks downloaded installers and keeps a local update history.
- Updates itself from this repository through **Sweep the Sweepable’r**.

The app is available in Turkish and English. There’s also a small broom assistant you can move, resize, or close.

## Running it

Download the [Windows x64 ZIP](https://github.com/27-coder/Sweepabler/releases/download/v1.2.3-dev/Sweepabler-1.2.3-dev-windows-x64.zip), extract it, and open `Süpürücü.exe`.

On the first run, choose **Turkish** or **English**. Setup installs any missing WinGet, Git, Chocolatey, and Scoop tools, checks them, then opens the app. You can change the language later from the footer’s **Language** menu.

Press **Detect geezer's** to scan, pick the apps you want, then press **Sweep!**. If an app is open, Sweepable’r asks before force-closing it. Save your work before agreeing. Discord is excluded because of its update and relaunch behavior.

**Sweep the Sweepable’r** checks this repo for a newer EXE, including development releases. It verifies the download, closes the app, replaces the current copy, and opens it again. **Delete Sweepable’r** asks for confirmation and removes that portable EXE; your saved data and installed tools stay.

The current release is **1.2.3-dev**. It’s still in development and the EXE is unsigned, so Windows may show a warning when you open it.

## What’s next

More supported apps, backups, cleanup previews, file shredding, and Windows options such as Copilot and Edge controls. These are plans; they aren’t part of the app yet. The [roadmap](ROADMAP.md) has the current list.

## Source and data

Sweepable’r uses WPF and .NET 10. Settings, history, and cached downloads stay under `%LOCALAPPDATA%\Supurucu`. There’s no account or background service. More detail is in the [technical notes](docs/TECHNICAL.md).

The source is [MIT licensed](LICENSE). Contributions are welcome. Changes are recorded in the [changelog](CHANGELOG.md).
