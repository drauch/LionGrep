# Locate.UI.Tests

End-to-end smoke tests that drive the **real** Locate WinUI 3 window via FlaUI / UI Automation.

These are **slow on purpose** — every test launches assertions against the same live app process — and are intended to be run **once before each release** as a "did I break anything visible?" gate. Don't wire them into your inner loop; the fast `Locate.Core.Tests` suite is for that.

## Prerequisites

- An **interactive desktop session**. UI Automation needs a real desktop to point at; headless CI servers won't work without a session-keeping host (e.g. self-hosted runners on a logged-in dev box).
- Build the WinUI app first — the test runner finds the most-recently-built `Locate.exe` by walking up from its own bin folder:
  ```pwsh
  dotnet build Locate/Locate.csproj -c Debug -p:Platform=x64
  ```
  (Release also works; the fixture tries both.)

## Running

```pwsh
# Sequential — UI tests cannot run in parallel against one window.
dotnet test Locate.UI.Tests --settings Locate.UI.Tests/Locate.UI.Tests.runsettings
```

The `runsettings` file pins `MaxCpuCount=1` and `NumberOfTestWorkers=1` for you.

To run a single fixture while iterating:

```pwsh
dotnet test Locate.UI.Tests --filter "FullyQualifiedName~SearchTests"
dotnet test Locate.UI.Tests --filter "FullyQualifiedName~ReplaceTests"
```

## What's covered

Every user-visible feature has at least one test. The suite is organized as:

| File | Verifies |
|---|---|
| `Tests/SearchTests.cs` | Literal case-sensitive / case-insensitive / non-ASCII byte path / regex with prefilter / whole-word / file-name glob / exclude-paths / empty-search-root validation / single-match file |
| `Tests/FilterPanelTests.cs` | Search-in-results toggle open/close / debounced filter narrowing / Esc closes panel / "Also match file path" checkbox |
| `Tests/ReplaceTests.cs` | Ctrl+Alt+Enter immediate replace (no .bak) / Replace…→Cancel leaves files untouched / Replace with backups + Undo round-trip |
| `Tests/SearchSplitTests.cs` | Inverse search via SplitButton flyout / Search in currently found files |
| `Tests/SortTests.cs` | Header click cycles None → Asc (▲) → Desc (▼) → None |
| `Tests/ResultsToolbarTests.cs` | Expand all / Collapse all preserve file row count / Show query button restores form |
| `Tests/HotkeyTests.cs` | Ctrl+Enter from multi-line Search-in box / Layered Escape (filter first, then form) |
| `Tests/SettingsWindowTests.cs` | Settings window opens with all expected checkboxes / About dialog opens & dismisses |
| `Tests/WindowChromeTests.cs` | Window title reflects first line of Search-in / Status line format |

## Test data

`CorpusBuilder` builds a deterministic temp directory at `%TEMP%\locate-uitests-ro` once per run. It contains a small set of files with hand-picked tokens so each test can pin down expected match counts. Replace tests get their own throwaway directory via `CorpusBuilder.CreateIsolated(...)` and clean up in `[TearDown]`.

## State and side-effects

The suite is **isolated from your real settings**. At fixture start it picks a fresh subkey under
`HKCU\Software\LocateUITests\<guid>` and launches the app with
`--alternate-registry-key <that-subkey>`. The app routes every read and write of settings, presets,
recents, and the last-form snapshot through that path instead of `HKCU\Software\Locate`. At fixture
teardown the sandbox subkey (and the `LocateUITests` parent if it ends up empty) is wiped.

Net effect on your machine after a run:
- **Your `HKCU\Software\Locate` is untouched.** Recents stay yours, last-form stays yours, presets stay yours.
- **No file outside** the test temp directories is modified — Replace tests use `CorpusBuilder.CreateIsolated`.
- One `ResetEverything_ClearsRegistrySandbox` test even verifies that **Settings → Reset everything…** wipes the entire app subtree, end-to-end. Safe to run because it operates on the sandbox.

The `--alternate-registry-key` flag is also useful outside the test suite: launch Locate with
`Locate.exe --alternate-registry-key Software\LocateScratch` to play with a throwaway settings
profile without touching your real one.

## Adding a new test

1. If there's a fitting fixture, add a `[Test]` method to it and use the existing `AppDriver` helpers.
2. For a new feature area, add a new class under `Tests/`, give it `[TestFixture]` and `[NonParallelizable]`, and instantiate `AppDriver` in `[SetUp]`.
3. Reset form state with `_driver.ResetForm()` at the top of each test — every test must be self-contained.

## Limitations & caveats

- **Doesn't cover** things that require a live shell extension or external app: **Open with…** dialog, **Properties** dialog, the file-shell **Cut/Copy** clipboard handover, **Open in Excel** (would launch Excel). Those are smoke-tested by hand.
- **WinUI 3 UI Automation gotchas**: a few controls (e.g. items inside virtualised ListView templates) only become reachable once they've scrolled into view. Tests stick to the top of the result list to stay deterministic.
- **Cancellation**: each test allows up to 30s for a search to finish (`AppDriver.WaitForSearchToFinish`). Tweak the timeout if you point the suite at a much larger corpus.
