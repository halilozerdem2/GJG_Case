# GameModes

## Mode 1: Case Only
- `GameManager`'ın Case modu (`GameMode.Case`) tasarımcıların board parametrelerini canlı tweak edebileceği sahne akışıdır.
- `Assets/Resources/Level Configurations/Case Scene Configurations/GameModeConfig.asset` board boyutu, renk seti ve ikon eşiklerini tanımlar; move/time limitleri devre dışıdır.
- Case sahnesinde `BoardRegeneratorUI` slider'ları doğrudan bu asset'e yazdığı için yeni deneyler için sahneyi yeniden yüklemeye gerek yoktur.
- Powerup butonları gizlidir, Objective HUD yalnızca debug amaçlıdır.

## Mode 2: Case + Enhancements
- `GameMode.Game` olarak referans verilen moddur; aynı sahnede Case board'unu korurken move/time limitleri, powerup cooldown'ları ve hedef bloklar aktiftir.
- Varsayılan konfig `Assets/Resources/Level Configurations/Objects/GameModeConfig.asset` dosyasında tutulur. Tasarımcılar buradan powerup cooldown girişlerini ve special block eşiklerini düzenleyebilir.
- Ana menüde Game seçildiğinde oyuncu Case sahnesine değil Game sahnesine gider; `ObjectiveController` moves/time sayaçlarını gösterir, `LevelCanvasManager` tüm powerup butonlarını açar.

## Mode 3: Wonder Blast Mode
- Wonder Blast, Game modunun üretim varyantıdır; ana menüde Game seçip zorluk panelinden Easy/Medium/Hard tercih edildiğinde `GameMode.Easy/Medium/Hard` enum değerleri bu modu temsil eder.
- Her zorluk için ayrı `GameModeConfig` vardır (`Assets/Resources/Level Configurations/Easy Config/...`, `Medium Config`, `Hard Config`). Renk sayısı, static hedef maskeleri, special block eşikleri ve limitler bu asset'lerde değişir.
- Wonder Blast modunda tüm powerup'lar (Shuffle, PowerShuffle, DestroyAll, DestroySpecific) aktif, hedef toplama animasyonları ve Win/Lose panelleri tamamlanmış durumdadır. Case modunda olmayan UI polish bu modda görülebilir.
- Yeni Wonder Blast varyantı eklemek için `GameMode` enum'una değer ekleyip `GameManager.ModeResourcePaths` sözlüğüne yeni resource yolunu tanımlamak yeterlidir.

## Modlar arası farklar
| Özellik | Mode 1: Case Only | Mode 2: Case + Enhancements | Mode 3: Wonder Blast |
| --- | --- | --- | --- |
| Board kaynağı | Case Scene GameModeConfig | Objects/GameModeConfig | Easy/Medium/Hard GameModeConfig'leri |
| Move/Time limit | Kapalı | Opsiyonel (`limits` alanı) | Easy/Medium/Hard için farklı değerler |
| Powerup UI | Gizli | Açık ama üretim ayarları | Tamamı açık + cooldown göstergeleri |
| Statik hedefler | Manuel test için isteğe bağlı | Config'den okunur | Zorluk bazlı maske + Objective HUD |
| Kullanım amacı | Level tasarım/prototipleme | Case gereksinimlerini + ek özellikleri gösterme | Oyuncu-facing demo + difficulty skalası |
