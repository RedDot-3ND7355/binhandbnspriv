# LegacyBin tool made by Endless

Full documentation inside the LegacyBin folder.

> Previews
<img width="2560" height="1392" alt="image" src="https://github.com/user-attachments/assets/4a24f320-5733-4da1-b01f-e8a0ae2cae70" />
<img width="197" height="177" alt="image" src="https://github.com/user-attachments/assets/1a57875a-14d5-4ee0-b8dc-1c01866d824d" />

## Projects

- **`LegacyBin/`** — the original WinForms app (`net9.0-windows`, migrated from .NET Framework 4.8). Table editor, unpack/repack XML, translation workbench.
- **`LegacyBin.Ava/`** — Avalonia UI (`net9.0`) portable to Linux/macOS/Windows. Covers the translation workbench (export translation XML, merge by alias, fill gaps via Google Translate, apply) plus unpack/repack-to-XML, and a headless CLI.

Both projects share the same engine sources (`BDat.cs`, `LocalfileTranslation.cs`, `AutoTranslateService.cs`, …) — kept buildable for both targets.

## Build

```sh
# everything (WinForms + Avalonia)
dotnet build LegacyBin.sln

# just the Linux/portable app
dotnet build LegacyBin.Ava

# just the Windows WinForms editor
dotnet build LegacyBin/LegacyBin.csproj
```

The WinForms project builds cross-platform but only *runs* on Windows. Use **LegacyBin.Ava** on Linux.

## Run

```sh
dotnet run --project LegacyBin.Ava          # GUI (translation workbench)
dotnet run --project LegacyBin.Ava -- unpack localfile.bin [outDir]   # CLI
dotnet run --project LegacyBin.Ava -- repack localfile.bin outDir     # CLI
dotnet run --project LegacyBin.Ava -- merge src.xml target.xml out.xml # CLI
```

Merge accepts both BnsDatTool `Translation.xml` (`<table>`) and unpacked `datafile_XXX.xml` (`<list>`) on either side; the output keeps the target's format/ids.

Note: the UI renders Thai/BNS text — install a font covering the script (e.g. `noto-sans-thai` / `noto-sans-cjk`) on the host if glyphs look missing.

## Linux self-contained build

```sh
dotnet publish LegacyBin.Ava/LegacyBin.Ava.csproj -c Release -r linux-x64 --self-contained -o artifacts/linux-x64
./artifacts/linux-x64/LegacyBin.Ava          # run without a .NET install
```

Same for `linux-arm64`. The GUI needs a display (X11/Wayland) and the usual system libs (`fontconfig`, GTK3).

## Headless UI smoke test (CI-friendly, no display needed)

```sh
dotnet run --project LegacyBin.Ava.Tests -- LegacyBin/Resources/localfile.bin
```

Constructs the Avalonia windows via `Avalonia.Headless` and exercises open-bin, export, merge and apply through the shared engine.
