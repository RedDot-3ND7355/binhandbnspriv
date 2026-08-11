# Changelog

## v2.3

### Localfile translate — client-safe markup
- **Normalize entities on apply** — bare `"`, `'`, `&`, `<`, `>` outside real tags are re-escaped to `&quot;` / `&apos;` / `&amp;` / `&lt;` / `&gt;` before writing lookup text (Google free MT often drops them).
- **Entity-tag protect** — whole `&lt;arg …/&gt;`-style tokens are treated as atomic placeholders so MT cannot mangle icon/arg references inside them.
- **Restore missing tags** — if MT eats a placeholder (common for leading `<image …/>`), tags are re-inserted from the original protect list.
- **Protect → translate → unprotect** paths always run **NormalizeEntities** on the way out (markup and plain strings).
- Goal: keep translated `localfile(64).bin` **readable by the client** (no raw quotes / broken arg markup that can crash or break UI).

---

## v2.2

### Localfile translate — fill gaps
- After **Merge by alias**, **Fill gaps (auto-translate)** only translates rows still at `original == replacement` (missing aliases).
- Detects language pair from already-merged rows (e.g. `th` → `en`) and locks it for the run.
- Dedupes by original text; resumes via `.gtcache.{sl}-{tl}`.
- Parallel **Workers** (default 6) + stepped progress bar; cancel keeps cache.

---

## v2.1

- Bug fixed for `datafile(64).bin`!
- Sorry about that! &lt;/3

---

## v2.0

- Full 32/64-bit support for read/write
- Full unpack/repack
- Translation support for localfile
- Full-fledged in-app BIN editor
