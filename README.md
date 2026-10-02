<p align="center"><img src="src/Sweepabler/Assets/supurucu-polished.png" width="112" alt="Sweepable’r logo"></p>
<h1 align="center">Sweepable’r</h1>
<p align="center">Choose which Windows apps to update from one compact window.</p>
<p align="center">WinGet · Chocolatey · Scoop · Supported official installers</p>
<p align="center"><a href="https://github.com/27-coder/Sweepabler/releases/latest"><strong>↓ Download for Windows x64</strong></a></p>
<p align="center">English · <a href="README.tr.md">Türkçe</a> · Free and open source</p>

<p align="center"><img src="docs/media/update-en.png" width="430" alt="Sweepable’r Update view showing a selectable Git for Windows update"></p>

Sweepable’r finds outdated apps and updates the ones you choose. Its Turkish name is **Süpürücü**, and it is the first product in the Able’r family.

## Try it

1. Open the [latest Windows release](https://github.com/27-coder/Sweepabler/releases/latest), download the **Windows x64 ZIP**, and extract it.
2. Run `Süpürücü.exe`, choose **Turkish** or **English**, and let first-run setup finish.
3. Press **Detect geezer's**, select the apps you want to update, and press **Sweep!**.

First run requests administrator access and prepares any missing WinGet, Git, Chocolatey, and Scoop tools. The EXE is currently unsigned, so Windows may show a warning. See [Running it](#running-it) for details.

## A 20-second look

<p align="center"><img src="docs/media/interface-demo.gif" width="494" alt="Animated Sweepable’r interface demo: choose an update and cycle through Update, Delete, Patch, and Backup"></p>

The screenshot and demo use the actual app interface with installed-app versions checked read-only. The demo shows selection and wardrobe navigation; no updates were installed. **Delete, Patch, and Backup are development previews**, with their action buttons disabled.

## What it does

- Finds updates through WinGet, Chocolatey, Scoop, and a catalog of official download sources.
- Downloads up to three direct installers at once. Smaller downloads start first; large ones slow down while smaller jobs finish. Installation stays one app at a time. Package managers handle their own downloads.
- Groups related apps, such as Python, Anaconda, and Miniconda, while keeping their versions and selections separate.
- Keeps chosen apps out of updates through **Pirate Lover!**.
- Checks downloaded installers and keeps a local update history.
- Updates itself from this repository through **Sweep the Sweepable’r**.

The app is available in Turkish and English. There’s also a small broom assistant you can move, resize, or close.

Click the small red wardrobe in the top-right corner to cycle through **Update → Delete → Patch → Backup**. Update keeps the existing updater controls. Delete, Patch, and Backup currently show planned features with disabled action buttons.

## Running it

Download the **Windows x64 ZIP** from the [latest release](https://github.com/27-coder/Sweepabler/releases/latest), extract it, and open `Süpürücü.exe`.

On the first run, choose **Turkish** or **English**. Setup installs any missing WinGet, Git, Chocolatey, and Scoop tools, checks them, then opens the app. You can change the language later from the footer’s **Language** menu.

Press **Detect geezer's** to scan, pick the apps you want, then press **Sweep!**. If an app is open, Sweepable’r asks before force-closing it. Save your work before agreeing. Discord is excluded because of its update and relaunch behavior.

**Detect geezer's** also checks this repo for a Sweepable’r update, including development releases. Sweepable’r stays out of the normal app list; the **Sweep the Sweepable’r** text turns green when a verified newer release is available. Clicking that button verifies the download, closes the app, replaces the current copy, and opens it again. **Delete Sweepable’r** asks for confirmation and removes that portable EXE; your saved data and installed tools stay.

The current release is **1.2.5**. The app is still in development and the EXE is unsigned, so Windows may show a warning when you open it.

## What’s next

More supported apps, backups, cleanup previews, file shredding, and Windows options such as Copilot and Edge controls. These are plans; they aren’t part of the app yet.

## Why Sweepable’r?

Most of us started on Windows. Over the years, we built a setup around it: work, games, tools, and all the little things we’re used to. Switching to Linux or macOS sounds appealing, but taking that whole setup with us is a lot of work.

Sweepable’r is for those of us staying on Windows. It starts with keeping the apps in that setup up to date.

## Source and data

Sweepable’r uses WPF and .NET 10. The app source is in [src/Sweepabler](src/Sweepabler). Settings, history, and cached downloads stay under `%LOCALAPPDATA%\Supurucu`. There’s no account or background service.

The source is [MIT licensed](LICENSE). Contributions are welcome.

Developers: [27-coder](https://github.com/27-coder) and [umutkkgz](https://github.com/umutkkgz).
