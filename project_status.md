# Project Status: PastelParadeAccess

## Project Info

- **Game:** Pastel Parade
- **Engine:** Unity 6000.0.23f1
- **Architecture:** 64-bit
- **Mod Loader:** MelonLoader
- **Runtime:** net35
- **Game directory:** I:\SteamLibrary\steamapps\common\PastelParade
- **User experience level:** Little/None
- **User game familiarity:** Somewhat

## Setup Progress

- [x] Experience level determined
- [x] Game name and path confirmed
- [x] Game familiarity assessed
- [x] Game directory auto-check completed
- [x] Mod loader selected and installed (MelonLoader or BepInEx)
- [x] Tolk DLLs in place
- [x] .NET SDK available
- [x] Decompiler tool ready
- [x] Game code decompiled to `decompiled/`
- [x] Tutorial texts extracted (if applicable)
- [x] Multilingual support decided
- [x] Project directory set up (csproj, Main.cs, etc.)
- [x] AGENTS.md updated with project-specific values
- [x] First build successful
- [ ] "Mod loaded" announcement working in game

## Current Phase

**Phase:** Framework
**Currently working on:** Converting `working/` mod to template-aligned structure while preserving behavior.
**Blocked by:** Nothing

## Codebase Analysis Progress

- [ ] Tier 1: Structure overview (namespaces, singletons)
- [ ] Tier 1: Input system (all key bindings documented)
- [ ] Tier 1: UI system (base classes, text access patterns)
- [ ] Tier 2: Game mechanics (analyzed as needed per feature)
- [ ] Tier 2: Status/feedback systems
- [ ] Tier 2: Event system / Harmony patch points
- [ ] Tier 3: Localization system
- [x] Tier 3: Tutorial analysis
- [x] Results documented in `docs/game-api.md`

## Implemented Features

List features with their status:

- **Project Skeleton** - DONE - Root `PastelParadeAccess.csproj`, `Main.cs`, Melon attributes, net472 build pipeline.
- **Working Mod Integration** - DONE - Existing logic integrated and buildable from project root.
- **Code Split (Phase 1)** - DONE - Main logic split into `Main.Core.cs` and `Main.Speech.cs`; hub-specific formatting moved to `HubHandler.cs`.
- **Localization Framework (Mod Strings)** - IN PROGRESS - `Loc.cs` added (`en/jp/zh-CN/zh-TW`) and used for startup + hub tip prefix.

## Pending Tests

What the user should test in the next game session:

- Mod loads in game and announces localized `mod_loaded` line once at startup.
- Enter a hub tip context and confirm tip prefix is localized and spoken correctly.
- Verify no regressions in existing working behaviors (menu navigation, dialog, world map announcements).

## Known Issues

- Large monolithic file still remains:
  - `working/TolkExporter/Patches.cs` is still very large and should be split by feature in next refactor step.

## Architecture Decisions

Document important decisions so future sessions understand the reasoning:

- Use `working/` as the active implementation baseline, then refactor into template-aligned split handler files.
- Treat `reference/` as read-only behavior reference only; do not copy blindly.
- Localization is mandatory from day one: all announced strings go through `Loc.Get()`.
- Language scope for the mod is "all game-supported languages" detected so far: `jp`, `en`, `zh-CN`, `zh-TW`.
- MelonLoader runtime is `net35`; framework planning should target `net472`.
- Root entry class aligned to template filename: `Main.cs` with Melon attributes, logic split via partial class files.

## Key Bindings (Mod)

- F1: Help
- F12: Toggle debug mode

## Notes for Next Session

Write anything the next conversation needs to know:

- User requested strict template flow.
- User confirmed decompiled game code is already available in `decompiled/`.
- User wants `working/` improved: split code into separate `.cs` files per template, ensure localization framework exists, then add more features.
- Start Phase 1 Tier 1 with targeted search (do not full-read large files): namespaces, singleton access points, input bindings, UI text access patterns.
- `scripts/Test-ModSetup.ps1` check on 2026-02-11 now passes fully (16 OK, 0 warnings, 0 errors).
- Next refactor target: split `Patches.cs` into feature files (`Menu`, `Dialogue`, `WorldMap`, `Settings`, `Results`) without behavior changes.
