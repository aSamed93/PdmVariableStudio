# Phase 0 — Doğrulanacak PDM API davranışları

Bu belge bir **kapıdır**. Aşağıdaki maddelerin tamamı gerçek bir **test vault'unda**
doğrulanmadan eklenti üretim vault'una kurulmamalıdır.

> **Durum (2026-09-28):** Veri bütünlüğüne dokunan maddelerin tamamı doğrulandı. Açık
> kalanlar yalnızca performans ve tazeleme: 4 (okuma yolu), 7'nin check-in kapalı senaryosu
> ve 9'un 100+ dosya ölçümü. Kodda `PHASE 0'DA DOĞRULANACAK` işareti artık tek yerde:
> `App/Pdm/PdmVariableReader.cs` (madde 4).

Gerekçe: `help.solidworks.com` bu makineden HTTP 403 döndürüyor ve yerel `API_GB.chm`
plan aşamasında açılamadı. Kodda kullanılan API'lerin **imzaları** interop derlemesi üzerinde
reflection ile doğrulandı, ancak bazı **davranışlar** doğrulanamadı. Bu maddeler kodda
`PHASE 0'DA DOĞRULANACAK` yorumuyla işaretli; her biri tek bir yerde toplandı ki doğrulama
sonucuna göre değişiklik tek satırda yapılabilsin.

Sonuçları bu belgeye yazın. Bir madde `❌` çıkarsa, ilgili kod yorumundaki alternatif uygulanır.

---

## Durum tablosu

| # | Madde | Kod konumu | Sonuç |
|---|---|---|---|
| 1 | Arka plan STA thread'inde `LoginAuto` | `App/Threading/PdmWorkQueue.cs` | ✅ **doğrulandı** |
| 2 | Klasör bağlam menüsünde `mlObjectID1` | `AddIn/VariableStudioAddIn.cs` | ✅ **doğrulandı** |
| 3 | `"@"` vs boş dize konfigürasyon semantiği | `Core/Domain/ConfigurationKey.cs` | ✅ **doğrulandı** |
| 4 | `GetVarFromDb` ile `GetVar` farkı | `App/Pdm/PdmVariableReader.cs` | ⬜ |
| 5 | `SetVar` 4. parametresi (`false` doğru çalışıyor) | `App/Pdm/PdmVariableWriter.cs` | ✅ pratikte doğrulandı |
| 6 | `HasRightsEx` hak kapsamları | `App/Pdm/PdmVariableReader.cs` | ✅ **doğrulandı** |
| 7 | `Flush()` sonrası Explorer kart tazeleme | — | 🟡 check-in ile doğrulandı; check-in kapalıyken denenmedi |
| 8 | Komutun menülerde görünmesi | `AddIn/VariableStudioAddIn.cs` | ✅ **doğrulandı** |
| 9 | Ölçüm: 10 / 100 / 1.000 dosya süresi | — | 🟡 14 dosya ölçüldü; 100+ bekliyor |

---

## 1. Arka plan STA thread'inde `LoginAuto` — ✅ DOĞRULANDI (2026-08-25)

**Neden önemliydi:** Mimarinin temeli. Çalışmasaydı tüm PDM erişimi çağıran thread'e
taşınmalı ve arayüz donmasın diye parçalanmalıydı.

**Sonuç:** Çalışıyor. `AddinTest` vault'una karşı:

```
[BILGI] [  1] Uygulama başlatıldı. vault='AddinTest', klasör=1, parent=0
[BILGI] [  3] PDM çalışma kuyruğu hazır (vault 'AddinTest', thread 3).
[BILGI] [  3] Vault değişkenleri okundu: 112 adet.
```

Thread 1 (giriş) ile thread 3 (PDM kuyruğu) farklı — yani `new EdmVault5Class()` +
`LoginAuto` adanmış bir arka plan STA thread'inde sorunsuz çalışıyor ve o thread üzerinden
`IEdmVariableMgr5` ile 112 değişken okunabiliyor. Aynı desen ayrı süreçte de geçerli
(kardeş proje `PDMetry.DemoSeed` de konsol uygulamasından vault'a yazıyor).

**Tekrar doğrulamak için:** eklentiden ya da doğrudan uygulamayı çalıştırıp
`%LOCALAPPDATA%\PdmVariableStudio\studio.log` içinde şu satırı arayın:

```
[BILGI] PDM çalışma kuyruğu hazır (vault '<ad>', thread <n>).
```

---

## 2. Klasör bağlam menüsünde `EdmCmdData.mlObjectID1` — ✅ DOĞRULANDI (2026-08-25)

**Neden önemliydi:** Yanlış klasörün dışa aktarılması, kullanıcının saatlerce yanlış veriyle
çalışması demek.

**Sonuç:** Klasör bağlam menüsünde `mlObjectID1` doğru klasör kimliğini taşıyor.
`TEKYAZ` vault'unda:

```
[BILGI] [eklenti] Komut tetiklendi (id 3001, veri sayısı: 1).
[BILGI] [eklenti] Seçili klasör: SPECIAL (#3).
```

Araçlar menüsünden çağrıldığında ise komut verisi boş geliyor ve kod bunu doğru ele alıyor:

```
[BILGI] [eklenti] Komut tetiklendi (id 3001, veri sayısı: 0).
[BILGI] [eklenti] Komut verisi boş; klasör uygulamada seçilecek.
```

**Kalan doğrulama:** vault kökü ve derin alt klasörlerde ayrıca deneyin; günlükteki adın sağ
tıkladığınız klasör olduğunu görün.

**Mevcut savunma:** Kod gelen numarayı kullanmadan önce gerçekten bir klasör nesnesine
çözüyor; çözülemezse kullanıcıdan seçim istiyor. Yani yanlış bir DOSYA numarası gelirse
sessizce yanlış klasör dışa aktarılmaz.

---

## 3. `"@"` vs boş dize — konfigürasyon semantiği — ✅ DOĞRULANDI (2026-08-25)

**Neden önemliydi:** Yanlış konfigürasyona yazmak sessiz veri bozulmasıdır.

**Sonuç:** Mevcut model doğru. `TEKYAZ\Classified` klasöründe, konfigürasyonlu `.SLDPRT`,
`.SLDASM` ve `.SLDDRW` dosyaları üzerinde gerçek bir `Uygula` çalıştırıldı; hem `@` hem
adlandırılmış konfigürasyon satırları aynı işlemde yazıldı ve sonuç sorunsuz.

**Anlamı:**

| Konfigürasyon | PDM'ye gönderilen | SOLIDWORKS karşılığı |
|---|---|---|
| Dosya düzeyi, SOLIDWORKS dosyası | `"@"` | **Custom** sekmesi |
| Dosya düzeyi, generic dosya | boş dize | — |
| Adlandırılmış (`Varsayılan`, `Uzun`, …) | konfigürasyon adı | **Configuration Specific** sekmesi |

`@` ile adlandırılmış konfigürasyonlar PDM veri kartında **ayrı ayrı** tutulur — biri
diğerini etkilemez. Ürünün "her konfigürasyon ayrı satır" tasarımı bu yüzden doğru:
kullanıcı `@` satırını değiştirdiğinde adlandırılmış konfigürasyonlara dokunulmaz.

**Gösterim:** Dosya düzeyi arayüzde ve çalışma kitabında `@` olarak yazılır, tire ya da boş
hücre olarak değil. İlk sürümde tire kullanılıyordu ve kullanıcıya "burada bir değer yok"
izlenimi veriyordu; oysa `@` gerçek ve ayrı bir kart alanıdır.

---

## 4. `GetVarFromDb` ile `GetVar` farkı

**Neden önemli:** `GetVar` yerel önbellekten okumaya çalışıp ağdan dosya çekebilir; 1.000
dosyalık bir taramayı dakikalara çıkarır.

**Nasıl doğrulanır:**
1. Yerel kopyası **olmayan** (Explorer'da gri görünen) bir klasör seçin.
2. Dışa aktarın, süreyi ölçün.
3. `PdmVariableReader.PreferDatabaseRead = false` yapıp yeniden derleyin, tekrar ölçün.
4. İki çıktının **değerlerinin aynı** olduğunu ve `PreferDatabaseRead = true`'nun daha hızlı
   olduğunu doğrulayın.
5. Yerel önbelleğin dolup dolmadığına bakın: `GetVarFromDb` dosya çekmemeli.

**❌ ise:** `PreferDatabaseRead` varsayılanı `false` yapılır.

---

## 5. `SetVar` 4. parametresinin anlamı

**İmza (reflection ile doğrulandı):**
```
void SetVar(string bsVariableName, string bsConfiguration, ref object value, bool <?>)
```

**Neden önemli:** Bu parametre "tüm konfigürasyonlara uygula" anlamına geliyorsa, `true`
göndermek kullanıcının dokunmadığı konfigürasyonları da ezerdi. Kod **her zaman `false`**
gönderiyor.

**Durum:** 3. maddedeki gerçek uygulama başarılı geçti; `false` göndermek beklendiği gibi
çalışıyor ve konfigürasyonlar birbirini etkilemiyor. Parametrenin tam adı ve `true`
davranışı hâlâ bilinmiyor — ama `true` göndermeye ihtiyacımız yok, çünkü her konfigürasyon
ayrı bir satır olarak yönetiliyor.

**Bu parametreyi `true` yapmayın.** Kullanıcının dokunmadığı konfigürasyonların sessizce
ezilmesi, ürünün temel vaadini bozar.

**Ek uyarı:** Toplulukta bildirilen bir sorun var — `IEdmBatchUpdate2.SetVar` ile `"@"`
kullanıldığında, kullanıcının kendi çektiği dosyalarda değer yazılmıyor. Biz batch update
kullanmıyoruz; enumerator yolunda böyle bir belirti görülmedi.

---

## 6. `HasRightsEx` — ✅ DOĞRULANDI (2026-08-25, ölçümle)

**İmza (interop metadata'sından parametre ADLARIYLA okundu):**

```
bool IEdmFolder5.HasRightsEx(int lRights, int lFileID)
```

İkinci parametre **DOSYA** kimliğidir, klasör değil. **Ama her hak dosya kapsamında anlamlı
değil** — asıl bulgu bu.

**Gerçek vault ölçümü** (TEKYAZ, Admin kullanıcı, üç farklı klasör, farklı iş akışı durumları
— "Under Editing" dahil):

| Çağrı | Sonuç |
|---|---|
| `HasRightsEx(ChangeCard, 0)` | **True** |
| `HasRightsEx(ChangeCard, <gerçek dosya kimliği>)` | **her zaman False** |
| `HasRightsEx(Lock, <gerçek dosya kimliği>)` | True |
| `HasRightsEx(Read, <gerçek dosya kimliği>)` | True |

Yani `EdmRight_ChangeCard` **klasör kapsamlı** bir haktır; dosya kimliğiyle sorulduğunda
dosyanın durumu ne olursa olsun `false` döner. `EdmRight_Lock` ise dosya kapsamında anlamlı.

**Doğru kullanım — iki hak, iki farklı kapsam:**

```csharp
var canChangeCard = folder.HasRightsEx((int)EdmRightFlags.EdmRight_ChangeCard, 0);
var canLock       = folder.HasRightsEx((int)EdmRightFlags.EdmRight_Lock, file.FileId);
return canChangeCard && canLock;
```

**Yaşanan hata:** İlk sürüm klasör numarasını dosya kimliği yerine geçiriyordu, ikincisi ise
`ChangeCard`'ı dosya kimliğiyle soruyordu. İkisinde de sonuç aynı: **yönetici bir kullanıcıda
bile** önizlemedeki her satır "Yetki yok", tüm onay kutuları pasif, `Uygula` tıklanamaz.
Belirti "uygulama bozuk" gibi görünüyordu.

**Yeniden ölçmek için:**

```powershell
Add-Type -Path "C:\Program Files\SOLIDWORKS PDM\EPDM.Interop.epdm.dll"
$v = New-Object EPDM.Interop.epdm.EdmVault5Class
$v.LoginAuto("<vault>", 0)
$f = ([EPDM.Interop.epdm.IEdmVault15]$v).RootFolder.GetSubFolder("<klasör>")
"ChangeCard klasör = $($f.HasRightsEx(0x10000, 0))"
$pos = $f.GetFirstFilePosition()
while (-not $pos.IsNull) {
    $file = $f.GetNextFile($pos); if (-not $file) { break }
    "{0,-28} ChangeCard(dosya)={1} Lock(dosya)={2}" -f $file.Name, $f.HasRightsEx(0x10000,$file.ID), $f.HasRightsEx(0x2,$file.ID)
}
```

> `$pos.IsNull` kontrolü şart: liste sonunda `GetNextFile` COM hatası atıyor.

**Kalan doğrulama (isteğe bağlı):** Kart değiştirme yetkisi OLMAYAN bir kullanıcı hesabıyla
içe aktarın; hücrelerin "Yetki yok" ile işaretlendiğini — yani kontrolün doğru negatif de
verdiğini — görün.

**Hata durumu:** Yetki okunamazsa (COM hatası) `true` dönülüyor ve asıl reddi PDM veriyor.
Bilinçli: yanlış negatif (yazabilecek kullanıcıyı engellemek) yanlış pozitiften daha zararlı —
bu iki hatanın kendisi bunun kanıtı.

---

## 7. `Flush()` sonrası Explorer kart görünümü

**Neden önemli:** Değer yazıldı ama Explorer eski değeri gösteriyorsa kullanıcı işlemin
başarısız olduğunu sanar.

**Nasıl doğrulanır:** Uygulamadan sonra Explorer'da dosyanın kart sekmesine bakın.
Tazelenmiyorsa F5 / klasör değiştirip dönme ile tazelenip tazelenmediğine bakın.

**Durum (2026-09-27, 🟡 kısmen):** "İşlem sonunda check-in edilsin" **açıkken** doğrulandı:
TEKYAZ\Nemo'da 14 dosyaya 390 değer uygulandıktan sonra Explorer'da dosya seçildiğinde veri
kartı ve Bill of Materials sekmesi yeni değerleri gösterdi; geri almadan sonra eski değerlere
döndü. Check-in yeni bir sürüm ürettiği için Explorer bunu kendiliğinden alıyor.
**Açık kalan:** check-in **kapalıyken** (dosya kullanıcıda çekili kalırken) kartın tazelenip
tazelenmediği.

**❌ ise:** `IEdmVault5.RefreshFolder` ya da `EdmRefreshFlag` ile bir tazeleme çağrısı
eklenir. (Toplulukta bunların "beklendiği gibi çalışmadığı" bildirilmiş; bu yüzden MVP'ye
konmadı, önce ölçülecek.)

---

## 8. Komutun menülerde görünmesi — ✅ DOĞRULANDI (2026-08-25)

**Sonuç:** `EdmMenu_ShowInMenuBarTools` tek başına verildiğinde komut HEM Araçlar menüsünde
HEM klasör sağ tık menüsünde görünüyor. Bağlam menüsünün varsayılan olduğu böylece
doğrulandı.

**Geçmişi:** İlk sürümde komut hiç görünmedi. Günlük `GetAddInInfo`'nun sorunsuz çalıştığını,
yani eklentinin yüklendiğini ve `AddCmd`'nin hata vermediğini gösterdi — sorun beş bayrağın
birlikte verilmesiydi:

```
ContextMenuItemFolder | OnlyFolders | MustHaveSelection | OnlySingleSelection | ShowInMenuBarTools
```

`OnlyFolders`, `MustHaveSelection` ve `OnlySingleSelection` dosya listesi seçimi semantiğine
ait; klasör ağacında o anlamda bir seçim olmadığı için komutu bastırıyorlar.

**İkinci deneme de görünmedi:** `ContextMenuItem (0x400) | ContextMenuItemFolder (0x800) |
ShowInMenuBarTools`. Günlük eklentinin `explorer.exe` içine yüklendiğini ve `AddCmd`'nin hata
vermediğini gösterdi — ama komut Araçlar menüsünde bile çıkmadı. Bu iki değer başka bir
mekanizmaya ait görünüyor ve kaydı sessizce geçersiz kılıyor.

**Şimdiki yaklaşım — tek bayrak:** `EdmMenu_ShowInMenuBarTools`.

Gerekçe API'nin kendi tasarımında yazılı: `EdmMenu_NeverInContextMenu` (0x40) diye bir
bayrağın var olması, komutların bağlam menüsüne **varsayılan olarak** girdiğini gösteriyor.
Çıkarmak için bayrak gerekiyorsa girmek için gerekmiyor demektir. Ayrıca kardeş proje
`PDMetry` de yalnızca bu bayrağı kullanıyor ve bu makinede çalışıyor — yani kanıtlanmış
yapılandırma.

Süzgeçler (`OnlyFolders` vb.) **kullanılmıyor**; hangi bağlamdan gelindiği `OnCmd` içinde
çözülüyor ve klasör bulunamazsa uygulama kendi klasör seçme penceresini açıyor.

**Kontrol listesi:**

Kanıt: `studio.log` (2026-09-20 → 2026-09-27) ve 2026-09-27 ekran kayıtları.

- [ ] Klasör ağacında klasöre sağ tık → **PDM Variable Studio** görünüyor — *günlük ağaç ile
  listeyi ayırt etmiyor; ayrıca denenmeli*
- [x] Dosya listesinde bir klasöre sağ tık → görünüyor (`Seçili klasör: Nemo (#3)`; kayıtta
  görüldü)
- [x] Bir dosyaya sağ tık → görünüyor ve **dosyanın bulunduğu klasörle** açılıyor
  (`veri sayısı: 14` → `Seçili dosyanın klasörü kullanılıyor: Nemo (#3)`)
- [x] Araçlar menüsünden erişilebiliyor (`veri sayısı: 0` → `klasör uygulamada seçilecek`)
- [x] Uygulama doğru klasörle açılıyor (`--folder 3` → arayüzde `Klasör: \Nemo`)
- [x] Uygulama kurulu değilse, nereye kurulması gerektiğini söyleyen mesaj çıkıyor
  (`Uygulama bulunamadı; beklenen konumlar kullanıcıya bildirildi.`)
- [x] Pencere kapanıp tekrar açılabiliyor (aynı gün içinde 20'den fazla başlatma)
- [x] Explorer kapatılıp açıldığında çökme yok (eklentinin 5. ve 6. sürümleriyle 20'den fazla
  yeniden yükleme; hata kaydı yok)
- [ ] Administration'dan eklenti kaldırıldığında Explorer sağlıklı kalıyor — *denenmedi*

Günlükte her adım görünür (`[eklenti]` etiketiyle):

```
[BILGI] [eklenti] Eklenti yüklendi, komut kaydedildi.
[BILGI] [eklenti] Komut tetiklendi (veri sayısı: 1).
[BILGI] [eklenti] Seçili klasör: Parts (#42).
[BILGI] [eklenti] Uygulama başlatılıyor: C:\...\PdmVariableStudio.exe --vault "..." --folder 42
```

`Komut tetiklendi` satırı yoksa komut hâlâ görünmüyor ya da tıklanmamış demektir.

---

## 9. Ölçüm

Aşağıdaki tabloyu gerçek vault'ta doldurun. Tasarım 1.000 dosya için makul süre hedefliyor.

| Dosya sayısı | Değişken sayısı | Tarama | Değer okuma | Excel yazma | Toplam |
|---|---|---|---|---|---|
| 14 (TEKYAZ, 2026-09-27) | 18 | 0,04–0,11 sn | 0,44 sn | 0,31 sn | **0,76 sn** |
| 100 | | | | | |
| 1.000 | | | | | |
| 3.000 | | | | | |

Süreler `studio.log` içindeki zaman damgalarından okunabilir.

**Uygulama ve geri alma** (dosya başına check-out → yazma → check-in):

| Dosya sayısı | Hücre | İşlem | Süre | Dosya başına |
|---|---|---|---|---|
| 14 (TEKYAZ, 2026-09-27) | 390 | Uygula | 22,4 sn | **~1,6 sn** |
| 14 (TEKYAZ, 2026-09-27) | 378 | Geri al | 21,4 sn | **~1,5 sn** |

Süreyi hücre sayısı değil **dosya sayısı** belirliyor: her dosya için check-out, `Flush()` ve
check-in birer PDM turu. Bu hızla 1.000 dosya yaklaşık **27 dakika** sürer; darboğaz okuma
değil yazma. Yazma stratejisi bilinçli seçildi (bkz. `CLAUDE.md` → "Yazma stratejisi");
hızlandırma ancak toplu check-out/check-in ile mümkün ve önce ölçülmeli.
