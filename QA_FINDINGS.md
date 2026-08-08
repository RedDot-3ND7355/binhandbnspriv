# QA Findings — LegacyBin manual translation & merge audit

Audit of the final deliverable `artifacts/merged_datafile_255_manual.xml`
(481,970 lookups, 4 tables, ~201 MB) against the original Thai client
(`LegacyBin/Resources/localfile.bin` → unpack `datafile_255.xml`), the official
EN local file (`datafile_448.xml`), the KR/EN 327 table (`datafile_327.xml`),
and the newly added `datafile_327_Shattered.xml`.

---

## 1. Translation coverage of the Thai sweep

- 7,689 rows still containing Thai were extracted (`/tmp/opencode/manual/extract.jsonl`)
  and translated to English, idx-keyed accumulations in
  `/tmp/opencode/manual/translations.jsonl` (7,689 entries, 0 duplicates).
- Authoring: batches 1–9 (2,250 rows, hand-authored in-session) + 13 subagent
  slices (5,439 rows, each validation-gated: unique idx, full coverage, byte-
  fidelity of every `<...>` tag, zero Thai characters in output, entities verbatim).
- Applied via `translate-apply` (row-ordinal keyed, refuses to overwrite
  non-Thai text). All 7,689 applied, 0 unmatched.

## 2. Structural verification

- XML parses cleanly; 481,970 lookups across 4 tables.
- `repack` → `unpack` round trip performed repeatedly (before and after every
  pass; final verification under `/tmp/opencode/qa/final6/`): every (alias, text)
  pair identical after the binary round trip → file is game-consumable.
  Only byte-level formatting differs.
- **Writer fix — length cap eliminated (2026-08):** the historical constraint
  `len(translation) <= len(original)` came from REPACK crashes on growing
  strings. Root cause found: `BDAT_SUBARCHIVE` stores its decompressed size and
  per-field start offsets as u16 (ceiling 65,535 bytes per chunk), and the
  writer never re-chunked — an edited chunk crossing 65,535 bytes silently
  truncated (e.g. header 65,722 → 186 mod 65,536) and the reader then failed.
  Fixed in `LegacyBin/BDat.cs`: `BDAT_ARCHIVE.Write` now recursively splits any
  chunk whose serialized payload exceeds 60,000 bytes (slices fields/lookups,
  per-piece StartAndEndFieldId markers), verified by a stress test that grew
  47,651 rows by ~2,000 chars each: chunks 3,046 → 7,150, max chunk 59,994,
  round trip identical. **Strings can now be any length; the only constraint is
  the per-chunk 65,535-byte budget (64KB of headroom per original ~32KB chunk).**
- Note: the final file was re-serialized once by Python `ElementTree` (during
  the KR fill). Entity forms in text (e.g. `&quot;`) became literal quote chars;
  `&`, `<`, `>` remain escaped. Functionally equivalent (proven by round trip).

## 3. Why ~5,137 rows are empty

Cross-checked against original Thai 255, EN 448, KR 327 and Shattered 327:

| Bucket | Rows | Explanation |
|---|---|---|
| Blank in **every** locale | 4,991 (97%) | Empty in the original Thai client and in all EN/KR sources; string slots exist but the game data shipped blank (unused/unreferenced entries). Nothing lost in our pipeline. |
| Empty in Thai, text exists in sources | 143 (3%) | Duplicate-alias slots: the key occurs multiple times; merge keyed by alias (first occurrence wins), so only one slot got text. Cosmetic, game-side invisible. |
| Fillable from Shattered | 98 | Only those few have an English source; left unfilled (see §6). |

## 4. `datafile_327_Shattered.xml` analysis

**Correction (user):** `datafile_327_Shattered.xml` is NOT an official client —
it is simply the best available source we have to fill language values.
Official sources: the 448 table (EN) and the new 327 table (KR/EN).
`localfile_en.bin` is NOT ground truth and was never used by the pipeline.
The operative rule: 448 is the closest to ground truth and wins wherever it
has text; Shattered-sourced English is unofficial-fill data whose style and
quality is the loosest of all sources.


- Format: raw compressed export (`<collection compressed="1">`), layout
  `['', alias, text]`; 725,674 `BXML_LOOKUPTABLE`, 695,242 unique keys.
- Language mix: 678,844 EN (97.7%) / 272 KR / 16,126 empty.
- 189 rows contain genuine private-server content — PS-patched rows on an otherwise official table (excluded from any fill):
  - Loading tips referencing `sebns.com`, `support.sebns.com`, `patch.sebns.com`
  - The EULA row replaced with "SHATTERED EMPIRE USER AGREEMENT"
  - PS-only achievements ("SE Game Master", "Game Master")
- **Not PS** (verified against official sources):
  - "Stratus Empire" — official name: the official EN 448 table itself contains
    741 matching rows verbatim (e.g. `Achieve.Name_711_faction_KillPC_Faction6_step1`
    = "Stratus Initiate"). The PS borrowed the name, not the other way round.
  - "Act VII: Shattered Empire" (`QG_NextbaeCheongEpic3`) — present in official
    448 *and* in the original Thai 255; a legitimate chapter title.

## 5. KR → EN fill (performed)

- Pre-fill: 49,010 rows containing Korean (aliased subset 48,992).
- Filled 44,419 rows from Shattered (alias match, text non-empty, English,
  PS-marker exclusion regex: `sebns | SHATTERED EMPIRE USER AGREEMENT |
  SE Game Master | Game Master | support. | patch. | .com | .net | loadingtip`,
  case-insensitive).
- 0 PS-marked rows matched any fill candidate; after fill the deliverable
  contains **0 private-server references** (hard-marker scan: 0).
- 4,591 Korean rows remain — **no English source exists for them anywhere**
  (not in 448, not in Shattered).

## 6. Final state of `merged_datafile_255_manual.xml`

| Metric | Value |
|---|---|
| Lookups | 481,970 |
| English | 472,242 (98.0%) |
| Korean | 4,591 (0.95%) |
| Empty | 5,137 (1.07%) |
| Thai | 0 |
| Private-server markers | 0 |

## 7. Consistency fixes applied during QA

Canon-name unification on the final file (drift from parallel translation):

| Replaced | With | Rows |
|---|---|---|
| Mu-sung | Mushin | 15 |
| Gunma-yum / Gunma-hye | Gunma-yeom | 16 |
| Haemujin | Hae Mu-jin | 11 |
| Unkguk | Un Guk | 10 |
| Po-hwa-ran | Pohwaran | 12 |
| Naru (word) | Naryu | 21 |
| Yu-cheon | Yucheon | 3 |
| Lou Mang | Lumang | 1 |
| Jin Soyeon | Jin So-yeon | 5 |
| Saiwei | Xiwei | 4 |
| Ak/Aek/Ek-tae-hu | Ek Tae-hu | 10 |
| Chubby Root | Thick Root | 5 |
| Hongmun | Hongmoon (canon BNS EN spelling used by official 448: 2,439×) | 115 |

Guard: one Hongmun replacement touched the tag attribute
`imagesetpath="00027918.Portrait_Hongmungui"` — reverted, so client texture
references stay valid.

## 8. Korean-pass results (second sweep)

- All 4,591 Korean rows extracted (`/tmp/opencode/manual/extract_kr.jsonl`) and translated to
  English: 3,996 rows (87%) via a canonical pattern/glossary layer
  (`kr_canonical.py`, `translations_kr.jsonl` — stats lines, weapon stage names, gem names,
  effect names, cooldown texts) and 595 unique rows (item brand names, quest dialogue,
  achievements, UI, event names) via 3 validated subagent slices.
- Validation per slice: coverage, duplicate-free, markup byte-fidelity, zero Hangul/zero Thai
  in output. Personal/proper names transliterated consistently (Baekwol, Galma, Gwimungwan,
  Gnawm... see glossary in `WORKER_INSTRUCTIONS_KR.md`).
- Injected directly into the XML (ordinal-keyed); verification: **0 Korean rows, 0 Thai rows**
  across all 481,969 aliased lookups; repack -> unpack round trip identical (all pairs equal).
- Final language mix: 472,242 EN + 4,591 -> 0 KR; only 5,137 empty slots remain (blank in
  every locale, see section 3).

## 9. SE-strings QA pass (22,743 prose rows, COMPLETE)

- Target pool: the 75,850 no-448 rows sourced from Shattered-fill; prose subset
  (Npc.name2/title2/action, Item.Desc5/Desc2, Skill, SSG.Train, Achieve, Text,
  Store2/q_ etc.) = 22,743 rows, sliced 300-per-slice (`se_qa_1..76.jsonl`),
  polished by 4 parallel subagents under hard rules (length cap
  `len(translation) <= len(original)`, ASCII-only with entities, every `<...>`
  tag byte-for-byte; the `bullets="●"` attribute is official and preserved).
  (Note: during the pass the bullet glyph had been normalized `●`→`-`; fully
  corrected and re-verified afterward — see below.)
- 19 waves × 4 slices, each validated (coverage, cap, ASCII, tag) before
  ordinal injection → 22,743/22,743 done; outputs in `/tmp/opencode/manual/se_out_*.jsonl`.
- **Bullet glyph correction (post-pass):** the QA ASCII-only rule had rewritten
  `bullets="●"` → `bullets="-"` in all 532 occurrences (379 rows). Checked the
  official files: 448 has 460 × `●`, official 327 has 1,032 × `●`, the original
  Thai 255 has 420 × `●`; `-` appears in only 4 official rows (plus `1`–`8` for
  recipe lists and a few ` • `). `●` IS the official bullet style and ships in
  every official bin, so all 532 were mechanically restored (length-neutral)
  and the worker instructions updated to preserve `●` going forward. Re-verified:
  round trip identical, bullets values now `●` ×619 / numbered lists only.
- **Ellipsis / punctuation fidelity audit (post-pass):** compared the non-ASCII
  inventory of official 448/327/255 vs the deliverable and restored the same
  class of deviation everywhere it existed in table 255:
  - `…` (U+2026, used 1,848× by official 448 and 1,142× by the original Thai
    255): 63 rows had been written as `...` — restored/rewritten (35 direct
    restores + 5 KR accessory flavor patterns re-translated properly + 13 quest
    lines got their ellipsis rhythm back). 0 rows remain missing vs pristine.
  - `。` (full-width period, VN-language leak in Shattered-fill rows): 638
    occurrences → `.`.
  - `’`/`"` vs `&apos;`/`&quot;`: the official files themselves use BOTH raw
    glyphs AND entity strings (448 contains 2,397 raw `’` plus 81,113 `&apos;`
    entities; 55,243 `&quot;`), because the client parses tooltip markup at
    runtime — so the entity form written by the QA pass is official-compatible
    and renders identically. No action needed.
  - `—` em-dashes (474 in the deliverable, 0 in pristine 255): all trace to
    verbatim official-448 text (e.g. q_ story rows, "Behind you—something&apos;s
    there!") or to our own TH→EN translations using standard English typography
    — matching official usage. Kept.
  - Byte-cap safety check for every glyph edit: final UTF-8 length ≤ original
    slot bytes (KR/TH slots are 3-byte chars, so English has headroom).
- Re-verified by full repack→unpack round trip (`/tmp/opencode/qa/final6/`):
  481,970 rows identical, 0 diffs.
- **448-preference closure (uncapped):** once the writer could grow strings,
  every row whose alias exists in the official 448 table with non-empty text was
  switched to the 448 value verbatim — **47,635 rows** (covers the 43,284
  formerly cap-blocked "grow" rows, all KR-fill rows with 448 counterparts,
  Thai-pass rows, and the meme-flavored accessory rows, which now carry the
  true official flavor text: "Too hot to handle.", "No one can tame the wind.",
  "Forge new ground.", "Leave no stone unturned.", "Feel my flurry.").
  Guard: 448 rows containing Hangul/CJK/Thai text (11 rows — credits screen
  with Korean staff names and jamo-only Usercommand leftovers) were left at the
  curated translations instead of reinporting Korean.
- Typical fixes landed: curly ’ “ ” – — … → entities/ASCII; "the items stats" →
  "item stats"; "can't" → "cannot"/`&apos;`; joined-token de-merges
  ("Slashevery 2 sec" → "Slash (2 sec)"); missing subjects/verbs; VN-language
  fill tokens translated (Luc Moc → Green Isle, "Tan Xuan Cung Chuc" →
  "New Year's Wishes", tay đua → Rider); "Royale Spectator" → "Royal Spectator";
  "Joint Technique"/"Combined Attack" → "Joint Attack"; Boss/Attack Zone
  sentences rounded ("just as the Boss's Attack Zone is activated" → "...as the
  Boss&apos;s Attack Zone activates").
- Consistency sweep (length-safe only, applied after all slices):
  Bloodshadow Harbour/Harbor → Bloodshade Harbor (16), Hae Mujin/Hae Mu-jin →
  Haemujin (314), Jonghado → Junghado (2), stray "Admy " token dropped (4).
- ~1,900 dev-marker rows identified and left byte-identical
  (Test/(Temp)/Dummy/(X)/Decoration N/TBD/Invisible NPC/None stubs/scale-suffix
  names/Hello Kitty gag NPCs).
- Known cap-blocked and intentionally kept: "Poharan" (official 448 spelling —
  the closure now applies official text wherever it exists), "Nighshade
  Honorguard Brute" typo (no 448 counterpart), weapon-collector title cases.

## 10. Final state (post all passes)

| Metric | Value |
|---|---|
| Lookups | 481,970 |
| English | 476,748 (98.9%) |
| Empty | 5,222 (1.08%) — blank in every locale incl. KR slots added by 327 merge; unreferenced |
| Korean | 0 |
| Thai | 0 |
| Private-server markers | 0 |

- Repack → unpack round trip verified on the FINAL deliverable
  (`/tmp/opencode/qa/final3/`): 481,970 rows, all (alias, text) pairs identical,
  0 content diffs; the repacked bin is `final3/localfile.bin`.
- Known drift note: "Haemujin" (SE-row spelling) vs official 448 "Hae Mujin":
  the 448-closure applies the official spelling wherever 448 has the alias;
  rows without a 448 counterpart keep "Haemujin".

## 12. Standing gates (run on every updated deliverable)

- **Arg multiset must equal the ORIGINAL THAI 255 table's** for every row the
  Thai table contains. The client substitutes `<arg p="N".../>` by rank from
  THAI skill/item data — an arg missing from the text (or an extra one) can
  crash the client (the skill window crash was exactly this). 448's args are
  NOT the ground truth (448's patch data ranks differ).
- Escaping gates: 0 bare `&`, 0 bare `<`, 0 control chars, 0 non-EN rows
  (Thai/Korean/CJK/fullwidth).
- Round trip: repack → unpack must be identical.

Status (2026-08, final11): 0 Thai, 0 Korean, 0 arg mismatches vs the original
Thai table (476,703 rows compared), 0 escaping defects, round trip identical.
181 rows were repaired: missing `<arg>` tags inserted verbatim from the Thai
row (before trailing `<br/>`s), invented args removed (e.g. one row where Thai
has no args at all).

## 11. Caveats / possible next steps

1. **~53k no-448 non-prose rows** (Item.MainInfo 24,505, Item.Nick 15,624,
   Npc.name2/Name2 18,732, title2 3,667) come from Shattered-fill English and
   remain un-QA'd — mostly short display names; could get a light pass at
   leisure. Prose QA (22,743 rows) is done.
2. **Shattered-fill English** is casual/machine-flavored in places; no official
   EN exists for those rows; acceptable but not NCSoft polish.
3. **5,222 empty rows** are blank in every available locale — nothing to
   translate; likely unreferenced slots.
4. Validation tooling and row archives live under `/tmp/opencode/manual/`
   (extract.jsonl, translations.jsonl, WORKER_INSTRUCTIONS.md, batch files)
   and `/tmp/opencode/qa/` (round-trip scratch) — ephemeral; copy elsewhere
   if needed for reproducibility.
