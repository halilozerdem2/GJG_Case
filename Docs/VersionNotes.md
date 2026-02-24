# Version Notes – v1.0.0

## Overview
- Physics-free match puzzle built around a configurable board (2–10 columns/rows, up to 6 colors) defined through `BoardSettings` assets and `GameModeConfig` per-level data.
- Central loop: GameManager drives states (GenerateLevel → SpawningBlocks → WaitingInput → BlastAnimation → Falling / Deadlock → EndGame/Win/Lose) and synchronizes GridManager, BlockManager, UI, Lil character, and audio/event systems.
- All runtime board state lives inside `BoardModel` (simple array “boardmap” of `Cell` structs) and mirrors onto a grid of `Node` MonoBehaviours, allowing deterministic logic independent of visuals.

## Core Gameplay Systems
- **Board creation & scaling** – `GridManager` validates settings, instantiates the background board prefab, spawns `Node` instances in a centered grid, and fits the layout to the camera or designated `playableAreaRect` while keeping a padding envelope so any board size still fits on screen.
- **Pooling & spawning** – `BlockManager` seeds the pool via `ObjectPool`, pre-instantiating every regular, special, and static block prefab plus blast VFX. Spawn operations pull from the pool, attach to the shared `BlocksRoot`, and animate from an elevated offset before settling.
- **Input & group detection** – Player taps are routed to `BlockManager.TryHandleBlockSelection`, which rejects invalid taps, runs a BFS over `BoardModel` and `Node[,]` to identify connected groups ≥ 2 using cached buffers, then proceeds to `ExecuteBlast`.
- **Blast resolution** – `ExecuteBlast` (and the `ChainClearGroup` queue) clears regular blocks immediately, queues adjacent statics/specials, and raises `GameEventBus` events. After clears, cascading is simulated by scanning each column, computing target rows for surviving pieces, animating them down, marking empty cells for refills, and respawning new blocks.
- **Deadlock avoidance** – After every cascade/refill, `ModelHasValidMove` checks the logical board; if no matches remain `ResolveDeadlock` runs `TryShuffleBoard`. This system locks static targets, buckets nodes by color, forces at least two guaranteed pairs, and if even that fails, completely regenerates the board from the pool to ensure the player never sees an unwinnable layout.
- **Objective control** – `ObjectiveController` listens to `StaticBlockSpawned`, target progress, and move/time events to update UI counters, move/time HUDs, and automatically reports win conditions back to `GameManager` once all targets are satisfied.

## Block & Obstacle Catalogue
- **Regular Blocks** – Always groupable; swap their icon per-fuse threshold (A/B/C) to telegraph combo potential and share `blockType` IDs with the SFX lookup.
- **Row / Column Clear (“Rocket”) Blocks** – Created via large blasts or combos. Activation clears their whole row/column, projects a colored “rocket line” VFX, and contributes to combos below.
- **Bomb Blocks** – Square AoE blasts defined by `BombBlock.ExplosionRadius` with radius-multiplier combos. Used both as standalone specials and as conversion targets during color combos.
- **Color Clear Block** – Rotating wildcard orb. On activation it samples either its configured color or the color at its node, fires beam FX (`ColorClearLineEffect`) to every cell matching that color, and can retarget mid-sequence if chained.
- **Static Blocks** – Occupy nodes permanently, can carry an `IceBlock` overlay that takes multiple hits (`TryDamageIce`), cannot be selected, and only clear if a blast touches an adjacent cell or through scripted manipulations/powerups. Each static has its own blast VFX and registers toward objectives.

## Special Combos & Advanced Clears
- **Striped + Striped** – Two row clears create a cross pattern (row + partner’s column); row + column clears both axes simultaneously.
- **Striped + Bomb** – Row/Column + Bomb extends the blast to a triple-row/column sweep centered on the activator.
- **Color + Striped / Bomb** – Converting combos replace nearby regular blocks with stripes matching the partner’s orientation or bombs, animate laser beams to each converted target, then auto-trigger the converted specials. Bomb combos extend their radius by `BombComboRadiusMultiplier` before detonating.
- **Special merges** – When a special spawns from a blast, `SpecialMergeSequence` hides it until the merge animation completes so visual stack-ups feel cohesive.
- **End-game rockets** – Remaining moves spawn random stripes on regular nodes via `TrySpawnEndGameRocket`, override their block type using `GameModeConfig` lookup if provided, and immediately activate them for a celebratory sweep.

## Static Targets & Objectives
- `GameModeConfig.StaticTargetSpawns` defines prefabs plus placement masks, letting designers seed ice cages, crates, etc. per level at load time.
- Static indices are tracked in `BlockManager`, progress is exposed through `TryGetStaticTargetProgress`, and UI counters update via `ObjectiveController`.
- Powerups/manipulations and combo adjacency can also remove statics: adjacency detection lets static blocks near a blast take damage or be flagged for collection events.

## Powerups & Utility Actions
- **Shuffle** – Simple adjacency swaps triggered via powerup button, reusing the same shuffle logic as deadlock recovery and plays the shuffle audio cue.
- **Power Shuffle** – Clears the board (except statics), sorts remaining blocks by color, and re-deals them with bounce-and-scale animations for a dramatic reset.
- **Destroy All** – Vaporizes every non-static block, plays per-block blast FX, queues a full refill, and leaves statics to resolve separately.
- **Destroy Specific** – Removes any block matching the selected `blockType` and then refills, useful for target hunts.
- **Static utilities** – `RestoreAllStaticIce` heals damaged ice overlays (used by Lil’s manipulations) while `ConvertRandomBlocksToStaticTargets` turns random regular nodes into obstacles for challenge spikes.

## Game Modes, Limits & Progression
- Multiple `GameMode` values (Game, Case, Easy, Medium, Hard) can each point to unique `GameModeConfig` assets holding board layout, special thresholds, static spawn rules, Lil manipulation toggles, and limiter settings.
- Move and/or time limits are configurable per level. GameManager tracks remaining moves/time, raises “near fail” events at 90% moves or 80% time, and transitions to Lose/Win when limits expire.
- Level select UI (`LevelsPanel` + `LevelButtonController`) auto-loads configs from `Resources/Levels`, sorts them, and shows lock/current/complete states with up to three stars per level.
- `LevelProgressService` persists highest unlocked level and star counts in PlayerPrefs so players keep their progress between sessions.

## Companion Character – Lil
- `LilManager` controls a persistent character rig + camera, synchronized with `LilStateMachine`. States include menu idle, level intro, waiting, win/lose reactions, humiliation, and manipulation cues.
- When enabled per level, Lil periodically performs manipulations: Variant 1 restores all ice layers (helpful); Variant 2 converts three random blocks into new static targets (hindering). Durations and intervals are defined inside `GameModeConfig`.
- Integrates with GameManager state changes to start intro cinematics on level generation, pause manipulations during cascades/end-game, and ensure consistent behavior across scenes.

## Audio, VFX & Feedback
- `AudioManager` (DontDestroyOnLoad) keeps independent music/SFX/Lil-voice sources, per-color block SFX, invalid selection, shuffle/power shuffle/rocket powerups, and win/lose jingles. Music is auto-swapped per scene via a `SceneMusicEntry` table and respects player mute toggles.
- VFX highlights: rocket line trails, color-clear laser beams, special activation particle pools, block-scale dips during shuffles, invalid tap shakes, and board shake/flash cues when objectives near completion.
- `VibrationManager` / `ButtonVibration` optionally vibrate on UI taps (where platform-supported) and can be toggled via the settings panel.
- `GameEventBus` surfaces high-level beats (level start/end, cascades, deadlocks, big combos, near fail, objectives) so other systems (UI, audio, characters) can respond without tight coupling.

## UI & UX Highlights
- `LevelsPanel`, `LevelCanvasManager`, and `WinLosePanelController` provide polished flows: world map with gating, in-level HUD (objectives, moves, time), win/lose overlays, settings panel, and block-type selection sheets.
- `TargetCollectionAnimator` animates collected statics flying toward HUD counters, while `TargetCollectionAnimator` & `StaticBlockCollected` events enable celebratory sequences.
- Settings cover music/SFX toggles, vibration, and player preferences (via `PlayerSettings` + `SettingsService`).
- `LilManager` and other persistent singletons are automatically created, cached, and reused between scenes for quick reloads.

## Technical Notes
- Deterministic board logic (all block manipulations go through `BoardModel` APIs) decouples gameplay from scene transforms, easing testing and potential platform serialization.
- Extensive pooling for blocks and effects keeps allocations down during cascades/shuffles; hot paths have profiler markers (falling, group detection, shuffles, icon updates) already instrumented for future optimization.
- Battle-tested fallbacks: if shuffling cannot find legal moves, `RegenerateBoardWithGuaranteedPairs` completely rebuilds the board; color-clear sequences suspend the chain resolver until all beams finish to avoid race conditions.
- Save/load ready: Level configs live under `Resources`, so distributing new stages just requires shipping a new config asset with its own BoardSettings + spawn rules.

These notes capture the feature set and mechanical depth included in this build and can accompany store submissions, press kits, or QA/release documentation.
