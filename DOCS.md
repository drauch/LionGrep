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

### Search variants (Search button dropdown)

Click the chevron on the **Search** split button:

- **Inverse search** — runs the same enumeration + filter pipeline, but yields files that *don't* match the pattern. Files that get **Skip-binary**'d are excluded from inverse results too — they're "unknown", not "no match". Inverse rows have no per-line matches and aren't expandable.
- **Search in currently found files** — re-runs using **only the What inputs** (Search for, Replace with, regex/case toggles). The current result set is the file list; the **Where** and **Filter** groups are ignored entirely. Disabled until you have results.

---

## 4. Replace

Replace rewrites every matched file on disk. Atomic via temp-file + `File.Replace`, so a failure can't corrupt the original. The current limit is **4 MiB per file** (replace-side); larger files throw `NotSupportedException` and are skipped. (Streamed replace for larger files is a v1.1 item.)

### 4.1 Confirmation dialog (3-way)

Clicking the **Replace…** button always opens a confirmation dialog with three options:

- **Replace with backups** *(primary)* — copies each modified file to `<filename>.bak` next to the original before overwriting. The full set of `.bak` files becomes the input for **Undo** (next section). Each fresh "Replace with backups" run resets the undo set; older `.bak` files remain on disk as orphans (not auto-deleted, for safety) but are no longer reachable via Undo.
- **Replace** *(secondary)* — overwrites in place. **Cannot be undone.**
- **Cancel** *(default)* — Esc / Enter on the dialog cancels.

**Bypass for power users:** `Ctrl+Alt+Enter` always replaces immediately, no dialog, **no backup**. Use this when you've already audited the result list and want to commit the rewrite without further prompts. To make the **Replace…** button behave the same way (immediate, no backup, no dialog), tick **Don't warn when replacing** in Settings.

### 4.2 Undo

The **Undo** button (next to **Replace…**) restores files from the most recent **Replace with backups** run. It iterates the tracked `<path, .bak>` pairs, copies each `.bak` over its original, then deletes the `.bak`. If **Keep file date when replacing** is on, the restored file's mtime is set back to the `.bak`'s mtime (which is the original pre-replace mtime); otherwise, the OS sets it to "now".

Undo is disabled until a successful **Replace with backups** has produced at least one backup. Files where the `.bak` is missing at undo time are reported as failed and skipped.

### 4.3 Other behavior

**Encoding round-trip:** Locate detects encoding via BOM (UTF-8/16 LE/16 BE/32 LE/32 BE). No BOM falls back to UTF-8. Replace writes back with the **same** encoding it detected, including the BOM if there was one.

**Line endings:** preserved per-line — CRLF stays CRLF, LF stays LF, even mixed within a file.

**Preserve case:** when on, the replacement is reshaped per-match:
- Original all-lower → replacement lowercased.
- Original all-upper → replacement uppercased.
- Original strict Title (first letter upper, rest lower) → replacement reshaped to Title.
- Anything else → replacement used as-is.

---

## 5. The status line — files searched / matches / skipped

The status line at the right end of the **SEARCH RESULTS** row reads:
`Y matches in N files (S files searched, K skipped)`.

- **N files** — files that produced a match.
- **Y matches** — total individual hits (a file contributing 12 matched lines counts 12).
- **S files searched** — files that passed all enumeration filters and were actually opened by the searcher (≥ N).
- **K skipped** — files considered but filtered out (see breakdown below).

The window title separately shows the first non-empty line of **Search in** so you always know which root the open Locate window is searching against, even when many windows are open.

**Skipped** is the count of files that were considered but excluded from the search.

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

**Why grepWin reports more skipped files for the same query.** grepWin walks **into** every directory and rejects each file individually, so files inside `bin`, `obj`, `.git`, `packages`, etc. inflate its skipped count. Locate's `ShouldRecursePredicate` prunes those subtrees from enumeration entirely — the files are never seen by the OS, never counted, and never paid for. Same physical results, fewer syscalls, smaller skipped number.

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

**Path column** — when **Search in** has exactly one root, paths are shown relative to that root (so you see `src/Locate` rather than `C:\Projects\2026Projects\Locate\src\Locate`). When you have multiple search roots, the column shows the full directory path so the result is unambiguous.

**Encoding column** — shows `—` (em-dash) when the file is in the results because of a name match only and was never opened. When the file's content was scanned, this column shows `UTF-8`, `UTF-8 BOM`, `UTF-16 LE`, `UTF-16 BE`, `UTF-32 LE`, or `UTF-32 BE` per BOM detection (no BOM falls back to `UTF-8`).

- **Drag** the thin gap between two columns to resize them. The east-west cursor confirms you've grabbed the handle.
- **Click** any column header to cycle sort: **None → Ascending → Descending → None**. The active column shows `▲` or `▼`. When the primary sort is **Path**, the secondary sort is always **Name** so siblings within a directory appear alphabetically.
- The first search of each session sorts results by **Path → Name** ascending. Subsequent searches respect whichever sort you've chosen (sticky across searches).

### 7.2 Responsive column hiding

When the window narrows, columns are hidden in this priority order (most aggressive first): **Date modified → Path → Size → Encoding → Ext → Matches**. **Name** is always visible. Resetting widths happens only when the breakpoint changes; user-driven resizes are preserved within a breakpoint.

### 7.3 Selection and copy

Multi-select via Ctrl/Shift-click as in any ListView. Right-click on a row that isn't part of the current selection narrows the selection to just that row before opening the context menu (so you don't accidentally act on the entire list).

- **Open with editor** — runs the configured editor command for each selected file. If no editor is configured (or the launch fails), an error dialog appears. Multi-select (more than one file) always prompts a confirmation first.
- **Open with…** — invokes the standard Windows "Open With" picker (`rundll32 shell32.dll,OpenAs_RunDLL`). Multi-select prompts a confirmation since each file gets its own dialog.
- **Open containing folder** — opens Explorer with each selected file pre-selected (`/select,...`). Multi-select always prompts a confirmation first.
- **Cut** / **Copy** — places the selected file(s) on the system clipboard as `StorageItems` with the appropriate move/copy preferred-drop-effect. Pasting in Explorer (or any shell-aware target) moves or copies them just like a native cut/copy.
- **Copy path(s) to clipboard**
- **Copy filename(s) to clipboard**
- **Copy line(s) to clipboard** — the matched line text(s).
- **Copy as CSV** — `Name,Path,Line,Column,Text`, one row per match. RFC-4180 quoting (values containing commas, quotes, CR, or LF are wrapped in `"…"` with internal `"` doubled).
- **Delete** — moves the selected file(s) to the **Recycle Bin** via `StorageFile.DeleteAsync`. Always confirms first; deleted rows are removed from the live results list.
- **Properties** — opens the standard Windows file properties dialog (`SHObjectProperties`). With multi-select, only the first file's properties are shown.

Ctrl+C is **not** bound by default — use the right-click menu (or Export buttons in the SEARCH RESULTS header) to copy/export.

### 7.4 SEARCH RESULTS section header

Above the results table, a section header shows action buttons:

- **Expand all** / **Collapse all** — toggles every result row's matched-line view.
- **Export to CSV** — opens a Save As dialog and writes all results as a UTF-8 (BOM-prefixed) CSV file.
- **Open in Excel** — writes a real `.xlsx` workbook (via ClosedXML) to `%TEMP%` and shells it open with the default `.xlsx` handler. The header row is bold with a light-grey background; columns auto-fit to the first 200 rows.

### 7.5 Open files (double-click)

- **Single row** — double-click opens that file in the configured editor at the first match's line/column.
- **Multiple rows selected** — double-click prompts a confirmation dialog (`Open N files?`) before launching N editor instances. Cancel to abort.
- **Line entry inside an expanded row** — double-click opens at that exact line.

Configure the editor command in **Settings**; placeholders `%path%`, `%line%`, `%column%` are substituted.

---

## 8. Splitter & form layout

The horizontal divider between form and results can be dragged vertically. On launch, the divider is positioned at the form's natural content height — no wasted blank space.

When you start a search or replace, the form row collapses (the divider rides up to the top). Drag it back down to see the form again.

While the form is collapsed, the **window title** mirrors the running search status — e.g. `Locate — 42 files, 318 matches, 9,213 skipped (running)` — so you always see progress without having to expand the form. The taskbar/Alt+Tab title shows the same string.

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
- **Don't warn when replacing** — when ticked, the **Replace…** button skips the 3-way confirmation dialog and replaces immediately (no `.bak`), exactly like `Ctrl+Alt+Enter`. The dialog otherwise always shows by default.
- **Remember recently used values between sessions** — when ticked (default), the recents dropdown for each input persists in the registry. Untick to disable: existing history is wiped on Save and no new entries are recorded.
- **Presets** — list view on the left, details panel on the right. Add / Remove buttons. Each preset has a name, optional hotkey, and three apply-group checkboxes. Save persists everything; Close also saves.
- **Reset everything…** (footer, left side) — wipes the entire `HKCU\Software\Locate` registry hive: settings, presets, recents, and the last-form snapshot. Confirmation dialog protects against accidents; the action is irreversible.

All settings persist under `HKCU\Software\Locate`.

---

## 11. Hotkeys reference

| Hotkey | Action |
|---|---|
| **Ctrl+Enter** | Run search (works even when focus is in a multi-line input) |
| **Ctrl+Alt+Enter** | Replace **immediately** — bypasses the 3-way confirmation dialog. No backup is created. |
| **Escape** | Cancel the running search **and** restore the form panel. Pressing Escape repeatedly is idempotent — the form returns to the same cached natural height each time. |
| **Enter** | Default action for the focused control (newline in multi-line Search-in, etc.) |
| **F2** etc. | Whatever you've assigned to a preset |
| **Double-click on input field** | Show recents dropdown for that field, sized to the input's width |
| **Double-click on result row** | Open file in editor (multi-select prompts a confirmation first) |
| **Right-click on result row** | If nothing is selected, selects the right-clicked row before opening the menu; existing multi-select is preserved |
| **Open with editor / Open containing folder** | Always confirms when more than one file is selected (no Explorer-spam from a stray multi-select) |

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
