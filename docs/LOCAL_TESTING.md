# Yerel PDM test kılavuzu

> **Önce [SPIKE-PHASE0.md](SPIKE-PHASE0.md).** Orada işaretlenmemiş bir eklentiyi üretim
> vault'una kurmayın.

## Derleme

```powershell
dotnet build PdmVariableStudio.sln --configuration Release
powershell -File docs\verify-package.ps1
```

## Adım 1 — Uygulamayı diske kurun

Uygulama vault'ta **değil**, diskte durur. Bu yüzden güncellemek için vault'a dokunmak ve
Explorer kapatmak gerekmez.

```powershell
# yerel kurulum (yönetici PowerShell)
powershell -File docs\install-app.ps1

# ya da ağ paylaşımına
powershell -File docs\install-app.ps1 -Destination "\\sunucu\pdm\VariableStudio"
```

Betik dosyaları kopyalar ve `HKLM\SOFTWARE\PdmVariableStudio\InstallPath` değerini yazar;
eklenti uygulamayı buradan bulur. Ağ payına kurulduysa her istemcide bir kez:

```powershell
powershell -File docs\install-app.ps1 -RegisterOnly -Destination "\\sunucu\pdm\VariableStudio"
```

**Eklenti olmadan da denenebilir** — bu, PDM tarafını hiç karıştırmadan uygulamayı sınamanın
en hızlı yolu:

```powershell
& "$env:ProgramFiles\PDM Variable Studio\PdmVariableStudio.exe"
```

Birden fazla vault view'ı varsa hangisine bağlanacağı sorulur; klasör sorulmaz, dosyaları
"Dışa Aktar" sekmesindeki kaynak düğmelerinden eklersiniz.

## Adım 2 — Eklentiyi vault'a yükleyin

1. SOLIDWORKS PDM Administration aracında **test vault'una** yönetici olarak bağlanın.
2. Vault altındaki **Add-ins** bölümünden eklenti ekleme işlemini başlatın.
3. `src\PdmVariableStudio.AddIn\bin\Release\net481\` altındaki **iki DLL'i birden** seçin:
   - `PdmVariableStudio.AddIn.dll` (~16 KB)
   - `EPDM.Interop.epdm.dll` (~374 KB)

   Eksik bırakılan bir bağımlılık, eklenti yüklenirken tip çözümlemesini düşürür ve PDM bunu
   *"not a multi-threaded COM-server"* gibi yanıltıcı bir iletiyle gösterir — hata mesajı
   eksik dosyayı söylemez.
4. Eklentinin yüklenmesini tamamlayın.
5. **Tüm** PDM Explorer ve Administration pencerelerini kapatıp tekrar açın.
   .NET PDM eklentileri çalışan istemciye zorla yeniden yüklenemez.

Yeni bir eklenti sürümü yüklerken `VariableStudioAddIn.GetAddInInfo` içindeki
`mlAddInVersion` **ve** `AssemblyInfo.cs` içindeki `AssemblyVersion` birlikte artırılmalı.
**Yalnızca uygulamayı** güncelliyorsanız (olağan durum) bu adıma hiç gerek yok —
`install-app.ps1` yeter.

### Eklentinin ESKİ bir sürümü kuruluysa — önce onu silin

.NET assembly'leri bir süreçten **kaldırılamaz**. PDM Administration bir vault'a bağlandığında
o vault'taki eklentileri kendi sürecine yükler; sonra aynı adlı bir eklentiyi güncellemeye
kalktığında eski kopya bellekte kalmaya devam eder. Bu, kurulumu kilitleyebilir ve genellikle
**"Sunucu Meşgul"** iletisiyle görünür.

Doğru sıra:

1. Tüm PDM Explorer ve Administration pencerelerini, varsa `PdmVariableStudio.exe`'yi kapatın.
2. Administration'ı açın. **Her test vault'unda** (yalnızca birinde değil) `Add-ins` düğümüne
   bakın; `PDM Variable Studio` varsa **silin**.
3. Administration'ı ve tüm Explorer pencerelerini **yeniden kapatın**. Bu adım şart: silmek
   kaydı kaldırır ama assembly hâlâ ConisioAdmin'in belleğindedir.
4. Administration'ı yeniden açıp iki DLL'i ekleyin.

Hangi sürümün yüklendiğini günlükten kesin olarak görebilirsiniz:

```
[BILGI] [eklenti] Eklenti yüklendi, komut kaydedildi. Sürüm 2.0.0.0, AddInVersion 2, süreç ConisioAdmin.
```

`Sürüm` ve `süreç` alanları olmayan bir satır **eski v1 eklentisinden** geliyordur — o vault'ta
hâlâ kayıtlı demektir.

### "Sunucu Meşgul" iletisi

```
Diğer program meşgul olduğundan bu eylem tamamlanamıyor.
```

Bu bir COM çağrısı zaman aşımıdır (`SERVERCALL_RETRYLATER`), veri kaybı riski taşımaz.
`Yeniden Dene` güvenlidir. Sık görülen sebepleri, kontrol sırasıyla:

| Sebep | Kontrol |
|---|---|
| Eklentinin eski sürümü hâlâ kayıtlı ve yüklü | Yukarıdaki dört adımı uygulayın |
| Açık Explorer penceresi eklentiyi tutuyor | Görev yöneticisinden tüm `explorer` + PDM süreçlerini kontrol edin |
| Arşiv sunucusu ya da SQL yavaş yanıt veriyor | `Yeniden Dene`, birkaç kez |
| Uygulama (`PdmVariableStudio.exe`) hâlâ açık ve vault oturumu tutuyor | Kapatın |

**Eklentinin kendisi sorunlu mu?** Günlüğe bakın. `Eklenti yüklendi, komut kaydedildi.`
satırı varsa eklenti yüklenmiş ve `GetAddInInfo` tamamlanmıştır — o hâlde sorun eklentide
değil, PDM'in kurulum işlemindedir.

## Kabul kontrolleri

### Kurulum
- [ ] Klasör ağacında klasöre sağ tık → **PDM Variable Studio** görünüyor
- [ ] Dosya listesinde bir klasöre sağ tık → görünüyor
- [ ] Bir dosyaya sağ tık → görünüyor ve **dosyanın klasörüyle** açılıyor
- [ ] Araçlar menüsünden erişilebiliyor
- [ ] Pencere açılıyor; üstte doğru vault adı ve doğru klasör yolu yazıyor
- [ ] Değişken listesi vault'un gerçek değişken adlarıyla doluyor
- [ ] Uygulama kurulu değilken komut, nereye kurulması gerektiğini söyleyen bir mesaj veriyor
      (denemek için `HKLM\SOFTWARE\PdmVariableStudio` anahtarını geçici olarak silin)

Komut görünmüyorsa günlüğe bakın — `[eklenti]` etiketli satırlar akışı adım adım gösterir:

```
[BILGI] [eklenti] Eklenti yüklendi, komut kaydedildi.   ← buraya kadar geldiyse eklenti sağlam
[BILGI] [eklenti] Komut tetiklendi (veri sayısı: 1).    ← bu yoksa komut görünmüyor/tıklanmadı
[BILGI] [eklenti] Seçili klasör: Parts (#42).
[BILGI] [eklenti] Uygulama başlatılıyor: ...
```

### Dosya kaynakları
- [ ] `Klasör Ekle` PDM klasör seçme penceresini açıyor, seçilen klasörün dosyaları listeye giriyor
- [ ] `Alt klasörler` işaretliyken alt klasör dosyaları da geliyor, kapalıyken gelmiyor
- [ ] `Dosya Ekle` PDM dosya seçme penceresini açıyor, çoklu seçim çalışıyor
- [ ] `Ara ve Ekle` — dosya adı deseniyle (`*.sldprt`) sonuç veriyor
- [ ] `Ara ve Ekle` — değişken adı + değeriyle süzüyor
- [ ] Boş ölçütle arama uyarı veriyor, vault'un tamamını çekmiyor
- [ ] Aynı dosya iki kaynaktan eklendiğinde **bir kez** listede
- [ ] `Kaynak` sütunu doğru (Klasör / Dosya seçimi / Arama)
- [ ] `Seçilenleri Sil` yalnızca işaretlileri çıkarıyor
- [ ] `Listeyi Temizle` listeyi boşaltıyor
- [ ] Eklentiden klasöre sağ tıklayarak gelince o klasörün dosyaları hazır geliyor
- [ ] Uygulama tek başına açıldığında **klasör seçme penceresi çıkmıyor**, liste boş başlıyor

### Dışa aktarım
- [ ] `Dışa Aktar` → `.xlsx` üretiliyor, Excel'de **uyarısız** açılıyor
- [ ] `_Metadata` ve `_Rows` sayfaları görünmüyor
- [ ] `#`, dosya adı, klasör ve konfigürasyon sütunları düzenlenemiyor
- [ ] Değişken sütunları düzenlenebiliyor
- [ ] Sayı sütunları gerçek sayı, tarihler gerçek tarih (tr-TR Excel'de doğru görünüyor)
- [ ] Konfigürasyonlu bir `.sldprt` her konfigürasyon için **ayrı satır** üretiyor
- [ ] Konfigürasyonsuz bir dosya (`.docx`) tek satır üretiyor

### İçe aktarım ve önizleme
Excel'de birkaç değeri değiştirin, birkaçına dokunmayın, birine geçersiz tip yazın,
satırları sıralayın.

- [ ] Değişmeyen hücreler `Değişmemiş`
- [ ] Değişenler `Güvenli` (yeşil, ✓, seçili)
- [ ] Sayısal alana yazılan `abc` → `Hata` (kırmızı, ✗, seçilemez)
- [ ] Satırları sıralamak sonucu **değiştirmiyor**
- [ ] `Yalnızca değişenler` / `Çakışmalar` / `Hatalar` süzgeçleri çalışıyor
- [ ] Arama kutusu dosya adı, değişken ve değere göre süzüyor
- [ ] Her durumun hem **simgesi** hem **metni** hem **rengi** var
- [ ] İpucu (tooltip) durumun nedenini ve önerilen eylemi anlatıyor

### Çakışma — kritik test
1. Dışa aktarın.
2. Excel'de bir değeri değiştirin.
3. **Ayrı bir PDM oturumundan** (ya da başka bir kullanıcıyla) aynı dosyanın aynı
   değişkenine **başka** bir değer yazın.
4. İçe aktarın.

- [ ] O hücre `Çakışma` (turuncu, ⚠)
- [ ] Onay kutusu **işaretlenemiyor**
- [ ] `Uygula` bu hücreyi yazmıyor; diğerinin değeri korunuyor

### Uygulama
- [ ] Hiçbir şey seçili değilken `Uygula` devre dışı
- [ ] Check-out gerekiyorsa uyarı şeridi ve onay kutusu görünüyor
- [ ] Onay kutusu işaretlenmeden `Uygula` devre dışı
- [ ] Uygulamadan sonra PDM Explorer'da kart değerleri değişmiş
- [ ] **SOLIDWORKS'te dosyayı açıp** custom property'nin de değiştiğini doğrulayın
      (`Flush` ikisine birden yazar — bu, batch update yerine bu yolun seçilme sebebi)
- [ ] Başkası tarafından çekili bir dosya atlanıyor ve gerekçesi görünüyor
- [ ] Diğer dosyalar uygulanmaya devam ediyor
- [ ] `İşlem sonunda check-in et` açıkken dosyalar iade ediliyor
- [ ] Kapalıyken çekili kalıyor
- [ ] Kullanıcının **zaten çekili** tuttuğu bir dosya işlem sonunda **check-in edilmiyor**

### Geri alma — kritik test
1. Bir değişiklik uygulayın (`A → B`).
2. `İşlem Geçmişi` sekmesinde işlem görünüyor mu?
3. `Geri alma önizlemesi` → hücre `Güvenli` görünmeli.
4. `Geri Al` → değer `A`'ya dönmeli.

Sonra çakışmalı hâli:

1. Yeni bir değişiklik uygulayın (`A → B`).
2. Ayrı bir oturumdan `B → C` yapın.
3. `Geri alma önizlemesi`

- [ ] Hücre `Çakışma` görünüyor
- [ ] Seçilemiyor
- [ ] `Geri Al` **`C` değerini korumalı**, `A` yazmamalı

### Kararlılık
- [ ] Uygulama sürerken pencere kapatılamıyor (uyarı çıkıyor)
- [ ] Uygulama penceresi görev çubuğunda ayrı bir öğe olarak duruyor ve alt+tab ile erişiliyor
- [ ] Uygulama açıkken PDM Explorer normal çalışmaya devam ediyor (donmuyor)
- [ ] Tarama sırasında `Durdur` çalışıyor
- [ ] Explorer'ı kapatıp açın: işlem geçmişi duruyor
- [ ] 20 kez pencere aç/kapa sonrası Explorer'ın tanıtıcı (handle) sayısı artmıyor
- [ ] Eklenti kaldırıldığında Explorer sağlıklı kalıyor

## Sorun giderme

Tanılama günlüğü:

```
%LOCALAPPDATA%\PdmVariableStudio\studio.log
```

Eklenti PDM sürecinin içinde çalıştığı için konsol yoktur; tüm hata ayrıntıları ve yığın
izleri bu dosyaya yazılır. Hata iletişim kutuları da bu yolu gösterir.

Canlı izlemek için:

```bash
Get-Content "$env:LOCALAPPDATA\PdmVariableStudio\studio.log" -Wait -Tail 40
```

İşlem günlüğü (Undo'nun kaynağı):

```
%LOCALAPPDATA%\PdmVariableStudio\journal\<vault adı>\
  index.jsonl          işlem özetleri
  ops\<id>.jsonl       her işlemin tam kaydı
```

Bunlar düz metin JSONL'dir; bir metin düzenleyiciyle okunabilir. **Silmeyin** — silinen bir
kayıt geri alınamayan bir değişiklik demektir.

## Güvenlik notu

Eklenti PDM veritabanına **doğrudan yazmaz**; her işlem PDM API'si üzerinden, PDM'in kendi
kurallarına uyarak yapılır. Yine de üretim vault'una kurmadan önce tüm akışı bir test
vault'unda doğrulayın.
