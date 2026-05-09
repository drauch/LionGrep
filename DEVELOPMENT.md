# Locate — Developer Guide

This is the **onboarding doc for engineers** taking over the codebase. If you've just been handed the repo, read this top-to-bottom — it's written so you don't need any prior conversation context to be productive on day one.

For end-user behavior, see [DOCS.md](DOCS.md). For a 60-second project pitch, see [README.md](README.md).

---

## 1. The 30-second mental model

Locate is a **WinUI 3 desktop app** (`Locate/`) sitting on top of a **pure-.NET search/replace engine** (`Locate.Core/`). The engine is the interesting part — it targets **ripgrep-class throughput** through memory-mapped I/O, SIMD byte search over UTF-8, and per-file parallelism. The UI is deliberately thin: code-behind handlers parse routed-event args and immediately delegate to testable helpers in `Locate.Core.Logic/`.

The engine has **no UI dependencies** and the view-logic helpers have **no WinUI types in their signatures**. That separation is what makes the codebase fast to test and easy to evolve.

---

## 2. Project layout

```
Locate.slnx                 — solution file (.slnx, the new XML format)
├── Locate/                 — WinUI 3 shell. MVVM via CommunityToolkit.Mvvm source generators.
│   ├── MainWindow.xaml(.cs)        — the search/replace form + results table
│   ├── SettingsWindow.xaml(.cs)    — gear-icon modal: editor, presets, recents, reset
│   ├── AboutDialog.xaml(.cs)
│   ├── ViewModels/                  — MainViewModel (large), SettingsViewModel, FileMatchViewModel
│   ├── Models/Preset.cs             — INotifyPropertyChanged DTO for a saved preset
│   ├── Services/                    — RegistryStore, SettingsStore, PresetsStore, RecentsStore, EditorLauncher
│   ├── Controls/CursorThumb.cs      — thin Thumb wrapper (Thumb is sealed in WinUI 3)
│   ├── Native/                      — P/Invoke shims (shell properties, etc.)
│   └── Locate.csproj                — net10.0-windows10.0.19041.0, x86/x64/ARM64
│
├── Locate.Core/            — search/replace engine (no UI deps)
│   ├── Searcher.cs                  — top-level coordinator: enumerate → parallel match → stream results
│   ├── FileEnumerator.cs            — Where/Filter pruning (size/date/attribute/exclude-paths)
│   ├── FileSearcher.cs              — single-file search; the byte-level fast path lives here
│   ├── FileReplacer.cs              — atomic temp+rename replace; encoding round-trip
│   ├── LiteralMatcher / RegexMatcher / IMatcher        — pattern abstractions
│   ├── LiteralLineReplacer / RegexLineReplacer / ILineReplacer
│   ├── RegexLiteralExtractor.cs     — pulls the longest required literal out of a regex (ripgrep-style)
│   ├── EncodingDetection.cs         — BOM-sniff with UTF-8 fallback
│   ├── CasePreserver.cs             — Replace's "Preserve case" reshaping
│   ├── FileFilters.cs               — glob/regex parsing for File names + Exclude paths
│   ├── SearchOptions / FileEnumerationOptions / ReplacementContext / MatchTypes / SyncProgress
│   └── Logic/                       — view-logic helpers used by the WinUI shell
│       ├── HotkeyParser.cs              — "Ctrl+Shift+F2" ⇄ VirtualKey + modifier mask
│       ├── ResponsiveLayout.cs          — column-width breakpoints + hide-priority order
│       ├── SortDirectionLogic.cs        — None → Asc → Desc cycle + arrow rendering
│       ├── CsvBuilder.cs                — RFC-4180 quoting; used by Export-to-CSV and copy-as-CSV
│       └── PathPrefixDedup.cs           — prunes redundant search roots (C:\Foo plus C:\Foo\Bar → C:\Foo)
│
├── Locate.Core.Tests/      — NUnit unit tests for engine + logic (~200 tests)
│   ├── *Tests.cs                    — engine: FileSearcher, FileReplacer, FileEnumerator, …
│   ├── RegexLiteralExtractorTests.cs (large; pins the literal-extractor edge cases)
│   └── Logic/                       — pure-function helpers
│
├── Locate.UI.Tests/        — FlaUI smoke suite (NUnit, sequential, real WinUI process)
│   ├── AppFixture.cs                — process lifecycle + sandbox registry key
│   ├── AppDriver.cs                 — UIA element finder, type/click/check helpers
│   ├── CorpusBuilder.cs             — deterministic synthetic corpus for tests
│   ├── Tests/*.cs                   — per-feature smoke tests
│   └── README.md                    — coverage matrix + prerequisites
│
└── Locate.Bench/           — BenchmarkDotNet suite + corpus generator
    ├── Benchmarks/
    ├── Datasets/                    — code/log/repo profiles
    ├── Program.cs                   — CLI: `prepare <profile>`, `--filter`
    └── README.md
```

There are **no other top-level folders**. There is no separate "shared" project, no plugins folder, no generated code that lives in source. What you see is what runs.

---

## 3. How to build

The repo targets **.NET 10 SDK** and **Windows App SDK 2.0**. Building requires Windows; running requires Windows 10 1809 or newer.

Prerequisites:
- Visual Studio 2026 (or `dotnet` SDK 10) with the **Windows App SDK** workload.
- The "MSIX Packaging Tools" extension (only needed if you want to package; not required for `dotnet build`).

From the repo root:

```pwsh
# Restore + build everything (Debug, x64).
dotnet build Locate.slnx -c Debug -p:Platform=x64

# Run the app.
dotnet run --project Locate/Locate.csproj -c Debug -p:Platform=x64

# Release build, x64.
dotnet build Locate/Locate.csproj -c Release -p:Platform=x64

# Publish a self-contained x64 app (R2R + trimmed).
dotnet publish Locate/Locate.csproj -c Release -p:Platform=x64
```

Notes:
- `Locate.csproj` declares `<Platforms>x86;x64;ARM64</Platforms>` — pick one explicitly with `-p:Platform=x64`. Builds without an explicit platform will fail to find the WinAppSDK runtime targets.
- `AllowUnsafeBlocks` is on (mmap pointer handling).
- The MSIX project capability is enabled but you don't need to package to run from the IDE.

---

## 4. Running the tests

There are three tiers, in order of speed:

```pwsh
# Fast: pure unit tests (engine + view-logic helpers). Runs in seconds. No UI thread.
dotnet test Locate.Core.Tests/Locate.Core.Tests.csproj -c Debug

# Slow: end-to-end UI smoke. Drives a real WinUI window via FlaUI. Sequential.
# Build the app first (the suite launches the built exe).
dotnet build Locate/Locate.csproj -c Debug -p:Platform=x64
dotnet test Locate.UI.Tests/Locate.UI.Tests.csproj -c Debug -s Locate.UI.Tests/Locate.UI.Tests.runsettings

# Benchmarks (release only, takes minutes).
dotnet run --project Locate.Bench -c Release -- --filter '*LiteralCaseSensitive*'
```

### Test framework: NUnit, not xUnit

All test projects are NUnit. **Stay on NUnit when adding tests.** No mocks; tests use real temp files and a synthetic corpus.

### UI tests are slow on purpose

`Locate.UI.Tests` is the **pre-release smoke gate**, not the inner loop. It runs sequentially (`MaxCpuCount=1` in `Locate.UI.Tests.runsettings`) against a single live process. Use it before tagging a release. Use `Locate.Core.Tests` for everyday work.

The UI suite isolates itself from your real settings via `--alternate-registry-key Software\LocateUITests\<guid>`; the sandbox subkey is wiped at fixture teardown.

### Running a sandboxed app instance manually

Useful when poking at preset/settings UI without touching your real profile:

```pwsh
Locate.exe --alternate-registry-key Software\LocateScratch
```

All persisted state (settings, presets, recents, last-form snapshot) redirects to that subkey. Delete the subkey to reset.

---

## 5. Architecture notes

- **`Locate.Core`** — the search/replace engine **plus** view-logic helpers (`Locate.Core.Logic`). Pure .NET, no UI deps. Memory-mapped I/O, SIMD-vectorized byte search via `IndexOf`, RFC-style BOM detection, ordinal / `OrdinalIgnoreCase` semantics. Engine + logic are tested with ~200 NUnit tests against real temp files (no mocks).
- **`Locate.Core.Logic`** — code that used to live in the WinUI code-behind: `HotkeyParser`, `ResponsiveLayout`, `SortDirectionLogic`, `CsvBuilder`, `PathPrefixDedup`. Each is a pure function with WinUI-free inputs and outputs, and each is unit-tested. The window code-behind translates between these helpers and the WinUI types.
- **`Locate` (the app)** — WinUI 3 shell, MVVM via `CommunityToolkit.Mvvm` source generators. Custom title bar, custom CheckBox template for compact density, `CursorThumb` UserControl wrapping a `Thumb` (Thumb is sealed in WinUI 3, so we can't subclass it directly).
- **Persistence** — registry under `HKCU\Software\Locate\…` (recents, settings, presets, last-form snapshot). Redirectable per-process via `--alternate-registry-key`.

### MVVM conventions

- ViewModels use `[ObservableProperty]` and `[RelayCommand]` from `CommunityToolkit.Mvvm`.
- Code-behind handlers parse routed-event args and **immediately** delegate to a testable helper. Anything richer than a one-liner has been extracted to `Locate.Core.Logic`.
- The XAML files (`MainWindow.xaml`, `SettingsWindow.xaml`, `AboutDialog.xaml`) are intentionally **not** unit-tested — they're declarative bindings to ObservableProperties and event handler names.

---

## 6. Performance levers in the engine

This is the part the engine cares about most. If you're touching `Locate.Core/Searcher.cs` or `FileSearcher.cs`, read this first.

- **Per-file parallelism.** `Searcher.Search` dispatches files across `Environment.ProcessorCount` worker threads via `Parallel.ForEach`, with results streamed back through a bounded `BlockingCollection` so a slow consumer can't OOM the producer. On 8-core boxes this translates to ~Nx wall-clock speedup on warm caches; cancelling either side propagates to the other via a linked CTS, and consuming early (e.g. `.Take(10)`) cleanly stops the producer.
- **Byte-level fast path for literal patterns over UTF-8.** UTF-8 is self-synchronizing — a multi-byte character's bytes never appear at any other character's position — so byte-level `IndexOf` is correct for **any** case-sensitive pattern, not just ASCII. `FileSearcher` skips per-line decoding entirely and uses SIMD-vectorized `IndexOf`/`IndexOfAny` over the raw bytes. Only matched lines are decoded for display. Case-insensitive matches still hit the path when the pattern is ASCII (via pre-folded bytes + `IndexOfAny(lower, upper)` skip-scan); whole-word boundaries are checked at byte level since word characters are themselves ASCII (any byte ≥ 128 is part of a non-ASCII char and therefore a non-word boundary).
- **Regex literal pre-filter.** `RegexLiteralExtractor` walks the regex pattern statically and finds the longest contiguous required-literal substring (e.g. `class\s+(\w+)` ⇒ `class`). Each file is first byte-scanned for that literal; if it's not present, the regex engine never runs on that file. Conservative on ambiguity (returns no literal rather than risk missing a match). This is the single most impactful regex optimization in real-world workloads — modeled after ripgrep's required-literal extraction.
- **Small-file shortcut.** Files smaller than 64 KB skip mmap and use `File.ReadAllBytes` instead. Mmap setup cost (Section object + view mapping + `AcquirePointer`/`ReleasePointer`) dominates at small sizes; on NTFS + SSD a buffered read is strictly faster.
- **Single decode pass per matched line.** UTF-8 emits at most one char per byte, so the decode buffer is sized from the byte length directly — no separate `GetCharCount` pass.
- **Whole-file regex on demand.** `DotMatchesNewline` triggers a one-shot decode + whole-text regex run; line-by-line matching is preserved for the common (single-line) case where it's cheaper.
- **Line-text reuse for dense matches.** When N hits land on the same line (e.g. `the` in English prose), the byte-fast-path decodes the line text once and shares the string across all `LineMatch` records on that line.
- **`Ascii.IsValid` for the all-ASCII checks.** SIMD-vectorized in the BCL since .NET 8; we don't roll our own loop.

### Semantic invariants (these are load-bearing)

- **Ordinal everywhere.** `StringComparison.Ordinal` / `OrdinalIgnoreCase` only. **Never** `CurrentCulture` or `InvariantCulture`. Regex options always include `RegexOptions.CultureInvariant`. This is intentional — matches ripgrep, avoids Turkish-i and German-ß surprises, and is dramatically faster.
- **Encoding round-trip.** Detect via BOM (UTF-8/16 LE/16 BE/32 LE/32 BE); no BOM ⇒ UTF-8. Replace writes back with the *same* encoding it detected, BOM included if present.
- **Line endings.** Preserved per-line — CRLF stays CRLF, LF stays LF, even mixed within a file.
- **Search size cap = 2 GiB.** Replace size cap = 4 MiB. Streamed paths for both are open (see §8).

---

## 7. Benchmarking

`Locate.Bench/` is a separate console project with a BenchmarkDotNet suite plus a deterministic synthetic-corpus generator. The generator caches by `(profile, seed)` so repeated runs hit the exact same files on disk — which is also what makes ripgrep-comparable measurements possible:

```pwsh
# Build (or reuse) the corpus, get its absolute path on stdout
$corpus = (dotnet run --project Locate.Bench -c Release -- prepare code).Trim()

# Time ripgrep on it
Measure-Command { rg --no-stats -c blazingNeedle $corpus | Out-Null }

# Run our benchmark suite — the LiteralCaseSensitive case targets the same workload
dotnet run --project Locate.Bench -c Release -- --filter '*LiteralCaseSensitive*'
```

See `Locate.Bench/README.md` for the available profiles, the patterns each benchmark exercises, and tips for fair comparisons (matching OS page-cache state, etc.).

---

## 8. Known gaps / v1.1 candidates

- **Search > 2 GiB** — needs a streamed, chunked-with-overlap path (overlap = pattern length − 1 to avoid splitting a match across chunks).
- **Replace > 4 MiB** — needs streamed temp-file rewrite (current impl is in-memory).
- **Global system-wide hotkeys** for presets (in-app only today).
- **Live re-fit of the form/results splitter** when `WrapPanel` content reflows on width change (form-fit only runs on first activation).

---

## 9. Gotchas worth knowing

- **WinUI 3 has no `Grid.SharedSizeGroup`.** Don't use it — the XAML compiler exits 1 silently. Use identical hardcoded widths instead.
- **Many WinUI 3 controls are sealed** (`Thumb`, etc.) so subclassing is out; wrap in a UserControl (see `Controls/CursorThumb.cs`).
- **`TextBlock` has no `Background`.** Wrap in a `Border` if you need one.
- **`Color` vs `Colors`** are in different namespaces (`Windows.UI` vs `Microsoft.UI`). Easy to mis-import.
- **Don't culture-compare strings.** See semantic invariants above.
- **Keep [DOCS.md](DOCS.md) in lock-step with feature changes.** Every behavior change should land with a docs update in the same commit.

---

## 10. Where to look first when…

| You're trying to… | Start here |
|---|---|
| Add a new toggle to the form | `MainWindow.xaml` + `MainViewModel` (`[ObservableProperty]`), then thread it through `SearchOptions` |
| Change how a pattern is matched | `IMatcher` + `MatcherFactory`; literal vs regex split |
| Change replace behavior | `FileReplacer` (atomic file rewrite) and `ILineReplacer` (per-line substitution) |
| Tweak the byte-level fast path | `FileSearcher` |
| Touch the regex pre-filter | `RegexLiteralExtractor` (read the test file first — there are subtle invariants) |
| Add a column to the results table | `MainWindow.xaml` table + `FileMatchViewModel` + `ResponsiveLayout` (hide priority) |
| Persist a new setting | `RegistryStore` / `SettingsStore` |
| Add a smoke test | `Locate.UI.Tests/Tests/*.cs` — copy the closest existing fixture |
