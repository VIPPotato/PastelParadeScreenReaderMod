# Accessibility Mod Template

## User

- Blind, screen reader user
- Experience level: asked during setup → adjust communication
- User directs, Codex codes and explains
- Uncertainties: ask briefly, then act
- Output: NO `|` tables, use lists

## Session Start

On greeting:
1. Read `project_status.md` — summarize phase, last work, pending tests, notes
2. If pending tests exist, ask user for results before continuing
3. Suggest next steps or ask what to work on
Update `project_status.md` on significant progress and before session end.

## Environment

- **OS:** Windows (Bash/Git Bash)
- **Game directory:** I:\SteamLibrary\steamapps\common\PastelParade
- **Architecture:** 64-BIT
- **Mod Loader:** MELONLOADER

## Coding Rules

- Handler classes: `[Feature]Handler`
- Private fields: `_camelCase`
- Logs/comments: English
- Build: `dotnet build [ModName].csproj`
- XML docs: `<summary>` on all public classes/methods. Private only if non-obvious. Critical for dev integration.
- Localization from day one: ALL ScreenReader strings through `Loc.Get()`. No exceptions. `Loc.cs` = Phase 2 framework, not later addition. Even for single-language mods.

## Coding Principles

- **Playability** — play as sighted do; cheats only if unavoidable
- **Modular** — separate input, UI, announcements, game state
- **Maintainable** — consistent patterns, extensible
- **Efficient** — cache objects, skip unnecessary work
- **Robust** — utility classes, edge cases, announce state changes
- **Respect game controls** — never override game keys, handle rapid presses
- **Submission-quality** — clean enough for dev integration, consistent formatting, meaningful names, no undocumented hacks

Patterns: `docs/ACCESSIBILITY_MODDING_GUIDE.md`

## Error Handling

- Null-safety with logging: never silent. Log via DebugLogger AND announce via ScreenReader.
- Try-catch ONLY for Reflection + external calls (Tolk, changing game APIs). Normal code: null-checks.
- DebugLogger: always available, active only in debug mode (F12). Zero overhead otherwise.

## Before Implementation

1. Search `decompiled/` for real class/method names — NEVER guess
2. Check `docs/game-api.md` for keys, methods, patterns
3. Only use safe mod keys (game-api.md → "Game Key Bindings")
4. Large files (>500 lines): targeted search first (Grep/Glob), don't auto-read fully

## Session & Context Management

- Feature done → suggest new conversation to save tokens. Update `project_status.md`.
- ~30+ messages → remind about fresh conversation (AI re-reads everything per message)
- Before ending/goodbye → always update `project_status.md`
- Never re-read decompiled code already documented in `docs/game-api.md`
- After new code analysis → document in `docs/game-api.md` immediately
- Problem persists after 3 attempts → stop, explain, suggest alternatives, ask user

## References

- `project_status.md` — central tracking (read first!)
- `docs/ACCESSIBILITY_MODDING_GUIDE.md` — code patterns
- `docs/technical-reference.md` — MelonLoader, BepInEx, Harmony, Tolk
- `docs/unity-reflection-guide.md` — Reflection (Unity)
- `docs/state-management-guide.md` — multiple handlers
- `docs/localization-guide.md` — localization
- `docs/menu-accessibility-checklist.md` — menu checklist
- `docs/menu-accessibility-patterns.md` — menu patterns
- `docs/game-api.md` — keys, methods, patterns
- `docs/distribution-guide.md` — packaging, publishing
- `docs/git-github-guide.md` — Git/GitHub intro
- `templates/` — code templates
- `scripts/` — PowerShell helpers
