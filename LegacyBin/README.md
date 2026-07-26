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
2. **Export XML** → `Translation.xml` / `Translation64.xml` (alias + original text from the text/commons table)  
3. Optionally **Merge XMLs by alias** (source language → target structure)  
4. **Apply XML → open bin** (matches by **alias**, then by original text)  
5. **File → Save** the bin  

Lookup layout for text records: `words[0]=alias`, `words[1]=display text`. Oversized compressed blocks can be auto-split after apply.
