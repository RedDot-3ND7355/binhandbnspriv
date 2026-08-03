# LegacyBin

Blade & Soul `datafile.bin` / `localfile.bin` unpack / repack tool (32-bit and 64-bit clients).

## Features

- **Unpack** bin → per-table XML under `<file>.files/`
- **Repack** edited XML → bin (writes via temp file, then replaces the original)
- **32-bit** (`datafile.bin`, `localfile.bin`) and **64-bit** (`datafile64.bin`, `localfile64.bin`) layouts
- Bit-identical round-trip when content is unchanged (paddings and placeholders preserved)

## Binary format documentation

**Full structure reference for developers (32-bit vs 64-bit headers, tables, loose/archive, fields, paddings):**

### [docs/BIN_FORMAT.md](docs/BIN_FORMAT.md)

That doc covers file layout diagrams, field sizes, detection rules, XML mapping, and a repack safety checklist.

## 32-bit vs 64-bit (summary)

| Area | 32-bit | 64-bit |
|------|--------|--------|
| Header size fields | `int32` | `int64` |
| Table count | `int32` | `int64` |
| Name-table body | if `tableCount > 10` | same; entries 16-byte vs 12-byte in blob |
| Loose `FieldCount` | `int32` | `int32` + pad `int32` when `elementCount == 1` |
| List payload `Size` | `int32` | `int32` |
| Compressed tables | same | same |
| Field records | same | same |
| Trailing / region padding | preserve | preserve |

Mode is resolved by:

1. Filename containing `64.bin` / `datafile64` / `localfile64`, or  
2. Header layout detection if the file was renamed  

## Usage

1. Build `LegacyBin.csproj` (.NET Framework 4.8)
2. Run **LegacyBin** → main window is **Bin U/R Tool** (Unpack / Repack)
3. **Open Bin Editor** → full in-app table/field/string editor (or from the editor: **Tools → Legacy Unpack/Repack…**)

### Bin Editor

1. **File → Open** a `datafile.bin` / `datafile64.bin` / `localfile*.bin`
2. Select a table (left). Edit **fields** (top-right) and **lookup strings** (bottom-right)
3. **File → Save** / **Save As** writes the `.bin` (temp file then replace)
4. Optional: **File → Export XML…** for external tools

**View → Field data as ints** toggles int vs hex field payloads (same as the “Int?” checkbox on the U/R form).  
**View → Dark mode** toggles the editor/dialog theme (on by default; U/R tool already uses MaterialSkin dark).

### Editor notes

- Large tables use a **virtual** field/string grid (safe for tens of thousands of rows).
- **Loose** tables are fully editable. **Archive** tables: pick a **sub-archive** block (or all merged); save recompresses.
- **Add / Delete** field & string rows (single block for archives — not “All blocks”).
- **Add block / Delete block** on the sub-archive bar (new empty block starts with one Size=0 placeholder field).
- **Edit → Undo / Redo** (Ctrl+Z / Ctrl+Y), **Find in all tables** (Ctrl+Shift+F).
- **View → File header**, **Name table (hex)**, **Region tail / padding (hex)** — region tail is **editable** (advanced).
- Region tail and list trailing padding are preserved on save.
- Open → Save with no edits should remain bit-identical for known good samples.

### Localfile translate

**Tools → Localfile Translate…** (BnsDatTool-compatible):

1. Open `localfile.bin` / `localfile64.bin` in the editor  
2. **Export XML** → Target `Translation.xml` (aliases + original text from the text/commons table)  
3. **Merge XMLs by alias** — Source = other region’s Translation XML (e.g. English), Target = current client export (e.g. Thai structure). Matched aliases copy source replacements in.  
4. **Fill gaps (auto-translate)** — After merge, many rows are already `original ≠ replacement`. The tool:  
   - samples those rows and **detects original language + replacement language** (e.g. `th` → `en`)  
   - locks that pair for the run  
   - only translates **gaps** still at `original == replacement` (alias missing in source)  
   - dedupes by original text; resumes via `Translation.xml.gtcache.{sl}-{tl}`  
5. **Apply XML → open bin** (matches by **alias**, then by original text)  
6. **File → Save** the bin  

Lookup layout for text records: `words[0]=alias`, `words[1]=display text`. Oversized compressed blocks can be auto-split after apply.

Fill gaps uses the unofficial Google Translate free endpoint (no API key). Prefer merge-first so only missing aliases hit the API.

**Markup safety:** before each MT call, `<font …>` tags and HTML entities (`&quot;`, `&lt;`, `&amp;`, …) are replaced with placeholders and restored afterward so Google cannot turn entities into raw `"` / `<` and break in-game UI.

**Speed:** set **Workers** (default 6, max 16) for parallel requests. ~6× is usually a good balance; if you see many errors/429s, drop to 3–4 or let retries/backoff handle it and re-run from cache.
