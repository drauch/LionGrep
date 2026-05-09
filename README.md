# Locate

**A fast, predictable grep & replace tool for Windows.**

Locate is a UI-driven search & replace tool built on .NET 10 + WinUI 3, targeting **ripgrep-class throughput** without leaving the desktop. It's the tool to reach for when grepWin is too slow and `rg` is too much hassle.

**Current version:** 1.0

---

## Highlights

- **Fast.** Memory-mapped I/O, SIMD-vectorized byte search over UTF-8, per-file parallelism, and ripgrep-style required-literal extraction for regex queries. On real corpora, comparable to `rg` — and much faster than grepWin.
- **Any file size.** Files above 2 GiB stream through a chunked search path (newline-aligned 64 MiB windows); files above 4 MiB stream through replace via a sibling temp + atomic rename. No silent skipping for big files.
- **Three-section form: Where / What / Filter.** Each section snapshots into a **preset** (with hotkey support); presets compose so you can mix a saved root with a saved filter.
- **Live filter on results.** Narrow the result set without re-running the search; every export, copy, and replace respects the current filter.
- **Atomic, encoding-safe replace.** BOM-detected encoding round-trips through the rewrite; CRLF/LF line endings are preserved per-line; replace runs in parallel across cores; Cancel and Undo are first-class.
- **Right-click results menu** — Open in editor / Open with… / Open containing folder / Cut / Copy / Copy paths / Copy filenames / Copy lines / Copy as CSV / Delete (Recycle Bin) / Properties.
- **Excel + CSV export.** Real `.xlsx` via ClosedXML, RFC-4180 CSV.
- **Inverse search** and **search-in-currently-found-files** for two-pass refinement.
- **Sandboxable.** `--alternate-registry-key` redirects all persisted state for testing or demos.

---

## Quick links

- **[DOCS.md](DOCS.md)** — full user manual (every feature, every quirk).
- **[DEVELOPMENT.md](DEVELOPMENT.md)** — architecture, testing strategy, performance levers, build instructions, project layout.

---

## Quick start (users)

1. Run `Locate.exe`.
2. Drop one or more directories into **Search in**.
3. Type a pattern in **Search for**.
4. Press **Ctrl+Enter**.
5. Double-click any result to open it in your editor (configure under the gear icon).

Press **Ctrl+Alt+Enter** to rewrite matches on disk. The first time, you'll get a three-way confirmation dialog: **Replace with backups** (creates `.bak` files, undoable), **Replace** (in place, no undo), or **Cancel**.

---

## Quick start (developers)

```pwsh
# Build everything (Windows + .NET 10 SDK + WinAppSDK 2.0 required).
dotnet build Locate.slnx -c Debug -p:Platform=x64

# Run.
dotnet run --project Locate/Locate.csproj -c Debug -p:Platform=x64

# Fast unit tests (~200, NUnit, no UI).
dotnet test Locate.Core.Tests/Locate.Core.Tests.csproj

# Slow end-to-end UI smoke (FlaUI; build the app first).
dotnet test Locate.UI.Tests/Locate.UI.Tests.csproj -s Locate.UI.Tests/Locate.UI.Tests.runsettings
```

See [DEVELOPMENT.md](DEVELOPMENT.md) for everything else.

---

## Requirements

- Windows 10 1809 (build 17763) or newer.
- For development: .NET 10 SDK and the Windows App SDK workload.
