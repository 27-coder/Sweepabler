# Contributing to Sweepable’r

Small, focused contributions are welcome: reproducible issues, translation corrections, tested catalog entries, or one behavior change.

## Local workflow

Use Windows with .NET 10 SDK:

```powershell
dotnet build .\ProperAppUpdater.csproj -c Release
dotnet run --project .\Tests\ProperAppUpdater.Tests.csproj -c Release
```

For UI checks, use isolated data:

```powershell
$env:SWEEPABLER_DATA_ROOT = Join-Path $PWD 'work\ui-data'
dotnet run --project .\ProperAppUpdater.csproj
Remove-Item Env:\SWEEPABLER_DATA_ROOT
```

The override is for development. Use a dedicated app-data directory; automatic cleanup treats its `Downloads` child as Sweepable’r’s cache.

Verify Türkçe and English, light and dark modes where relevant, keyboard controls, first-run cancellation, and closing during work. Avoid updating unrelated real apps to test a UI change.

## Adding an app

Try `catalog.user.json` in isolated app data first. Include the publisher’s official feed/release URL, exact x64 asset rule, tested silent arguments, installer type and scope, allowed domains/redirects, available checksums, and a verified signer identity. Supply registry match names with examples showing no collision.

Do not guess installer flags or copy a signer pin from an unrelated package. Prefer the package-manager path when a direct installer cannot preserve scope. See [the technical guide](docs/TECHNICAL.md).

## Code and translations

Keep installations sequential, prompts attached to the affected app, and automatic cleanup inside the app cache. Do not replace exact updates with global update-all commands.

Keep Turkish/English keys and formatting placeholders aligned. Use readable names and comments explaining unusual choices. Preserve third-party license notices.

Add regression coverage for changed download, trust, process, or storage behavior. Explain what changed, how it was checked, and any limits. Update the changelog and roadmap when status changes.

## Reports

Include Windows version, app version/language, update source, affected app/version, and reproduction steps. Remove private paths, names, tokens, and unrelated history before sharing logs.

For security-sensitive problems, use a private maintainer contact listed on their GitHub profile. Do not attach executable payloads or private machine data to a public issue.
