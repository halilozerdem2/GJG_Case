# GJG Case Study

Match-style blok oyunu prototipi için Unity 2022 tabanlı bir çalışma alanı. Grid üzerinde dinamik blok ikonları, deterministik deadlock çözümü, obje havuzu ve modlar arası davranış ayrımı gibi performans odaklı çözümleri örnekler.

## İçindekiler
- [Genel Bakış](#genel-bakış)
- [Özellikler](#özellikler)
- [Proje Dizini](#proje-dizini)
- [Çalıştırma](#çalıştırma)
- [Oyun Akışı](#oyun-akışı)
- [Genişletilebilirlik Notları](#genişletilebilirlik-notları)

## Genel Bakış
- **Motor:** Unity 2022 LTS (URP + 2D Renderer).
- **Platform:** Masaüstü ve mobil prototip ihtiyaçlarını hedefleyen deterministik grid lojikleri.
- **Çekirdek Sistemler:** `GameManager` (durum makinesi + mod yönetimi), `GridManager` (node üretimi ve boyutlandırma), `BlockManager` (blast, yer çekimi, shuffle, efekt tetikleme), `BoardModel` (1D dizi tabanlı veri modeli).

## Özellikler
- **Game & Case Modları:** Ana menüdeki dropdown/GameManager API’si ile belirlenir. Game modu hedef ve sınırlara hazır, Case modu performans/sandbox akışını korur.
- **Dinamik Grid:** `BoardSettings` ScriptableObject üzerinden satır/sütun, renk sayısı ve threshold değerleri ayarlanır. Grid yeniden oluşturulduğunda node pozisyonları otomatik ölçeklenir.
- **Obje Havuzu:** `ObjectPool` blok ve patlama efektlerini yeniden kullanarak `Instantiate`/`Destroy` maliyetini azaltır.
- **Deadlock Tespiti ve Shuffle:** Flood-fill temelli grup analizi; geçersiz durumlarda deterministik shuffle + garanti edilen çift üretimi.
- **Performans Hijyeni:** HashSet/Dictionary tahsislerini minimuma indirmek için diziler, paylaşılan buffer’lar ve tek seferlik listeler kullanılır.
- **Ayarlar & Ses:** `SettingsService` kalıcı müzik/SFX/titreşim kontrolleri sağlar; `AudioManager` sahneye özel müzik + efektleri yönetir.

## Proje Dizini
- `Assets/Scripts` – gameplay kodu (`BlockManager`, `GridManager`, `GameManager`, UI controller’ları vb.).
- `Assets/Scriptable Objects` – `BoardSettings` gibi konfigürasyon varlıkları.
- `Assets/Prefabs`, `Materials`, `Particle Effects` – görsel içerik.
- `Assets/Scenes` – `MainMenu`, `Case0/1`, `Game Scene` gibi test sahneleri.
- `To do list.md` – fazlar halinde planlanan refaktör ve özellikler.

## Çalıştırma
1. Unity Hub üzerinden projeyi açın (önerilen sürüm: 2022.3 LTS veya üstü).
2. `MainMenu.unity` sahnesini çalıştırın.
3. Ana menüde `Game Mode` dropdown’ından mod seçip ilgili butonla sahneye geçin.
4. Editor’de BoardSettings’i güncellemek için `Scriptable Objects/BoardSettings.asset` dosyasını düzenleyin; değişiklikler grid yeniden oluşturulduğunda uygulanır.

## Oyun Akışı
1. `GameManager` açılışta aktivasyon sahnesine göre `GenerateLevel` durumuna geçer.
2. `GridManager` node’ları oluşturur, `BlockManager` board modelini ve havuzları hazırlar.
3. `BlockManager.SpawnBlocks` ikili gruplar oluşturarak valid hamle garantiler.
4. Oyuncu blok seçtiğinde flood-fill ile grup bulunur; yeter büyükse blast edilir, hareketler/animasyonlar tetiklenir, model güncellenir.
5. Deadlock algılanırsa shuffle devreye girer ve board deterministik olarak yeniden düzenlenir.
6. Mod durumuna göre ileride special block üretimi, hedef bloklar, süre/hamle limitleri gibi davranışlar eklenmek üzere planlanmıştır.

## Genişletilebilirlik Notları
- README’deki Phase 3 planı özel bloklar, hedef blok sistemleri ve kısıtlamalara dair yol haritasını içerir.
- Yeni blok tipleri eklerken `Block` sınıfını soyutlayıp Regular/Special alt sınıflarıyla flood-fill ve pool entegrasyonunu koruma hedefleniyor.
- Localization paketi kurularak Türkçe metin desteği eklemek için gerekli hazırlıklar planlandı.

Daha fazla ayrıntı ve açık iş listesi için `To do list.md` dosyasını takip edin.
