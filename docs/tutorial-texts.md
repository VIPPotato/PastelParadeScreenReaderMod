# Pastel Parade Tutorial Text Extraction

Date: 2026-02-11

Note: This file contains extracted game tutorial/help findings (not mod documentation).

## Sources Searched

- Decompiled C# in `decompiled/`
- Game data in `I:\SteamLibrary\steamapps\common\PastelParade\PastelParade_Data\StreamingAssets`
- Addressables bundles in `I:\SteamLibrary\steamapps\common\PastelParade\PastelParade_Data\StreamingAssets\aa\StandaloneWindows64`

## Language Coverage Found

The game appears to support these language codes:

- `jp` (Japanese)
- `en` (English)
- `zh-CN` (Simplified Chinese)
- `zh-TW` (Traditional Chinese)

Evidence:

- `decompiled/PastelParade/SteamCheckState.cs:31`
- `decompiled/PastelParade/UILocalizeState.cs:49`

## Tutorial Flow Findings

- Tutorial scene load path:
  - If `GameDetailSo.HasTutorial` is true, the game loads `GameDetailSo.TutorialScene`.
  - Otherwise it loads the normal scene.
  - File: `decompiled/PastelParade/LoadTutorialSceneState.cs:16`
- Tutorial can be skipped via `InputCtrl.SkipTutorial`.
  - `SkipTutorial` is currently mapped to `MusicSelect`.
  - Files:
    - `decompiled/PastelParade/InputCtrl.cs:52`
    - `decompiled/PastelParade/GameTutorialStateBase.cs:81`
    - `decompiled/PastelParade/GameTutorialSkipState.cs:22`
- Tutorial text rendering:
  - Tutorial messages are played with `TutorialUI.PlayTextAsync(...)`.
  - The left counter text uses localization key `common.tutorial.left`.
  - File: `decompiled/PastelParade/TutorialUI.cs:58`
- Generic tutorial action model:
  - Action types: `ShowText`, `Perform`
  - Text source field: `TutorialAction.Text` (`MornLocalizeString`)
  - File:
    - `decompiled/PastelParade/TutorialActionType.cs:3`
    - `decompiled/PastelParade/TutorialAction.cs:11`

## Tutorial Scene Coverage

`SceneSettings` includes tutorial scene mappings for many stages:

- `Game1_1Tutorial` to `Game1_4Tutorial`
- `Game2_1Tutorial` to `Game2_4Tutorial`
- `Game3_1Tutorial` to `Game3_4Tutorial`
- `Game4_1Tutorial` to `Game4_5Tutorial`

File: `decompiled/PastelParade/SceneSettings.cs:216`

## Per-Mode Tutorial Input Cues

These are the input cue styles each tutorial scenario requests:

- Band: `PlayAny` (`UseAny = true`)
  - `decompiled/PastelParade.Band/BandTutorialScenario.cs:20`
- CampFire: `PlayAny`
  - `decompiled/PastelParade.CampFire/CampFireTutorialScenario.cs:20`
- Dance: `PlayLeft`, `PlayRight`, `PlayUp` (`UseLeft/UseRight/UseUp`)
  - `decompiled/PastelParade.Dance/DanceTutorialScenario.cs:20`
- Fishing: `PlayAny`
  - `decompiled/PastelParade/FishingTutorialScenario.cs:20`
- Kenpa: `PlayAny`
  - `decompiled/PastelParade.Kenpa/KenpaTutorialScenario.cs:20`
- Splash: `PlayDown`, `PlayLeft` (`UseDown/UseLeft`)
  - `decompiled/PastelParade.Splash/SplashTutorialScenario.cs:20`
- Submarine: `PlayAny`
  - `decompiled/PastelParade.Submarine/SubmarineTutorialScenario.cs:20`
- Treasure: `PlayPower(seconds)`
  - `decompiled/PastelParade.Treasure/TreasureTutorialScenario.cs:20`
- Trampoline: `PlayAny`
  - `decompiled/PastelParade/TrampolineTutorialScenario.cs:20`
- Volley: `PlayAny`
  - `decompiled/PastelParade.Volley/VolleyTutorialScenario.cs:20`

## Extracted Help/Tip Localization Keys

String scanning from addressables bundles found these likely keys/fragments:

- `hub.tips.message1`
- `hub.tips.message2`
- `hub.tips.message3`
- `b.tips.message`

These are likely hub/world tips text entries used by `UIHubTips`.

## Gaps and Limitations

- Human-readable localized tutorial sentences are not present in plain text files in this install.
- Most localize content appears packed in addressable bundles and referenced by keys (`MornLocalizeString`).
- To extract full text content for each language, we likely need Unity asset extraction from the bundles or runtime capture of resolved localized text.
