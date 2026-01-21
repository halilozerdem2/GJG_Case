# Architecture

## Board sistemi
- **Durum makinesi:** `GameManager` sahne kurulumu tamamlandığında `GenerateLevel → SpawningBlocks → WaitingInput → Falling/Deadlock → Win/Lose` akışını sürdürür, `GridManager` ve `BlockManager` referanslarını sahnede bulur ve `BoardSettings` değerlerini her ikisine push eder.
- **Grid üretimi:** `GridManager` node prefab'larını `boardSettings.Columns x boardSettings.Rows` ızgara olarak instantiate eder, hem `Node[,]` matrisi hem de O(1) erişim sağlayan `allNodes` dizisini doldurur, boş node setini güncel tutar ve board arka planını ekran boyutuna göre yeniden ölçekler.
- **Mantıksal model:** `BlockManager` içindeki `BoardModel` tek boyutlu `Cell[]` dizisiyle renk, ikon tier'i ve doluluk bilgisini tutar. Görsel node'lar (GameObject) ile bu model sürekli senkronize edilir; gravity, shuffle ve deadlock kontrolleri BoardModel üzerinden çalışır.
- **Static hedefler:** `StaticBlock` örnekleri `GameModeConfig.StaticTargetSpawns` maske bilgisine göre spawn edilir, ilgili hücre indeksleri kilitlenir ve `ObjectiveController` hedef durumunu takip edebilmesi için `StaticTargetProgressChanged` event'i tetiklenir.
- **Powerup etkileşimleri:** `LevelCanvasManager` UI üzerinden güçlendirmeleri tetikler, `GameManager` state bir süre `Pause`'a alınır ve `BlockManager` shuffle/powerup animasyonları bittiğinde `ForceWaitingAfterShuffle` ile kontrol tekrar oyuncuya bırakılır.

## Block inheritance yapısı
- **`Block` (abstract):** Ortak alanları (`node`, `blockType`, ikon referansları) ve click-handling'i barındırır. `FloodFill`, ikon tier güncelleme ve DOTween tabanlı ölçek animasyonları gibi yardımcıları sağlar.
- **`RegularBlock`:** Oyuncunun etkileştiği temel sınıf; eşleşme kontrolü, tier bazlı ikon seçimi ve BFS tabanlı grup toplama (`GatherSearchResults`) implementasyonunu içerir.
- **`SpecialBlock`:** Satır/sütun temizleyici, renk temizleyici ve 2x2 bomba gibi varyantların üst sınıfıdır. Katılım opsiyonel (`participateInGroups`) tutulur, her arketip kendi `Gather*Results` metoduyla etki alanını üretir.
- **Uzantılar:** `RowClearBlock`, `ColumnClearBlock`, `BombBlock`, `ColorClearBlock` ve `StaticBlock` gibi sınıflar `SpecialBlock`'tan türeyip tek davranışa odaklanır. `StaticBlock` patlatılamaz hedeflerdir ve `Block.BlockArchetype.Static` olarak işaretlenir.

## Group detection algoritması
- **BFS + buffer reuse:** `BlockManager.GatherGroupIndices` ( `Assets/Scripts/BlockManager.cs` ) BoardModel üzerinde BFS çalıştırır, `BlockSearchData` ile node grid ve 1D indeksleri birlikte taşır. `bfsQueue`, `groupIndicesBuffer`, `visitedStamps` gibi diziler board boyutu kadar bir kez allocate edilir ve her flood-fill çağrısında sıfırlanmak yerine damgalama (`visitStamp`) ile kullanılır.
- **Deterministik Flood-Fill:** `RegularBlock.GatherSearchResults` renk + blockType uyuşan komşuları (4-yönlü) ziyaret eder; `SpecialBlock` varyantları aynı buffer'ı kullanarak satır/sütun/renk/bomb seçimleri üretir. Böylece ikon tier güncellemeleri ve deadlock taramaları tek veri kaynağından beslenir.
- **Dirty alan yönetimi:** Blast, düşüş veya spawn sonrası ilgili hücre indeksleri `MarkDirtyCell` ile işaretlenir; ikon tier hesaplamaları yalnızca kirli hücrelerde çalışır, tam board refresh gerektiğinde `RequireFullBoardRefresh` tetiklenir.

## ScriptableObject kullanımı
- **`BoardSettings`:** Boyut, renk sayısı, eşik değerleri ve block prefab/effect eşleşmelerini tutar. `IsValid` ile brief kısıtları enforce edilir, `ApplyDimensions/ApplyThresholds` metodları runtime'da BoardRegeneratorUI tarafından kullanılır.
- **`GameModeConfig`:** Her mod için board ayarını, süre/hamle limitlerini, powerup cooldown'larını, special block eşiklerini ve statik hedef maske/prefab listelerini tutar. `Resources/Level Configurations/...` altındaki asset'ler `GameManager` tarafından otomatik yüklenir.
- **`SettingsService` + `PlayerSettings`:** Kullanıcı tercihlerini (müzik, SFX, titreşim) `PlayerPrefs` üstünden erişilebilir hale getirir, UI toggle'ları `LevelCanvasManager` üzerinden bu servislere bağlanır.

## Editor tooling
- **BoardSettings guard:** `BoardSettingsPlaymodeValidator` play mode'a girerken tüm BoardSettings asset'lerini doğrular; bir tanesi bile limitleri ihlal ederse otomatik olarak Play Mode iptal edilir ve geliştirici uyarılır.
- **GameModeConfig editörü:** `GameModeConfigEditor` özel inspector ile statik hedef maskelerini görsel olarak düzenleme (grid toggle) imkânı verir, tek tuşla alanı doldurma/temizleme sağlar.
- **BoardRegeneratorUI:** Case sahnesinde tasarımcıların satır, sütun ve A/B/C eşiklerini canlı olarak güncelleyip board'u tekrar üretmesini sağlar; kuralları ihlal eden girişlerde uyarı label'ını DOTween ile pulse'lar.
- **Profiler marker ağı:** `BlockManager` (spawn, falling, shuffle), `ObjectPool`, vb. yoğun fonksiyonlar `Unity.Profiling.ProfilerMarker` ile işaretlendiğinden Editor Profiler timeline'ında hangi aşamanın tükettiği hemen okunabilir.
