# TODOs for Case Study Implementation

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

- [ ] **Efficient Grid/Data Representation**
  - Replace repeated `_nodes.FirstOrDefault(...)` lookups (see `UpdateGrid`) with a `Node[,]` or per-column list for `O(1)` access.
  - Re-implement gravity/drop logic to run in `O(M*N)` by compacting columns without nested LINQ enumerations.

- [x] **Block Collection Hygiene**
  - Clean up `_blocks`: remove references when a block is blasted or convert it into an object pool list; otherwise it retains destroyed objects and wastes memory.
  - Decide whether `_blocks` should even exist (could derive live blocks from node occupancy when needed).

- [x] **Object Pooling & Spawn Optimizations**
  - Replace `Instantiate`/`Destroy` loops with a pool that recycles `Block` instances.
  - Reset block state (icon, node reference, animations) when reusing objects to minimize CPU/GPU spikes and GC pressure.

- [ ] **General Polish & Monitoring**
  - Hook up lightweight instrumentation (Unity Profiler markers/logs) to verify memory/CPU/GPU targets during mass blasts.
  - Create automated or editor-time validation to ensure new configs obey the case-study constraints before entering play mode.

## Improvements

- [x] **Deadlock Shuffle Fallback**
  - When `TryShuffleBoard` can’t find any color pair, provide a deterministic fallback (regenerate the board, reduce K, or spawn guaranteed pairs) so the game never loops in the `Deadlock` state.

- [x] **Prefab Icon Validation**
  - Extend editor-time validation to ensure every `Block` prefab has all four sprite tiers assigned and that sprites are not duplicated across colors, keeping the “unique icons per color/group size” promise.

- [x] **Flood-Fill Allocation Reduction**
  - Rework `Block.FloodFill`/`RefreshGroupVisuals` to reuse buffers or share traversal data so large blasts no longer allocate fresh `HashSet`/`Stack` instances each frame.

- [ ] **Spawn List Reuse**
  - Replace the `FreeNodes.ToList()` call in `SpawnBlocksCoroutine` with a cached list or direct iteration to avoid repeated GC spikes during mass refills.

## Bugs

- [ ] **Shuffle Scale Drift**
  - Repeatedly pressing Shuffle while animations play leaves blocks progressively smaller once they settle back, accumulating scale loss over time.
