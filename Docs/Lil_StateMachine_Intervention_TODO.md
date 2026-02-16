# Lil Character Control System — TODO

1) [x] Define Core Events + Bus
- [x] Add `GameEventBus` and emitters in Match-3 core.
- [x] Events: `OnLevelStart`, `OnMoveInputStarted`, `OnMoveCommitted`, `OnCascadeStarted`, `OnCascadeEnded`, `OnBoardRefillComplete`, `OnBigCombo`, `OnNearFail`, `OnObjectiveAlmostDone`, `OnDeadlock`, `OnLevelEnd(Win/Lose)`.

2) [x] Create Data Configs (ScriptableObjects)
- [x] `InterventionLevelConfig`: baseChance, budgetGain rules (per move/event), maxBudget, cooldowns, minMovesBetween, maxPerWindow(N), windowSize, severity thresholds, intensity curve vs progress/time.
- [x] `InterventionStrategySet`: list of strategies with tunable weights/guards per level.
- [x] `StateVFXConfig`: per-state animation/SFX/effect hooks.

3) [x] Implement State Pattern
- [x] Add `LilStateMachine` with `IState` and states: `MenuState`, `HumiliationState`, `Manipulation1State`, `Manipulation2State`, `Manipulation3State`, `SadState`, `LoseState`, `WinState`.
- [x] One‑shot default: auto-return to `MenuState` after min duration (configurable), except Win/Lose.

4) [x] Wire Lil Controller Persistence
- [x] Keep `LilController` with `DontDestroyOnLoad`, hold camera, and re-affirm parenting on scene load.
- [x] Expose `LilController.StateMachine` for Director to drive states and ensure a state machine component exists.

5) [x] Safe Window Gate
- [x] Add a `SafeWindowGate` that opens only on `OnCascadeEnded` (after a preceding `OnMoveCommitted`) and closes on next input (`OnMoveInputStarted`).

6) [x] Director (Budget + Cooldown + Guardrails)
- [x] Add `InterventionDirector` subscribing to bus.
- [x] Maintain budget, cooldown timers, recent intervention window, and move counters.
- [x] Before attempting: check cooldown, minMovesBetween, maxPerWindow(N), baseChance roll (from config), and safe window open.

7) [x] Selector (Strategy-Based)
- [x] Add `StrategyBasedInterventionSelector` using `InterventionStrategySet`.
- [x] Evaluate strategies against context (progress, near fail, combo size, last interventions).
- [x] Choose `Manipulation1/2/3` (or none) with weighted random, honoring severity thresholds and priority.

8) [x] Board Safety Guards
- [x] Add `BoardSafetyService`: `HasAtLeastOneLegalMove()`.
- [x] Director verifies safety before triggering.

9) [x] Run Interventions
- [x] Minimal `EffectRunner` that maps StrategyId to LilStateMachine manip states (one-shots handle exit).
- [ ] Optional: Introduce `IIntervention` concrete implementations later if needed.

10) [x] External Outcome States
- [x] Subscribe Director to `OnLevelEnd` to drive `WinState`/`LoseState` one‑shots.
- [ ] Later: Use `OnNearFail` to escalate severity mapping per design.

11) [x] Designer Tuning Hooks
- [x] Load `InterventionLevelConfig` and `InterventionStrategySet` per level from `GameModeConfig`.
- [x] Expose curves/thresholds via ScriptableObjects.

12) [x] Telemetry/Debug
- [x] Minimal debug HUD shows budget, safe window, state, last combo.
- [ ] (Optional) Counters for attempts/success/skips.

13) [x] Integrate With Input
- [x] SafeWindowGate closes during input/cascades; opens after cascade.
- [x] Director blocks when state machine one-shot is active.

14) [x] Scene Rebinds
- [x] Director re-subscribes to BlockManager progress on scene load; persistent components handle scene changes.

15) [ ] Unit/Integration Harness
- [ ] (Optional) Add a simple simulator to drive events for testing in Editor.


## Suggested File/Class List
- Core/events: `GameEventBus`, `Match3Events`
- State machine: `LilStateMachine`, `IState`, `MenuState`, `HumiliationState`, `Manipulation1State`, `Manipulation2State`, `Manipulation3State`, `SadState`, `LoseState`, `WinState`
- Controller/view: `LilController`, `LilView`, `StateVFXConfig`, `AudioAnimationHooks`
- Director: `InterventionDirector`, `SafeWindowGate`, `BoardSafetyService`
- Selector/strategies: `StrategyBasedInterventionSelector`, `IInterventionStrategy`, `InterventionStrategySet`
- Interventions: `IIntervention`, `Manipulation1Intervention`, `Manipulation2Intervention`, `Manipulation3Intervention`, `EffectRunner`
- Config: `InterventionLevelConfig` (SO), `InterventionStrategySet` (SO)


## Test Checklist
- Budget/cooldown: budget gain/spend, cooldown blocks further triggers.
- Guardrails: min moves between, max per N moves window respected.
- Safe window: no interventions during input/cascades; only after `OnCascadeEnded`.
- Board safety: intervention skipped/downshifted if no legal move would remain.
- Selector: strategy weights/thresholds honored across contexts (near fail, big combo).
- States: each one‑shot enters, runs FX, exits to Menu; Win/Lose fire once.
- Data-driven: per-level configs load and apply without code changes.
- Persistence: controller/state machine persist across scenes and rebind correctly.
