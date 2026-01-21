# GJG Case Study

## 1. Overview
Unity 2022 LTS ile geliştirilen bu match-style blok oyunu prototipi; deterministik grid lojikleri, obje havuzu, çoklu oyun modu ve hedef blok takibi gibi üretim odaklı çözümleri içerir. Proje ana menü, case sahneleri ve oyun sahnesi üzerinden BoardSettings ile şekillenen farklı konfigürasyonları gösterir.

## 2. Case Requirements Coverage
Stüdyo brifi aşağıdaki gereksinimlere odaklanıyor; her maddeye karşılık gelen uygulama detayı listelendi:

- **Collapse/Blast Mekaniği:** Oyuncu 2+ aynı renk bloktan oluşan gruplara dokunarak blast yapar; boş kalan hücrelere üstteki bloklar düşer, eksikler üstten spawn edilir. `BlockManager` flood-fill ile grup tespiti yapar ve yer çekimini yönetir.
- **Boyut ve Renk Esneği:** Board 2-10 satır (M), 2-10 sütun (N) ve 1-6 renk (K) arasında konfigüre edilir. `BoardSettings` editörden bu değerleri alır ve grid node’larını ölçekler.
- **İkon Seviyeleri:** Grup büyüklüğüne göre A/B/C eşiklerinin (örnek: A=4, B=7, C=9) üzerine çıkıldığında bloklar farklı ikon setleri gösterir. Prefab’lerde dört ikon (default + 3 tier) tanımlı; `Block.ApplyGroupIcon` threshold’ları `BoardSettings`’ten okur.
- **Deadlock Önleme:** Geçerli hamle yoksa sistem deterministik olarak board’u shuffle’lar; blind shuffle yerine uygun swap kombinasyonları denenip garanti hamle oluşturulur.
- **Performans ve Hafıza:** ObjectPool kullanımı, paylaşılan buffer’lar ve GC-free veri yapıları ile CPU/GPU/memory hedefleri gözetildi. Static hedefler bile pooling’e entegre edildi.
- **Ekstra Ekran Kart/CPU Yükü:** `TargetCollectionAnimator` gibi efektler DOTween/Coroutine ile hafifletildi; overlay canvas dönüşümleri optimize edildi.
- **Örnek Konfigürasyonlar:** README’deki M=10/N=12/K=6/A=4/B=7/C=9 ve M=5/N=8/K=4/A=4/B=6/C=8 senaryoları BoardSettings üzerinden kolayca tanımlanabilir; ikon seviyeleri otomatik uygulanır.

Ek olarak stüdyo brief’inde istenmeyen eksikleri kapatmak için şunlar geliştirildi:
- **Hedef Blok Sistemi:** Game modlarında statik hedef bloklar spawn edilip ObjectiveController tarafından takip edilir; TargetCollectionAnimator hedef toplama animasyonu sağlar.
- **Powerup Sistemi:** Case/Game modlarında Shuffle, PowerShuffle, DestroyAll, DestroySpecific güçlendirmeleri mevcut ve konfigüre edilebilir cooldown süreleriyle çalışır.
- **Win/Lose UX:** WinLosePanelController sahne bazlı panelleri yönetir; retry/main menu butonları GameManager üzerinden sahneleri tekrar yükler.

## 3. Game Modes
- **Case:** Tahta deneyleri ve BoardRegeneratorUI ile canlı parametre değişimi (Main Menu + Case sahneleri).
- **Game (Easy/Medium/Hard):** `Assets/Resources/Level Configurations/...` altındaki modlara özgü board/powerup/target konfigleri. Ana menüde dropdown + zorluk paneliyle seçilir.
- **Other:** İlgili ScriptableObject’lere yenileri eklenerek GameManager lookup’ına bağlanabilir.

## 4. How to Run
1. Unity 2022.3 LTS veya üstü ile projeyi açın.
2. `Assets/Scenes/MainMenu.unity` sahnesini çalıştırın.
3. Dropdown’dan Game/Case modunu seçin; Game için zorluk panelinden Easy/Medium/Hard seçin.
4. Seviye sırasında Retry/Main Menu butonları WinLosePanelController üzerinden sahneleri yeniden yükler.
5. Geliştirme sırasında FPS göstergesi için `FpsCounter` scriptini bir TMP_Text ile sahneye ekleyebilirsiniz.

## 5. Board & Mechanics
- **GridManager:** BoardSettings’ten satır/sütun alır, node prefab’larını üretir ve board görselini ölçekler.
- **BlockManager:** Flood-fill tabanlı grup tespiti, özel blok üretimi, statik hedef spawn’ları ve obje havuzunu yönetir.
- **TargetCollectionAnimator:** Statik blok yok edildiğinde ikonları HUD’daki counter’lara taşır (overlay canvas’ta doğru koordinat kullanımı için güncellendi).
- **Powerup Akışı:** LevelCanvasManager güçlendirmelerin cooldown’larını GameModeConfig’ten çeker; seviye başında tüm powerup’lar bekleme ile başlar.
- **Blok Soyutlaması:** `Block` abstract sınıfı tüm blokların temel davranışlarını (grid node referansı, flood-fill, ikon yönetimi, `CanParticipateInGroup` gibi) tanımlar. `RegularBlock` bu sınıftan türeyip tier ikonlarını ve oyuncu etkileşimli flood-fill mantığını uygular; `SpecialBlock` ise tekrar kullanılabilir row/column/bomb/color clean yardımcılarını içerir ve `RowClear`, `ColumnClear`, `BombBlock`, `ColorClear` gibi alt sınıflar bu fonksiyonları spesifik arketiplere göre kullanır. `StaticBlock` ise `Block`tan türeyip blast edilemeyen hedef blok davranışını sağlar.

## 6. Architecture Overview
- **GameManager:** Singleton durum makinesi (Generate → Spawn → WaitingInput → Falling/Deadlock/Win/Lose). Mod konfigürasyonlarını Resources’tan yükler, board/powerup limitlerini uygulayıp retry/main-menu çağrılarını yönetir.
- **Scriptable Objects:** `BoardSettings` boyutlandırma, `GameModeConfig` limitler + special/target spawn, `Level Configurations/...` mod bazlı preset’ler.
- **UI Katmanı:** MainMenuCanvasController (mod ve zorluk seçimleri), LevelCanvasManager (powerup UI), ObjectiveController (moves/time/targets HUD), WinLosePanelController (paneller + sahne geçişleri), FpsCounter.
- **Pooling & Effects:** ObjectPool blok/patlama prefab’larını yönetir; StaticBlock, SpecialBlock alt sınıfları ile BoardModel senkron tutulur.

## 7. Performance & Optimization
- **Obje Havuzu:** `ObjectPool`, tüm blok prefab’larını ve patlama efektlerini önceden instantiatelayıp tekrar kullanır. Seviye başında board hücre sayısı kadar blok hazırlanır; patlama sonrası blok komponenti `ReleaseBlock` ile havuza döner, böylece `Instantiate/Destroy` kaynaklı GC tahsisi engellenir.
- **BoardModel & Cache Kullanımı:** Grid, `BoardModel` içinde 1D int dizisiyle tutulur (index = x + y * columns). `BlockManager` ve `GridManager`, hash/dictionary yerine bu düz diziyi kullanır; node arayüzleri `GetNodeFromIndex`, `gridManager.NodeGrid[x,y]` olmadan direkt arr index üzerinden hızlı erişim sağlar. Flood-fill ve shuffle algoritmaları, `bfsQueue`, `groupIndicesBuffer`, `visitedStamps` gibi yeniden kullanılan int dizilerini işaretleyerek GC-free çalışır.
- **HashSet vs Diziler:** Rastgele erişim gereken yerlerde (boş node seti gibi) `HashSet<Node>` kullanılsa da sıcak yol fonksiyonları (flood-fill, özel blok üretimi, shuffle tutucu listeleri) dizi tabanlı buffer’larla yönetilir. Örneğin `shuffleColorBuckets` 256 listelik sabit dizidir; her renk için tekrar tekrar HashSet oluşturulmuyor.
- **Blast Akışı:** Oyuncu blok patlatmaya çalıştığında `BlockManager.TryHandleBlockSelection` çağrılır. `GetMatchingNeighbours` flood-fill’i `BoardModel`’deki `groupIndicesBuffer` dizisine yazar, eşik kontrolleri `BoardSettings.ThresholdA/B/C` ile yapılıp ikon seviyeleri sadece etkilenen bloklarda güncellenir. Blast sonrası `RemoveSpecialBlocks` ve `CollectAdjacentStaticBlocks` gibi metodlar her iterasyonda tampon dizileri/Queue’ları boşaltıp tekrar kullanır.
- **Deadlock Çözümü:** `ModelHasValidMove` her hamle sonrası `boardModel` üzerinden gizli cached komşuluk listeleriyle çalışır. Deadlock varsa `ResolveDeadlock` deterministik olarak board’dan olası swap kombinasyonları üretir (renk başına bucket listeleri, `usedColorIds` vs) ve en az bir geçerli grup oluşana kadar tek seferde swap yapar. “Kör shuffle” yerine analiz edilen swap ile CPU israfı önlenir.
- **Time/Move Sayaçları:** Limitler `Time.unscaledDeltaTime` ile takip edildiği için pause/state değişimlerinde clock drift oluşmaz. Coroutineler (limit timer, animasyonlar) mümkün olduğunca kısa tutulup `StopCoroutine` ile temizlenir.
- **TargetCollectionAnimator:** Dünya→UI dönüşümleri, overlay canvas’ta bile Camera referansı doğru seçilerek yapılır. `indicatorPool` queue’su ile animasyon objeleri tekrar kullanılır; DOTween yerine sade `Coroutine` ve `AnimationCurve` ile GPU/CPU maliyeti düşük tutulur.
- **Cache Friendly Veri Yapıları:** `GridManager` node referanslarını hem 2D `Node[,]` hem de 1D `allNodes` dizisinde tutar; blast ve shuffle gibi sıcak yollar `allNodes[i]` ile branchsiz çalışır. Special block spawn lookup’larında `Dictionary<Block.BlockArchetype, Queue<ParticleSystem>>` gibi küçük dictionary’ler sadece setup aşamasında doldurulur.
- **Profiling & Guards:** Kod boyunca `ProfilerMarker` kullanıldı (`BlockManager.ResolveFalling`, `BlockManager.SpawnBlocks` vs) ve kritik fonksiyonlar guard clause ile null kontrollerini minimal maliyetle yapıyor.

## 8. Testing Scenarios
- Easy/Medium/Hard modlarının hepsinde hedef toplama + win akışını kontrol edin.
- Powerup butonlarının seviye başında cooldown’da başladığını, süre bittiğinde aktifleştiklerini doğrulayın.
- Lose panelinden Retry ve Main Menu butonları ile sahne tekrar yüklenip board verilerinin sıfırlandığını test edin.
- BoardRegeneratorUI ile satır/sütun/threshold değiştirip grid’in yeniden oluşturulmasını gözlemleyin.
- TargetCollectionAnimator’ın farklı kamera modlarında doğru pozisyonlama yaptığını doğrulamak için overlay ve world-space canvas senaryolarını deneyin.

## 9. Known Limitations
- Sahneler arası yüklemelerde bazı ScriptableObject değişikliklerinin uygulanması için Unity’nin asset serialization’ını el ile tetiklemek gerekebilir.
- Mobile target için UI optimizasyonu ve dokunmatik giriş sistemi henüz eklenmedi.
- Game Scene’deki test düzenleri yalnızca referans; gerçek oyun akışı için daha fazla seviyelendirme ve VFX/SFX tuning’i planlandı.
- Multiplayer / online senaryolar düşünülmedi; tüm logic tek oyuncu varsayımıyla tasarlandı.

Ek geliştirme notları ve yapılacaklar listesi için `To do list.md` dosyasını inceleyebilirsiniz.
