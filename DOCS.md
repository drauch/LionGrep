# Locate — User Manual

Locate is a UI-driven grep-like search & replace tool for Windows, built on .NET 10 and WinUI 3. The goal is **predictable speed** at ripgrep-class throughput while staying out of your way.

This document is the canonical user manual. It describes every visible feature and every non-obvious behavior (especially "what counts as skipped"). It is updated in lock-step with the code.

---

## 1. Quick start

1. Launch the app. Focus starts in **Search for**.
2. Drop one or more directories into **Search in** (one per line) or click **Browse…**.
3. Type a pattern in **Search for**.
4. Press **Ctrl+Enter** (or click **Search**) to run.
5. Results appear in the table at the bottom. Double-click any line to open it in your configured editor (set in Settings).

Press **Ctrl+Alt+Enter** (or click **Replace**) to rewrite matches on disk. You'll get a confirmation dialog the first time; tick "Don't warn me again" to suppress it for future runs.

---

## 2. The three sections — Where, What, Filter

The form is divided by three labeled headers. They map directly to the three groups used by **presets** so a preset can apply just one or two of them.

### 2.1 Where

- **Search in** — one or more directory paths. Multi-line by default; the `↕` toggle expands the box for easier multi-directory editing. The `🕐` button shows recent values.
- **Browse…** — opens the system folder picker and appends the chosen folder to the list.

### 2.2 What

- **Search for** — the match pattern.
- **Replace with** — the substitution applied during Replace. Empty replacement deletes the match.
- **Toggles** (in this order):
  - **Use regex** — interpret patterns as regular expressions instead of literal text (see §3).
  - **Case sensitive** — when off, ASCII letters and most single-char Unicode case pairs fold (e.g. `К` matches `к`). Multi-char foldings like German `ß` ↔ `ss` do **not** fold (this is intentional; matches ripgrep semantics, never culture-aware).
  - **Whole word** — match must be bounded by non-word characters (ASCII alphanumeric + underscore).
  - **Preserve case** — when replacing, reshape the replacement string to match the original's case pattern (all-lower / all-upper / Title; Mixed is left as-is).
  - **Dot matches newline** — *(regex only)* makes `.` match `\n`. Currently line-oriented; multi-line regex matches across newlines is a v1.1 item.
  - **Search in file & sub directory names** — when on, the same **Search for** pattern is also matched against each file's relative path (with `/` separators). A file is yielded if **either** the content **or** the path matches. See §6.2 for caveats.
  - **Keep file date when replacing** — restores the original `LastWriteTime` after a replace.

The **Reset** button on the right of the **WHAT** header restores Search for / Replace with / all toggles to defaults.

### 2.3 Filter

- **File names** — pattern that *includes* files. Glob mode by default; multiple globs separated by `|`; prefix a token with `!` to exclude. Examples:
  - `*.cs` — only C# files.
  - `*.cs|*.ts|!*.Generated.cs` — C# and TS, excluding generated files.
  - `Regex` toggle: treat the entire field as one regex matched against the relative path.
- **Exclude paths** — pattern that *excludes* files **and** directories. Same `|`-separated glob (or whole-string regex). Examples:
  - `bin|obj|node_modules` — skip those directories anywhere in the tree.
  - `*.tmp` — skip files matching `*.tmp` regardless of directory.
  - `tests/fixtures` — skip that specific path under each search root.
- **Size** — `All sizes` (no filter), `Less than`, `Greater than`, `Between`. The KB box(es) appear when applicable. Default for new users: **Less than 256 KB** (matches grepWin).
- **Date** — `All dates` (no filter), `Newer than`, `Older than`, `Exactly on`, `Between`. Date pickers appear when applicable; format is ISO `yyyy-MM-dd`.
- **Traversal toggles** (full-width row): Include subfolders, Include system files, Include hidden items, Follow symbolic links, Skip binary files.
- **Reset** button on the **FILTER** header restores all filter values to defaults.

The form auto-stacks Size / Date below File names / Exclude paths when the window is narrow.

---

## 3. Pattern semantics

### Text mode (default)
Patterns are **literal substrings**. Ordinal comparison; case sensitivity controlled by the **Case sensitive** toggle. No glob/wildcard interpretation in this mode — `*.cs` searches for the literal substring `*.cs`.

### Regex mode
Patterns are .NET regular expressions, compiled with `RegexOptions.CultureInvariant`. When **Case sensitive** is off, `RegexOptions.IgnoreCase` is added. When **Dot matches newline** is on, `RegexOptions.Singleline` is added. **Whole word** wraps the pattern in `\b(?:…)\b`.

### Locale
Locate **never** uses culture-aware comparison. This is a design decision, deliberate, matching ripgrep's behaviour and avoiding surprises like Turkish dotless-i or German ß ↔ ss equivalence. It's also dramatically faster than culture-aware comparison.

---

## 4. Replace

Replace rewrites every matched file on disk. Atomic via temp-file + `File.Replace`, so a failure can't corrupt the original. The current limit is **4 MiB per file** (replace-side); larger files throw `NotSupportedException` and are skipped. (Streamed replace for larger files is a v1.1 item.)

You'll get a confirmation dialog the first time per session, with a "Don't warn me again" checkbox that persists in settings.

**Encoding round-trip:** Locate detects encoding via BOM (UTF-8/16 LE/16 BE/32 LE/32 BE). No BOM falls back to UTF-8. Replace writes back with the **same** encoding it detected, including the BOM if there was one.

**Line endings:** preserved per-line — CRLF stays CRLF, LF stays LF, even mixed within a file.

**Preserve case:** when on, the replacement is reshaped per-match:
- Original all-lower → replacement lowercased.
- Original all-upper → replacement uppercased.
- Original strict Title (first letter upper, rest lower) → replacement reshaped to Title.
- Anything else → replacement used as-is.

---

## 5. The "Skipped" count — what counts and what doesn't

The status line shows `X files, Y matches, Z skipped`. **Skipped** is the count of files that were considered but excluded from the search.

**Counted as Skipped:**
- Files filtered out by the **File names** include pattern.
- Files filtered out by **Exclude paths**.
- Files filtered out by the **Size** filter.
- Files filtered out by the **Date** filter.
- Files skipped by **Skip binary files** (any NUL byte in the first 8 KiB; UTF-16/32 BOM-detected files are *never* treated as binary).

**Not counted as Skipped (invisible to the count):**
- Files inside directories pruned by **Exclude paths** when matching as a directory pattern. The whole subtree is never traversed, and we don't pay to enumerate it just to count it. This is intentional, for performance.
- Files inside subfolders when **Include subfolders** is off — same reason.
- Files filtered by attribute: hidden (when **Include hidden items** is off), system (when **Include system files** is off), symlinks/reparse points (when **Follow symbolic links** is off). These never reach the file-level predicate and aren't counted.
- Files that errored (in use, > 2 GiB, access denied) — silently skipped.

**Rule of thumb:** "Skipped" answers the question *"how many files did we look at and decide to filter out?"* It does not include files we never saw because a higher-level predicate told us not to recurse.

---

## 6. Search caveats

### 6.1 "Search in file & sub directory names" uses the Search-for pattern

When you tick **Search in file & sub directory names**, the **same** pattern from **Search for** is matched against each file's relative path (with `/` separators). A file is yielded if either content or path matches.

**This is not a glob.** If you want all `.cs` files, the right tool is the **File names** filter (`*.cs`), not the search pattern. Typing `*.cs` in **Search for** with text mode searches for the literal three-character substring `*.cs`, which doesn't appear in `foo.cs`.

If you specifically want path matches by extension, use regex mode with something like `\.cs$`.

### 6.2 Empty pattern with names toggle

If **Search for** is empty *and* **Search in file & sub directory names** is on, no files are yielded — name matching reuses the same pattern, and an empty pattern matches nothing. To list all files matching a glob, run a search with a non-empty pattern (e.g. `.`) or future-feature: a "list files only" toggle.

### 6.3 File size limits

- **Search**: files up to 2 GiB. Larger files are silently skipped (counted in Skipped).
- **Replace**: files up to 4 MiB. Larger files throw `NotSupportedException` and are silently skipped.

Streamed search and replace for arbitrary file sizes is a v1.1 item.

---

## 7. Results table

### 7.1 Layout

Columns left-to-right: **Name, Size, Matches, Path, Ext, Encoding, Date modified**. The leading column is a chevron toggle to expand individual file rows and see the matched lines (with the matched substring bolded).

When **Search in file & sub directory names** is on and a file is in the results because of a **name** match, the matched portion of the file name in the **Name** column is highlighted with a yellow background. If a file appears for name reasons only (no content matches), the row's chevron is hidden — there's nothing to expand. Files with content matches show the chevron normally; if their name also matched, the highlight shows there too.

- **Drag** the thin gap between two columns to resize them. The east-west cursor confirms you've grabbed the handle.
- **Click** any column header to cycle sort: **None → Ascending → Descending → None**. The active column shows `▲` or `▼`. When the primary sort is **Path**, the secondary sort is always **Name** so siblings within a directory appear alphabetically.
- The first search of each session sorts results by **Path → Name** ascending. Subsequent searches respect whichever sort you've chosen (sticky across searches).

### 7.2 Responsive column hiding

When the window narrows, columns are hidden in this priority order (most aggressive first): **Date modified → Path → Size → Encoding → Ext → Matches**. **Name** is always visible. Resetting widths happens only when the breakpoint changes; user-driven resizes are preserved within a breakpoint.

### 7.3 Selection and copy

Multi-select via Ctrl/Shift-click as in any ListView. Right-click for the context menu:

- **Expand all** / **Collapse all**
- **Copy name(s)** — file names of the selected (or all) results.
- **Copy full path(s)** — full paths.
- **Copy line(s)** — the matched line text(s), one per line.
- **Copy as CSV** — `Name,Path,Line,Column,Text`, one row per match. RFC-4180 quoting (values containing commas, quotes, CR, or LF are wrapped in `"…"` with internal `"` doubled).

**Ctrl+C** is bound to **Copy as CSV** by default.

### 7.4 Edit on double-click

Double-click any line entry to open the configured external editor at that file/line/column. Configure the editor command in **Settings**; placeholders `%path%`, `%line%`, `%column%` are substituted in.

---

## 8. Splitter & form layout

The horizontal divider between form and results can be dragged vertically. On launch, the divider is positioned exactly at the form's natural content height — no wasted blank space.

When you start a search or replace, the form row collapses (the divider rides up to the top). Drag it back down to see the form again.

---

## 9. Presets

A preset captures a subset of the form's three groups (**Where**, **What**, **Filter**) and can be re-applied via the Presets dropdown or a hotkey.

- **Save current as preset…** — snapshots the current form. The three "apply group" checkboxes are pre-ticked for whichever groups you've changed from defaults. You can edit those flags in Settings.
- **Edit…** — opens the Settings window's Presets section, where you can rename, delete, change which groups apply, or assign a hotkey.

Activating a preset fills only the *checked* groups; unchecked groups are left untouched. So presets compose: apply a "Where: my source tree" preset followed by a "Filter: my exclusions" preset.

**Hotkeys**: assign per-preset in Settings, e.g. `Ctrl+1`, `Ctrl+Shift+F`, `Alt+F2`. Active in-app whenever Locate has focus. Global system-wide hotkeys are a v1.1 item.

The form's last successful state is also auto-restored on next launch (a "last form" implicit preset persisted in the registry under `HKCU\Software\Locate\Settings\LastForm`).

---

## 10. Settings

Opened from the gear icon in the title bar.

- **External editor** — command line for opening a file at a given line/column. Browse picks an `.exe` and pre-fills a sensible template like `"C:\…\code.exe" "%path%":%line%:%column%`. **Quote `%path%`** if your paths might contain spaces.
- **Don't warn when replacing** — suppresses the Replace confirmation dialog. Also set automatically when you tick "Don't warn me again" in the dialog itself.
- **Presets** — list view on the left, details panel on the right. Add / Remove buttons. Each preset has a name, optional hotkey, and three apply-group checkboxes. Save persists everything; Close also saves.

All settings persist under `HKCU\Software\Locate`.

---

## 11. Hotkeys reference

| Hotkey | Action |
|---|---|
| **Ctrl+Enter** | Run search |
| **Ctrl+Alt+Enter** | Run replace (with confirmation unless suppressed) |
| **Ctrl+C** (in results) | Copy selected results as CSV |
| **Enter** | Default action for the focused control (newline in multi-line Search-in, etc.) |
| **F2** etc. | Whatever you've assigned to a preset |

---

## 12. Architecture notes (for the curious)

- `Locate.Core` — the search/replace engine. Pure .NET, no UI deps. Memory-mapped I/O, SIMD-vectorized byte search via `IndexOf`, RFC-style BOM detection, ordinal/`OrdinalIgnoreCase` semantics. Tested with 100+ NUnit tests against real temp files (no mocks).
- `Locate.App` — WinUI 3 shell, MVVM via `CommunityToolkit.Mvvm` source generators. Custom title bar, custom CheckBox template for compact density, `ColumnResizer` UserControl wrapping a `Thumb` (Thumb is sealed in WinUI 3, so we can't subclass it directly).
- Persistence: registry under `HKCU\Software\Locate\…` (recents, settings, presets, last-form snapshot).

---

## 13. Known gaps (v1.1 candidates)

- Search > 2 GiB — needs a streamed, chunked-with-overlap path.
- Replace > 4 MiB — needs streamed temp-file rewrite.
- Multi-line regex (Dot-matches-newline currently line-oriented).
- "Inverse search" / "Search in currently found files" SplitButton items are stubs.
- Parallel `BatchReplacer` orchestrator (current implementation is sequential).
- Global system-wide hotkeys for presets (in-app only today).
- Live-update of preset name in the Settings list (Preset doesn't implement `INotifyPropertyChanged` yet).
- Live re-fit of the form/results splitter when WrapPanel content reflows on width change (form-fit only runs on first activation).
