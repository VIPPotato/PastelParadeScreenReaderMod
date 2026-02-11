# PastelParade - Game API Documentation

## Overview

- **Game:** Pastel Parade
- **Engine:** Unity 6000.0.23f1
- **Runtime:** net35 (from MelonLoader log)
- **Architecture:** 64-bit
- **Developer:** PastelParadeProject

---

## 1. Singleton Access Points

Initial findings only (full Tier 1 analysis pending).

- `MornGlobalBase<MornLocalizeGlobal>.I` - localization settings access
- `MornGlobalBase<MornUGUIGlobal>.I` - UI submit/cancel clips and input globals

---

## 2. Game Key Bindings (Do Not Override)

Physical key mapping is still pending because input actions are abstracted via `IMornInput`.

Confirmed action bindings:

- `Tutorial` uses `MusicSelect`
- `SkipTutorial` uses `MusicSelect`
- Other key actions present in `InputCtrl`: `RhythmLeft`, `RhythmRight`, `RhythmUp`, `RhythmDown`, `RhythmOther`, `Menu`, `TabLeft`, `TabRight`, `Submit`, `Cancel`, `Click`
- Input System gamepad controls confirmed in `MornInputCursorShowHide` include:
  - face buttons
  - shoulders (`leftShoulder`, `rightShoulder`)
  - triggers (`leftTrigger`, `rightTrigger`)
  - sticks and D-pad

Source:

- `decompiled/PastelParade/InputCtrl.cs`
- `decompiled/MornInput/MornInput/MornInputCursorShowHide.cs`

---

## 3. Safe Keys for Mod

Not finalized yet. Must complete full input analysis before assigning feature keys.

Currently reserved by template:

- `F1` help
- `F12` debug toggle

Current mod usage (implemented):

- `F3` toggle menu position announcements
- `F12` toggle debug mode
- `[` / `]` cycle custom map/hub targets
- gamepad `LT` / `RT` cycle custom map/hub targets
  - note: intentionally avoids `TabLeft`/`TabRight` game actions to prevent `LB` conflict with the game's hub open behavior

---

## 4. UI System

Initial tutorial-related UI findings:

- `TutorialUI` handles tutorial dialog text and remaining-count prompt.
  - Localized counter key: `common.tutorial.left`
  - Uses private serialized fields (`_talkPanel`, `_leftCountText`, etc.) and dependency injection.
- `UIHubTips` displays localized tip text from interacted object data.
  - Reads `InteractedObjectInfo.Tips.Get(_localize.CurrentLanguage)`

Likely reflection relevance:

- Many fields are `[SerializeField] private ...`; direct access will often require reflection in mod code.

Source:

- `decompiled/PastelParade/TutorialUI.cs`
- `decompiled/PastelParade/UIHubTips.cs`
- `decompiled/PastelParade/HubObjectInfo.cs`

---

## 5. Game Mechanics - Feature Catalog

Pending detailed Tier 2 analysis.

Tutorial mode coverage discovered:

- Band
- CampFire
- Dance
- Fishing
- Kenpa
- Splash
- Submarine
- Treasure
- Trampoline
- Volley

Each mode has a dedicated `*TutorialScenario` with its own input cue behavior.

---

## 6. Status and Notifications

Pending.

---

## 7. Audio System

Tutorial subsystem uses audio source mixing in `GameTutorialService` to switch loop/melody layers.

Source:

- `decompiled/PastelParade/GameTutorialService.cs`

---

## 8. Save and Load

Initial localization-related save info:

- Save data contains language and a first-run system-language confirmation flag.
- `SteamCheckState` maps Steam language to internal codes and persists to save.

Source:

- `decompiled/PastelParade/SteamCheckState.cs`
- `decompiled/PastelParade/SaveManager.cs`

---

## 9. Event Hooks for Harmony Patches

Early candidate hook points:

- `LoadTutorialSceneState.OnStateBegin()` for tutorial scene routing.
- `GameTutorialStateBase.OnStateUpdate()` for skip behavior.
- `TutorialUI.PlayTextAsync(...)` for tutorial text announcement interception.

These are provisional and need validation during implementation.

---

## 10. Localization

Confirmed language codes in game logic:

- `jp`
- `en`
- `zh-CN`
- `zh-TW`

Localization is key-based (`MornLocalizeString` + `MornLocalizeSettings.Get(language, key)`).

Related extracted keys:

- `common.tutorial.left`
- `hub.tips.message1`
- `hub.tips.message2`
- `hub.tips.message3`
- `b.tips.message`

Detailed extraction notes:

- `docs/tutorial-texts.md`

---

## 11. Known Issues and Workarounds

- Input action names are known, but actual physical keys are not yet mapped in decompiled C#.
- Most localized text appears in addressable assets, not plain-text files.

---

## 12. Not Yet Analyzed

- [ ] Namespace inventory and singleton catalog (full Tier 1)
- [ ] Complete input mapping including physical key conflicts
- [ ] Full UI hierarchy and text access patterns
- [ ] Gameplay systems tied to first target feature
- [ ] Harmony patch stability points for menu and gameplay flows

---

## Change Log

- **2026-02-11:** Initial setup documentation created with tutorial/localization baseline findings.
- **2026-02-11:** Added verified gamepad trigger availability and documented current mod key usage (`F3`, `F12`, `[`, `]`, `LT`, `RT`).
