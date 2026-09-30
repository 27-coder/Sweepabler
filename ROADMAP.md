# Growing the Able’r tree

Sweepable’r begins with app updates. The direction is broader Windows housekeeping, delivered in small changes people can inspect and use. Ideas here have no promised release date.

## Next increments

- Validate 1.2.2 on clean Windows 10/11 x64 machines, including required-tool installation, setup retry, both app languages in the fixed window sizes, and related-app grouping.
- Expand the catalog from real unsupported-app reports, checking official sources, scope, signing identity, and silent flags.
- Improve visibility of download size, active transfers, and restart requirements in the compact UI.
- Add a data-folder view and a manual preview for clearing Sweepable’r’s installer cache.
- Configure the GitHub release repository and validate portable self-update and removal. Design a proper installer and signed release workflow.

## Later branches

**Backup and recovery.** Start with explicitly selected files and app settings. Show where the backup goes, required space, and how to restore it. Whole-system imaging needs its own design and validation.

**Cleanup previews.** Inventory first. Show exact targets and estimated space before broader cleanup. Keep user data and automatic cache maintenance separate.

**Secure deletion.** Explore deliberate file shredding with explicit selection and final review. Explain SSD, snapshots, and backup limitations before calling anything unrecoverable.

**Windows component controls.** Research supported options for Copilot, Edge, and other components by Windows version. Show dependencies and a recovery path; do not silently remove system components.

## How a branch becomes a feature

A proposal should include the user problem, files or settings affected, reversal or recovery story, and evidence needed to call it ready. Keep status visible in the README and changelog. Ship useful increments rather than implying the roadmap is already implemented.
