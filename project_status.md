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

**Phase:** Bugfix and UX hardening
**Currently working on:** Second pass on settings/calibration/menu speech quality based on latest user log feedback.
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
- **Localization Framework (Mod Strings)** - DONE - `Loc.cs` expanded and used for startup + runtime toggle announcements + slider/menu position speech labels.
- **Persistent Runtime Toggles** - DONE - `F12` debug mode and `F3` menu position announcements, both saved via MelonPreferences.
- **Selection Spam Reduction** - DONE - Added stronger same-target selection dedupe to stop repeated `Start/Settings/Close` spam.
- **Speech Formatting Improvements** - DONE - Slider announcements now include role (`<label> slider <value>`), dialogue uses `Name： Text`.
- **Timing Calibration Fixes** - DONE - Suppressed calibration `Test` spam; timing value speech normalized to `ms`.
- **World Map / Hub Trigger Remap** - DONE - Custom cycle controls support keyboard `[ ]` + gamepad `LT/RT`, no longer tied to `TabLeft/TabRight` fallback.
- **Info + Menu Merge** - DONE - Added prefix merge flow so detail text and follow-up menu action can be spoken as one utterance.
- **Settings Tab Announcement Merge** - DONE - Sound/display tab prefixes are now queued and merged with first focused item as one utterance.
- **Calibration Test Feedback Hook** - DONE - Added `UITimingBar.MakeCopy` + `UITimingSettingState.UpdateTimingOffset` hooks for offset value announcements.
- **Known JP Label Localization Overrides** - DONE - Added localization mapping for common fixed labels (`もどる`, `フルスクリーン`, `Sound Check`, placeholder `テキスト`).
- **Slider Value-Only Change Speech** - DONE - Slider value changes now prefer value-only output path to avoid repeating option label.
- **Main Menu Version Merge** - DONE - Version text now queues into first main-menu selection utterance instead of speaking separately.

## Pending Tests

What the user should test in the next game session:

- Main menu first focused item is merged with version in one utterance (example: `ver 1.0.2 Start 1 of 3`).
- Settings entry now speaks tab + item together (one utterance), and no separate trailing `Audio`/`Display` line.
- Settings close position is corrected (not inflated like `13 of 13`) in sound/display/timing pages.
- Slider value changes announce only value changes (no immediate label-repeat line).
- In timing calibration:
  - offset changes are announced reliably with `ms`
  - pressing `Test` announces offset slider value
  - `Close/Test` position counts are reasonable
- Stories/sound-check lists no longer double-speak item name with a second duplicate line when position mode is enabled.
- Japanese fixed labels are localized through `Loc` (`もどる`, `フルスクリーン`, `Sound Check` rename path, placeholder `テキスト` fallback).
- `F12` toggles debug mode and persists across game restarts (`MelonPreferences.cfg`).
- `F3` toggles menu position mode and persists; when enabled, selection includes position (example `Close 5 of 5`).
- On map/hub custom snapping, gamepad `LT/RT` cycles targets and `LB` no longer conflicts through mod fallback.
- Entering level/detail menus avoids immediate `Play` echo after merged detail+action line.
- Dialogue line format has a space after speaker colon (`Amane： Still thinking about fish?`).

## Known Issues

- Large monolithic file still remains:
  - `working/TolkExporter/Patches.cs` is still very large and should be split by feature in next refactor step.
- Hub placeholder entries still need richer context mapping:
  - current fallback localizes `テキスト` to a generic item label, but per-item semantic naming is not complete yet.

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
- F3: Toggle menu position announcements
- F12: Toggle debug mode
- `[` / `]`: Cycle map/hub interactables
- Gamepad `LT` / `RT`: Cycle map/hub interactables

## Notes for Next Session

Write anything the next conversation needs to know:

- User requested strict template flow.
- User confirmed decompiled game code is already available in `decompiled/`.
- User wants `working/` improved: split code into separate `.cs` files per template, ensure localization framework exists, then add more features.
- Recent local commits:
  - `e8e5522` - persisted `F12` debug + `F3` menu position toggles
  - `4cddc4f` - selection spam reduction + slider/dialog speech formatting
  - `6075797` - timing calibration fixes + trigger remap + detail/menu merge
  - `9e71f36` - settings/tab merge refinements, calibration hooks, and localization overrides for fixed JP labels
- `docs/game-api.md` updated with verified trigger availability and current mod key usage.
- `scripts/Test-ModSetup.ps1` check on 2026-02-11 now passes fully (16 OK, 0 warnings, 0 errors).
- Next refactor target: split `Patches.cs` into feature files (`Menu`, `Dialogue`, `WorldMap`, `Settings`, `Results`) without behavior changes.
- TODO (future feature): album full-screen image descriptions should be announced when an image is opened.
