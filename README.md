<h1>
  <img src="LionGrep/Assets/Square44x44Logo.scale-200.png" alt="LionGrep logo" height="40" align="left" />
  &nbsp;LionGrep
</h1>

**A UI-powered grep-like search tool for Windows. Built on .NET 10 and WinUI 3.**

**Current version:** 1.0

---

## Highlights

- **Fast.** Memory-mapped I/O, SIMD-vectorized byte search over UTF-8, per-file parallelism, and ripgrep-style required-literal extraction for regex queries. On real corpora, comparable to `rg`.
- **Any file size.** Files above 2 GiB stream through a chunked search path (newline-aligned 64 MiB windows); files above 4 MiB stream through replace via a sibling temp + atomic rename. No silent skipping for big files.
- **Three-section form: Where / What / Filter.** Each section snapshots into a **preset** (with hotkey support); presets compose so you can mix a saved root with a saved filter.
- **Live filter on results.** Narrow the result set without re-running the search; every export, copy, and replace respects the current filter.
- **Atomic, encoding-safe replace.** BOM-detected encoding round-trips through the rewrite; CRLF/LF line endings are preserved per-line; replace runs in parallel across cores; Cancel and Undo are first-class.
- **Right-click results menu** — Open in editor / Open with… / Open containing folder / Cut / Copy / Copy paths / Copy filenames / Copy lines / Copy as CSV / Delete (Recycle Bin) / Properties.
- **Excel + CSV export.** Real `.xlsx` via ClosedXML, RFC-4180 CSV.
- **Inverse search** and **search-in-currently-found-files** for two-pass refinement.

---

## Quick links

- **[DOCS.md](DOCS.md)** — full user manual (every feature, every quirk).
- **[DEVELOPMENT.md](DEVELOPMENT.md)** — architecture, testing strategy, performance levers, build instructions, project layout.

---

## Quick start (users)

1. Run `LionGrep.exe`.
2. Drop one or more directories into **Search in**.
3. Type a pattern in **Search for**.
4. Press **Ctrl+Enter**.
5. Double-click any result to open it in your editor (configure under the gear icon).

Press **Ctrl+Alt+Enter** to rewrite matches on disk.

---

## Quick start (developers)

```pwsh
# Build everything (Windows + .NET 10 SDK + WinAppSDK 2.0 required).
dotnet build LionGrep.slnx -c Debug -p:Platform=x64

# Run.
dotnet run --project LionGrep/LionGrep.csproj -c Debug -p:Platform=x64

# Fast unit tests (~200, NUnit, no UI).
dotnet test LionGrep.Core.Tests/LionGrep.Core.Tests.csproj

# Slow end-to-end UI smoke (FlaUI; build the app first).
dotnet test LionGrep.UI.Tests/LionGrep.UI.Tests.csproj -s LionGrep.UI.Tests/LionGrep.UI.Tests.runsettings
```

See [DEVELOPMENT.md](DEVELOPMENT.md) for everything else.

---

## Requirements

- Windows 10 1809 (build 17763) or newer.
- For development: .NET 10 SDK and the Windows App SDK workload.

---

## Developer & License

Developed by **Dominik Rauch** (<dominik.rauch@signpath.io>).

Licensed under the **GNU Affero General Public License v3.0** — see [LICENSE.md](LICENSE.md).
