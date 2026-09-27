# Argon

Argon is a Unity Mod Manager (UMM) mod for **A Dance of Fire and Ice (ADOFAI)**. It provides an in-game HUD, a configurable key viewer, and an editor for arranging and styling both.

> **Status:** Development build. The project builds locally, but the current UI/runtime has not been comprehensively verified in-game. Use backups and report compatibility issues with the game version and mod list.

## What it does

- HUD modules for FPS, progress, accuracy, X-Score, BPM/KPS, status, records, judgements, combo, and progress bars.
- In-game HUD layout editor with anchors, drag/resize, profiles, and per-element styling.
- Hand-key layouts with **10, 12, 16, and 20 keys**, plus configurable foot-key layouts, custom mappings/labels, counters, and rain effects.
- Key-viewer canvas editor with zoom, pan, grid snapping, reset, and undo/redo for layout edits.
- Appearance options and a public API for other mods to register HUD elements. See [docs/API.md](docs/API.md).
- Settings are stored locally at `Application.persistentDataPath/Argon/config.json`.

Open settings with **Ctrl + Shift + O**.

## Build

Requirements: .NET SDK, a local ADOFAI installation, and a local O5Kit checkout (including its required submodule).

1. Copy `Directory.Build.props.example` to `Directory.Build.props` and set `GamePath` and `O5KitProject`.
2. Initialize O5Kit's submodules if needed:

   ```sh
   git -C ../O5Kit submodule update --init --depth 1
   ```

3. Build the UMM/Mono target:

   ```sh
   dotnet build Argon.csproj -c Release_Mono
   ```

The mod package is staged under `dist/Argon` (ignored by Git). To run the lightweight geometry/input-ownership regression checks:

```sh
dotnet run --project tests/Argon.Regression/Argon.Regression.csproj
```

These checks do not launch ADOFAI and are not a substitute for in-game testing.

## Upstream references and notices

- **JipperResourcePack (JRP)** — its BSD 3-Clause-licensed key-viewer layout definitions were used as the reference for the 10/12/16/20-key ordering, coordinates, key widths, and related layout values. Argon's C# implementation is adapted; JRP code/assets are not bundled. The required BSD notice and disclaimer are in [`THIRD_PARTY_NOTICES/JipperResourcePack-LICENSE.txt`](THIRD_PARTY_NOTICES/JipperResourcePack-LICENSE.txt). Upstream: [JipperResourcePack](https://github.com/yeonu-me/JipperResourcePack).
- **DM Note** — its editor interaction patterns and visual design were used as UI/UX references. No DM Note source code or assets are bundled. DM Note is distributed under **GPL-3.0-only**: [DM Note](https://github.com/yeonu-me/dm-note).
- **O5Kit** — external UI/library dependency referenced by the build, distributed under **LGPL-3.0-or-later**. It is maintained separately and is not copied into this repository: [O5Kit](https://github.com/modlist-org/O5Kit).

These notices apply to the named upstream work only; they do not, by themselves, declare an outbound license for Argon's own source. No separate Argon project license is declared in this repository yet.
