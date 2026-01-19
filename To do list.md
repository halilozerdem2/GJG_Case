# TODOs for Case Study Implementation

## Phase 1 — Case Study Implementation (Completed)

### Beklenen Performans Kazanımları
- CPU: ~%20 düşüş (deterministic flood fill + O(M*N) gravity).
- GPU: ~%15 daha az draw call (tek atlas + batch).
- GC/Memory: <0.5 KB/frame hedefi (object pool + koleksiyon hijyeni).


### Core Tasks
- [x] **Board Configuration & Validation**
  - Replace the loose `_width`, `_height`, and `blockTypes` fields with a single configuration data source (ScriptableObject or serialized settings struct).
  - Enforce the brief’s limits (`2 ≤ M,N ≤ 10`, `1 ≤ K ≤ 6`, thresholds `A,B,C` in ascending order).
  - Surface these settings to designers (inspector UI) and propagate them to the board generator so every play session respects the case rules.
- [x] **Dynamic Block Icons by Group Size**
  - Extend `Block` to store references to its `SpriteRenderer` and the 4 sprite variants (default + 3 tier icons).
  - When a group is discovered, update every block’s icon based on group size vs. thresholds (default / A / B / C tiers).
  - Ensure icon state refreshes when groups change (after blasts, drops, and spawns).
- [x] **Deadlock Detection & Smart Shuffle**
  - After each grid change, scan for any blastable group (size ≥ 2). If none exist, trigger a shuffle routine.
  - Implement a deterministic shuffle that preserves column counts and only performs swaps that introduce at least one valid group (no random retries without checks).
  - Integrate the shuffle into the `GameState` flow so the board never stays in a deadlocked “WaitingInput” state.
- [x] **Flood-Fill Result Reuse**
  - Refactor flood fill to return both the visited set and the group size per block.
  - Cache these results long enough to drive the icon updates and deadlock checks, avoiding duplicate DFS/BFS passes in a single frame.
- [x] **Efficient Grid/Data Representation**
  - Replace repeated `_nodes.FirstOrDefault(...)` lookups (see `UpdateGrid`) with a `Node[,]` or per-column list for `O(1)` access.
  - Re-implement gravity/drop logic to run in `O(M*N)` by compacting columns without nested LINQ enumerations.
- [x] **Block Collection Hygiene**
  - Clean up `_blocks`: remove references when a block is blasted or convert it into an object pool list; otherwise it retains destroyed objects and wastes memory.
  - Decide whether `_blocks` should even exist (could derive live blocks from node occupancy when needed).
- [x] **Object Pooling & Spawn Optimizations**
  - Replace `Instantiate`/`Destroy` loops with a pool that recycles `Block` instances.
  - Reset block state (icon, node reference, animations) when reusing objects to minimize CPU/GPU spikes and GC pressure.
- [x] **General Polish & Monitoring**
  - Hook up lightweight instrumentation (Unity Profiler markers/logs) to verify memory/CPU/GPU targets during mass blasts.
  - Create automated or editor-time validation to ensure new configs obey the case-study constraints before entering play mode.

### Improvements
- [x] **Deadlock Shuffle Fallback**
  - When `TryShuffleBoard` can’t find any color pair, provide a deterministic fallback (regenerate the board, reduce K, or spawn guaranteed pairs) so the game never loops in the `Deadlock` state.
- [x] **Prefab Icon Validation**
  - Extend editor-time validation to ensure every `Block` prefab has all four sprite tiers assigned and that sprites are not duplicated across colors, keeping the “unique icons per color/group size” promise.
- [x] **Flood-Fill Allocation Reduction**
  - Rework `Block.FloodFill`/`RefreshGroupVisuals` to reuse buffers or share traversal data so large blasts no longer allocate fresh `HashSet`/`Stack` instances each frame.
- [x] **Spawn List Reuse**
  - Replace the `FreeNodes.ToList()` call in `SpawnBlocksCoroutine` with a cached list or direct iteration to avoid repeated GC spikes during mass refills.

### Bugs
- [x] **Shuffle Scale Drift**
  - Repeatedly pressing Shuffle while animations play leaves blocks progressively smaller once they settle back, accumulating scale loss over time.

## Phase 2 — Performance-First Refactor (Next Iteration)

### Phase 2 Performans Hedefleri
- CPU: ek ~%15 düşüş (BoardModel BFS + dirty bölgeler + move listeleri).
- GPU: 20-25 draw call tavanı (tek atlas/Tilemap/mesh).
- GC/Memory: oyun döngüsünde 0 GC alloc, istikrarlı bellek kullanımı.


- [ ] **Acceptance Criteria (Must-have metrics)**
  - Zero GC Alloc during gameplay loop (tap → blast → drop → refill → icon refresh).
  - Stable frame time under stress (mass blast + full refill) on target mobile profile.
  - Bounded draw calls (same material/atlas, minimal state changes).
  - Deterministic deadlock resolution (guarantees at least one valid group without retry loops).
- [x] **Core Data Model (Decouple simulation from GameObjects)**
  - Create a `BoardModel` as a 1D array (`Cell[] cells`) where `Cell` is a struct with `byte colorId`, `byte iconTier (0..3)`, and `bool occupied` (or `colorId = 255` for empty).
  - Add helper methods for `O(1)` indexing: `Index(x,y)`, `X(i)`, `Y(i)`.
  - Move blast/drop/refill logic to operate only on `BoardModel` (no transforms, no nodes) for fewer transform touches and better CPU/memory behavior.
- [x] **Group Detection (Stamp-based BFS)**
  - Replace `HashSet<Block>` groups with preallocated buffers: `int[] queueOrStack`, `int[] buffer`, `int[] visitedStamp`, plus `int currentStamp`.
  - Implement `FindGroup(startIndex) -> (count, groupIndicesSpan)` with no allocations.
  - Implement a fast valid-move check: either scan BFS only when needed or run a quick adjacency check before BFS.
- [x] **Incremental Updates (Dirty Region Refresh)**
  - Introduce a dirty set of indices/columns impacted by the last action (blasted groups, neighbors, collapsed/refilled columns).
  - Recompute groups and icon tiers only for dirty cells while guaranteeing correctness after blast, gravity, refill, and shuffle.
- [x] **Rendering Layer (Keep visuals cheap and predictable)**
  - Choose one approach (Path A: pooled GameObjects, Path B: tilemap, Path C: single mesh) and ensure view objects become thin wrappers that simply set visuals and run animations.
  - Avoid `Transform.SetParent` churn; keep a stable parent and animate local positions.
  - Update only the indices that changed when refreshing visuals.
- [x] **Gravity/Refill Optimization (Model-first, view-second)**
  - Implement column compaction in the model using a per-column write pointer.
  - Produce a move list `(fromIndex -> toIndex)` plus spawned indices, and drive animations strictly from that data.
- [x] **Deadlock Handling (Deterministic & Low Cost)**
  - Keep the guaranteed-pair philosophy but detect deadlocks using only the model (no cached group structures).
  - After shuffling, run a single-pass guarantee patch that performs the minimal swap required to introduce a pair if none exists.
- [x] **Tween/Animation Budget (Reduce per-block allocations)**
  - Replace per-block DOTween sequences in hot paths with a lightweight custom tween runner or simplified DOTween usage (no nested sequences).
  - Fix shuffle scale drift permanently by storing an immutable `baseScale` per block and always animating relative to it.
- [x] **Grid/Node Lifecycle (Eliminate regenerate spikes)**
  - Board configuration UI now just updates the shared `BoardSettings` asset (rows/columns + thresholds) on the Main Menu; gameplay scenes build a fresh grid once at load.
  - Grid/board objects are instantiated once per scene load (no runtime regenerate button); to test new sizes, exit to Main Menu, edit settings, and reload the scene.
- [ ] **Memory & Collections Hygiene**
  - Replace dictionaries with arrays/lists wherever indices suffice (node grids, views, cells).
  - Pre-size lists and reuse buffers; avoid LINQ/yield patterns in hot loops to prevent hidden allocations.
- [ ] **Instrumentation & Regression Safety**
  - Add `ProfilerMarker`s for `GroupDetection`, `IconTierUpdate`, `GravityCompaction`, `Refill`, and `DeadlockCheck`.
  - Add an editor/playmode config validator that blocks play when `M/N` or `K` are out of bounds, thresholds `A/B/C` are invalid, or sprite tiers are missing.
