# Deadlock & Shuffle

## Deadlock tespiti nasıl yapılıyor
- `BlockManager.ModelHasValidMove()` BoardModel üstünden her hücreyi dolaşır, yalnızca `CanParticipateInGroup == true` blokları dikkate alır ve sağ/üst komşuda aynı renk varsa erken çıkış yapar. BoardModel düz bir `Cell[]` olduğu için tarama `O(M*N)`'dir.
- Blast veya drop akışından sonra `isValidMoveExist` bayrağı güncellenir; WaitingInput durumuna geçmeden önce en az bir grup olduğundan emin olunur.
- Statik hedefler `shuffleLockedFlags` üzerinden kilitlendiği için deadlock kontrolü onlara çarptığında `CanParticipateInGroup` false döner ve yanlış pozitif üretilmez.

## Blind shuffle neden yok
- Shuffle tetiklendiğinde `TryShuffleBoard()` önce tüm blokları renk bucket'larına toplar (`shuffleColorBuckets`). Her bucket'ta en az iki hücre varsa `CommitPair` ile board'un belirli koordinatlarına (ör. (0,0) ve (1,0)) kilitli eşleşmeler yerleştirilir.
- Statik blokların bulunduğu hücreler shuffle'a dahil edilmez (`LockStaticNodes`). Böylece shuffle sırasında hedef blok kayması veya prefab kaybı olmaz.
- Bucket'lar tüketildikten sonra kalan bloklar Fisher-Yates benzeri karıştırmayla `shuffleNodesBuffer` içinde yeniden dağıtılır; `AnimateShuffleBlock` her taşımaya görsel feedback verir.
- Eğer hiçbir renk çifti yoksa blind shuffle yapmak yerine `RegenerateBoardWithGuaranteedPairs` çalışır; board sıfırlanır, iki hücre aynı renkle doldurulur ve kalan hücreler konfigürasyondaki prefablardan rastgele seçilir.

## Shuffle sonrası solvable garanti mekanizması
- `CommitPair` ile kilitlenen hücreler shuffle esnasında tekrar karıştırılmaz; thus shuffle tamamlandığında en az bir çift garanti edilir.
- Shuffle sonrasında `ModelHasValidMove()` tekrar çağrılır; false ise `TryGuaranteeMove()` devreye girer ve aynı renkten iki blok bularak komşu hücrelere taşıyıp instant grup üretir.
- Eğer bu da başarısız olursa board komple yeniden üretilir (bkz. `RegenerateBoardWithGuaranteedPairs`) ve `RequireFullBoardRefresh()` tetiklenerek ikon tier cache'leri sıfırlanır.
- `shuffleCompletionCallback` yalnızca `isValidMoveExist == true` olduğunda success bildirir; aksi durumda GameManager tekrar Deadlock durumuna geçer ve kullanıcıya bilgi verilir.

## Worst-case senaryo
- Board'un tamamı statik hedefler veya tekil renklerden oluşuyorsa shuffle'ın swap üretmesi fiziksel olarak imkânsızdır. Bu durumda fallback regenerasyon devreye girer fakat statik blok hücreleri kilitli kaldığı için board doluluğu yetersiz kalabilir; metrik takibi için `StaticTargetCount` log'ları takip edilmelidir.
- Minimum board boyutu (2x2) ve renk sayısı 1 olduğunda shuffle hiçbir hareket alanı bulamaz; bu kombinasyon `BoardSettings.IsValid` ile engellense de manuel asset düzenlemeleri için validator uyarıları dikkate alınmalıdır.
- Eğer blok prefablardan biri eksik veya pool initialize edilmemişse shuffle sırasında `SpawnBlockOfType` null döndürebilir. Bu durumda fallback kısır döngüye girmesin diye Console uyarıları takip edilmeli ve `GameModeConfig.SpecialBlockPrefabs` ile `BoardSettings.BlockPrefabs` senkron tutulmalıdır.
