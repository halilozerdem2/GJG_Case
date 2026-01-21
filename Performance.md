# Performance

## CPU / GC optimizasyonları
- **BoardModel merkezli hesap:** `BlockManager` tüm taramaları tek boyutlu `Cell[]` üzerinde yürüttüğü için komşu aramaları `index = x + y * columns` formülüne indirgenir, LINQ veya `List<Node>` taramaları kullanılmaz.
- **Buffer yeniden kullanımı:** Flood-fill, shuffle ve ikon yenileme işlemleri için ayrılan `bfsQueue`, `groupIndicesBuffer`, `visitedStamps`, `dirtyIndices` gibi diziler board boyutu kadar allocate edilir; her çağrıda `Array.Clear` yerine damgalama (`visitStamp`, `groupEvaluationStamp`) uygulanır ve GC alloc oluşmaz.
- **Gravity & falling:** `ResolveFalling` her kolonu yukarıdan aşağıya tek geçişte sıkıştırır, hareket eden blokları `blockMoves` listesinde toplar ve düşüş animasyonlarını tek seferde tetikler. Bu akış için `ProfilerMarker` kullanıldığı için mobil hedefte CPU bütçesi kolay izlenir.
- **Limit sayaçları:** `GameManager` zaman ve hamle sınırlarını event tabanlı günceller; coroutine yalnızca zaman limiti aktifken çalışır ve `Time.unscaledDeltaTime` kullanıldığı için pause durumunda CPU israfı olmaz.

## Object pooling
- **Block havuzu:** `ObjectPool.Initialize` board hücre sayısına göre her block tipi için yeterli instansı önceden üretir. `ReleaseBlock` çağrıları object'i pasif parent'a taşıyıp durumunu sıfırlar; böylece `Instantiate/Destroy` kaynaklı hıçkırıklar engellenir.
- **Partikül havuzu:** Her renk için patlama efektleri `effectPools` altında tutulur, `SpawnBlastEffect` world konumuna yerleştirip oynatır, animasyon bitince `ReleaseBlastEffect` geri sıraya alır.
- **Special ve static bloklar:** `GameModeConfig.SpecialBlockPrefabs` aynı pooling sistemini kullanır; row/column/bomb blokları regular havuza dahil olduğu için ilave setup maliyeti yoktur.

## Batch / draw call yaklaşımı
- **Tek materyal kullanımı:** Tüm blok sprite'ları aynı atlas/mat altında tutulur; `Block` prefab'ları SpriteRenderer seviyesinde paylaşılan materyalle gelir. Shuffle/drop sırasında sadece `transform.localPosition` değiştiği için ek mat state değişimi olmaz.
- **Sıralama kontrolü:** `Node.SetSortingOrder` her satır için artan order belirler, `SpecialBlock` override ederek 20. sıraya konur; böylece Unity'nin `Transparent` queue'su tekrar sıralama yapmak zorunda kalmaz.
- **Board ölçeklendirme:** `GridManager.FitBoardToScreen` board arka planını tek mesh üstünden ölçekler; UI elementleri (Objective HUD vb.) ayrı canvas'larda tutulduğu için draw call çakışması azaltılır.

## Profiler screenshot referansları
- `Docs/Profiler/case-mode-128x8.png`: Case modunda 10x12 board, shuffle + drop sekansı; CPU: ~6.7 ms, GC: 0 B.
- `Docs/Profiler/enhanced-mode-powerups.png`: Case + Enhancements modunda eş zamanlı row/column clear + hedef toplama; CPU: ~8.3 ms, GC: 0 B.
- `Docs/Profiler/wonder-blast-stress.png`: Wonder Blast modunda maksimum renk (6) ve tüm powerup'ların ardışık tetiklenmesi; CPU: ~9.5 ms, GC spike yok.
- Eğer yeni kombinasyonlar test edilirse aynı klasöre tarih damgalı PNG ekleyip ilgili satırlara kısa not düşülmelidir.
