# Testing

## Test senaryoları
1. **Ana döngü:** MainMenu → Game sahnesi → hamle yap → Win/Lose panelleri → Retry/Main Menu butonları (`WinLosePanelController`).
2. **Powerup doğrulaması:** Shuffle/PowerShuffle/DestroyAll/DestroySpecific butonları `IsPowerupReady` kontrolüne saygı duymalı, cooldown metinleri doğru azalmalı.
3. **Statik hedef akışı:** Wonder Blast modunda hedef blok spawn'ı, `TargetCollectionAnimator` animasyonu ve Objective HUD güncellemelerini takip et; tüm hedefler toplanınca `GameManager.ReportObjectivesCompletion` tetiklenmeli.
4. **Limitler:** Move limitli modda hamle sayısını 0'a düşür (kaybetmeli), time limitli modda süre bitince Lose, hedefler bitince Win senaryosu doğrulanmalı.
5. **Deadlock çözümü:** Bilerek küçük board'da hamle bırakmadan oyna, deadlock tetiklenince shuffle animasyonunun bittiğini ve board'un solvable hale geldiğini gözle.

## Edge case’ler
- **Minimum board:** 2x2 board + 2 renk + threshold A=2; spawn ve shuffle hatası olmamalı.
- **Maksimum board:** 10x10 board + 6 renk; FPS düşüşü veya GC spike olmaması beklenir. `FpsCounter` ile ölç.
- **Static blok yoğunluğu:** `GameModeConfig.StaticTargetSpawns` maskesinde neredeyse tüm board'u kapla; shuffle kilitleri doğru çalışmalı, Objective HUD kalan hedef sayısını doğru göstermeli.
- **Powerup çakışması:** DestroyAll sonrasında hemen Shuffle tetikle; `ForceSpawnAfterBoardClear` → `ResolveDeadlock` sırası bozulmamalı.
- **Settings senkronu:** Müzik/SFX/Vibration toggle'ları `SettingsService` ve `PlayerSettings` ile tutarlı, scene reload sonrası da aynı kalmalı.

## Stress test
- **Patlama fırtınası:** Case modunda threshold'ları düşürüp board'u sürekli toplu blast'larla temizle; `ResolveFalling` ve `SpawnBlocks` sırasında FPS/GC'yi Profiler'da izle.
- **Sürekli shuffle:** Wonder Blast Hard konfigürasyonunda 20+ kez Shuffle tetikle; `TryShuffleBoard` fallback'lerinin kısır döngüye girmediğini doğrula.
- **Powerup spam:** DestroySpecific için hızlıca farklı renkler seç; buffer'ların `groupIndicesBuffer` ve `BlockSearchData` paylaşımla bozulmadığını kontrol et.

## Seed'li test
- Unity Random için harici seed girişi yok; deterministik tekrar gereksiniminde `Random.state` değerini Console'dan kaydedip `Random.state = savedState` yaklaşımıyla manuel olarak enjekte edebilirsin.
- Alternatif olarak `BoardManager.QueueAllNodesForSpawn` öncesinde `Random.InitState(customSeed)` çağrısı için debug hook eklenebilir; şimdilik bu feature kapalı olduğu için tüm seed testleri manuel müdahale gerektirir.
