# PDM Variable Studio — Kullanım Kılavuzu

SOLIDWORKS PDM Professional'daki dosyaların **kart değişkenlerini** Excel'de toplu düzenlemek
için. Dışa aktar → Excel'de düzenle → geri yükle → önizle → onayla. Her değişiklik kaydedilir
ve sonradan güvenle geri alınabilir.

Ücretsizdir. Hiçbir yere veri göndermez. SOLIDWORKS ya da Dassault Systèmes ile bağlantılı
değildir.

---

## Gereksinimler

| | |
|---|---|
| PDM istemcisi | SOLIDWORKS PDM **Professional** 2022 (30.0) veya üstü — Standard desteklenmez |
| Windows | 10 / 11, .NET Framework 4.8.1 (Windows 11'de hazır; Windows 10'da Windows Update ile gelir) |
| Excel | Dosyayı düzenlemek için; LibreOffice de çalışır |
| Yetki | Kurulum için bir kez yönetici hakkı; eklentiyi vault'a yüklemek için PDM yönetici hesabı |

---

## Kurulum

İki parça var ve ikisi de gerekli: **uygulama** (bilgisayara kurulur) ve **eklenti**
(vault'a bir kez yüklenir, PDM Explorer'a menü komutunu ekler).

### 1. Uygulama — her bilgisayara

Yayım paketini (`PdmVariableStudio-x.y.z.zip`) açın. Yönetici olarak açılmış bir
PowerShell'de, açtığınız klasöre gidip:

```powershell
powershell -ExecutionPolicy Bypass -File install-app.ps1
```

Betik uygulamayı `C:\Program Files\PDM Variable Studio\` altına kopyalar ve eklentinin
onu bulması için kayıt defterine yolu yazar. `-ExecutionPolicy Bypass` yalnızca bu
komut için geçerlidir, sistem ayarını değiştirmez.

Yönetici hakkınız yoksa kullanıcı klasörüne kurabilirsiniz:

```powershell
powershell -ExecutionPolicy Bypass -File install-app.ps1 -CurrentUser -Destination "$env:LOCALAPPDATA\PDM Variable Studio"
```

Çok istemcili ortamda uygulama bir **ağ paylaşımına** bir kez kopyalanıp her istemcide
yalnızca kayıt yapılabilir:

```powershell
powershell -ExecutionPolicy Bypass -File install-app.ps1 -RegisterOnly -Destination "\\sunucu\pdm\VariableStudio"
```

### 2. Eklenti — vault'a bir kez

PDM Administration → vault → **Add-ins** → sağ tık → **New Add-in…**

Şu **iki dosyayı birlikte** seçin (Ctrl ile):

| Dosya | Nerede |
|---|---|
| `PdmVariableStudio.AddIn.dll` | paketteki `AddIn\` klasörü |
| `EPDM.Interop.epdm.dll` | `C:\Program Files\SOLIDWORKS PDM\` — **pakette yoktur**, PDM istemcinizden alın |

> **İkinci dosyayı atlamayın.** Eksik olduğunda PDM, eklentiyi yüklerken
> *"…is not a multi-threaded COM-server"* gibi yanıltıcı bir hata verir; hata mesajı
> eksik dosyayı söylemez.

### 3. Explorer'ları kapatıp açın

PDM eklentileri çalışan Explorer'a yüklenmez. **Tüm** PDM Explorer ve Administration
pencerelerini kapatın, sonra açın. Aynı şey eklentiyi her güncellediğinizde geçerlidir.

---

## Kullanım

### Uygulamayı açmak

- **PDM Explorer'dan:** vault içinde bir klasördeyken **Araçlar** menüsünden (ya da sağ tık
  menüsünden) *PDM Variable Studio*. O klasörün dosyaları hazır gelir.
- **Doğrudan:** kurulum klasöründeki `PdmVariableStudio.exe` (varsayılan
  `C:\Program Files\PDM Variable Studio\`; kurulum Başlat menüsü kısayolu oluşturmaz).
  Birden fazla vault'unuz varsa hangisine bağlanacağı sorulur; liste boş açılır.

### Dışa Aktar sekmesi

1. **Dosyaları toplayın.** Dört kaynak aynı listeye ekler; karışık kullanabilirsiniz:
   - **Klasör Ekle…** — PDM'nin klasör penceresi. *Alt klasörler* işaretliyse ağacın tamamı.
   - **Dosya Ekle…** — PDM'nin dosya penceresi, çoklu seçim.
   - **Ara ve Ekle…** — dosya adı deseni (`*.sldprt`, `MIL-*`) ve/veya bir değişkenin değeri.
   - **Montajdan Ekle…** — bir montaj seçin, konfigürasyonunu belirleyin; montajın bütün
     bileşenleri (istenirse alt montajların içindekiler de) farklı klasörlerde olsalar bile
     eklenir. Her parça için yalnızca **montajın kullandığı konfigürasyon** ve `@` satırı
     gelir. Excel'de *Üst montaj*, *Seviye* ve *Adet* sütunları görünür (yalnızca bilgi
     amaçlı; PDM'ye yazılmaz).
2. **Değişkenleri seçin.** Alttaki listeden gereksizleri kaldırın; dosya küçülür, Excel'de
   gezinmek kolaylaşır.
3. **Dışa Aktar…** → `.xlsx` kaydedin. Ardından **Excel'de Aç** ile hemen düzenlemeye geçin.

Konfigürasyonlu SOLIDWORKS dosyalarında **her konfigürasyon ayrı satırdır.** `@` satırı
SOLIDWORKS'ün *Custom* sekmesidir; adlandırılmış konfigürasyonlardan ayrıdır.

### Excel'de düzenlerken

- Yalnızca **değer hücrelerini** değiştirin. Dolu bir hücreyi **boşaltmak "değeri sil"**
  demektir ve önizlemede değişiklik olarak görünür; yanlışlıkla silmediğinizden emin olun.
  Zorunlu bir değişken boşaltılırsa o hücre uygulanmaz.
- Satır silebilir, sıralayabilir, sütun gizleyebilirsiniz; uygulama satırları başlığa ya da
  sıraya göre değil, gizli kimliklere göre eşler. Yeni eklenen satırlar atlanır — PDM'ye
  dosya eklenmez.
- `_Rows` ve `_Metadata` sayfalarını **silmeyin** — eşleme bilgisi orada. Silinirse dosya
  reddedilir, PDM'ye hiçbir şey yazılmaz.
- Sayıları kendi bölgesel ayarınızla yazın (`12,4`); uygulama Türkçe ve İngilizce
  biçimleri doğru ayırt eder.

### İçe Aktar sekmesi

1. **Çalışma kitabı seç…** → düzenlediğiniz dosya.
2. Uygulama her hücre için **üç değeri** karşılaştırır: dışa aktarım anındaki değer,
   Excel'deki değer ve PDM'deki **şu anki** değer. Sonuç bir önizleme tablosudur:

| Durum | Anlamı | Ne yapılır |
|---|---|---|
| **Güvenli değişiklik** | Siz değiştirdiniz, PDM tarafı dışa aktarımdan beri değişmedi | Uygulanır |
| **Değişmemiş** | Excel'de dokunulmamış | Atlanır |
| **Zaten uygulanmış** | PDM'de zaten aynı değer var (başkası yazmış) | Atlanır, gereksiz yazma yok |
| **Çakışma** | Siz değiştirdiniz **ve** PDM'de de başkası değiştirmiş | **Uygulanmaz.** Excel'de o hücreyi güncel değere göre yeniden değerlendirip tekrar içe aktarın |
| **Hata** | Tür uyuşmazlığı, bulunamayan dosya, bozuk satır | Uygulanmaz; açıklama tabloda |

Süzgeçlerle yalnızca çakışmaları ya da hataları görebilirsiniz.

3. **Check-out onayı.** Yazma için dosya check-out edilmek zorundadır. Kutuyu işaretlemeden
   hiçbir dosya check-out edilmez. Sizin zaten çekili tuttuğunuz dosyalara dokunulmaz;
   yalnızca uygulamanın çektiği dosyalar işlem sonunda check-in edilir (isterseniz kapatın).
4. **Uygula.** İlerleme durum çubuğunda; bir dosyanın hatası diğerlerini durdurmaz.

### İşlem Geçmişi sekmesi

Yapılan her uygulama burada listelenir. Bir işlemi seçip **Geri Alma Önizlemesi** ile
PDM'deki güncel değerle karşılaştırın:

- Değer hâlâ sizin yazdığınız → **güvenle geri alınır**
- Değer zaten eskisine dönmüş → atlanır
- Başkası üstüne başka bir şey yazmış → **çakışma, dokunulmaz** (onun işini bozmamak için)

Geri alma da bir işlemdir ve geçmişe girer.

---

## Dosyalar nerede

Hepsi bu bilgisayarda, `%LOCALAPPDATA%\PdmVariableStudio\` altında (Hakkında penceresinden
tek tıkla açılır):

| | |
|---|---|
| `studio.log` | Uygulama ve eklenti günlüğü. Sorun bildirirken bunu ekleyin. |
| `journal\<vault>\` | İşlem geçmişi; geri alma buradan çalışır. **Silmeyin** — silinen kayıt geri alınamaz. |
| `settings.json` | Tercihler (alt klasörler, check-in yorumu, son klasör). Silinirse varsayılanlar gelir. |

**İşlem geçmişi kullanıcıya ve bilgisayara özeldir.** Bir işlemi yalnızca yapıldığı
bilgisayarda, aynı Windows hesabıyla geri alabilirsiniz. Ekip olarak ortak bir geçmiş
isteniyorsa geçmiş bir ağ paylaşımına yönlendirilebilir:

- tek kullanıcı: `settings.json` içine `"journalRoot":"\\\\sunucu\\pdm\\gunluk"`
- tüm ekip (yönetici): kayıt defterine `HKLM\SOFTWARE\PdmVariableStudio\JournalRoot`
  dize değeri — bu ayar kullanıcı ayarını geçersiz kılar.

Paylaşım ulaşılamıyorsa uygulama yerel klasöre **düşmez**; işlem başlatılmaz ve nedeni
söylenir. Geri alınamayacak bir değişiklik yapmaktansa hiç yapmamak tercih edilir.

---

## Sık sorulanlar / sorun giderme

**Menüde komut görünmüyor.**
Explorer'ları kapatıp açtınız mı? Eklenti yalnızca yeni açılan pencerelere yüklenir.
Hâlâ yoksa Administration'da eklentinin yüklü olduğunu ve `studio.log` içinde
"Eklenti yüklendi" satırını kontrol edin.

**Eklenti yüklenirken "not a multi-threaded COM-server".**
`EPDM.Interop.epdm.dll` eklentiyle **birlikte** seçilmemiş. Eklentiyi silip iki dosyayla
yeniden ekleyin.

**Komuta tıklıyorum, hiçbir şey olmuyor.**
Uygulama kurulmamış ya da başka bir yolda. `studio.log` içinde "Uygulama başlatılıyor"
satırının yolu ile gerçek kurulum yerini karşılaştırın; `install-app.ps1` kayıt defteri
değerini düzeltir.

**"Başlatma başarısız" / "vault'a bağlanılamadı".**
Bu bilgisayarda o vault için bir *vault view* olmalı ve PDM'de oturum açmış olmalısınız.
Ayrıntı `studio.log` içinde.

**Windows SmartScreen "tanınmayan uygulama" uyarısı.**
Uygulama kod imzalı değildir (imza sertifikası ücretlidir). Yayım sayfasındaki SHA-256
değerini indirdiğiniz dosyayla karşılaştırıp *Daha fazla bilgi → Yine de çalıştır*
diyebilirsiniz.

**Çalışma kitabı reddedildi.**
`_Rows`/`_Metadata` sayfası silinmiş, dosya başka bir vault'tan ya da eski bir sürümden.
Yeniden dışa aktarıp değişiklikleri o dosyaya taşıyın. Reddedilen dosyadan PDM'ye hiçbir
şey yazılmaz.

**Önizlemede satırlar "Dosya bulunamadı" ile engellendi.**
Dosya dışa aktarımdan sonra silinmiş, başka klasöre taşınmış ya da silinip aynı adla
yeniden eklenmiş. Yeniden eklenen dosya PDM'de yeni bir kimlik alır; eski çalışma kitabı onu
tanımaz. Yanlış dosyaya yazmamak için bu satırlar bilerek atlanır. Klasörü yeniden dışa
aktarıp değişiklikleri yeni dosyaya taşıyın.

**Bir dosya "özel olarak açık" hatası veriyor.**
Dosya SOLIDWORKS'te açık. Kapatıp o dosya için tekrar uygulayın; diğer dosyalar etkilenmez.

---

## Sürüm yükseltme

- **Uygulama:** yeni paketteki `install-app.ps1`'i çalıştırın. Explorer kapatmak gerekmez.
- **Eklenti:** yalnızca sürüm notlarında "eklenti güncellendi" yazıyorsa. Administration'da
  eklentiyi güncelleyin (iki dosya), tüm Explorer'ları kapatıp açın.

## Sorun bildirme

[GitHub Issues](https://github.com/aSamed93/PdmVariableStudio/issues). Hakkında
penceresindeki **Bilgileri Kopyala** çıktısını ve `studio.log` dosyasını ekleyin. Çalışma
kitabı eklerken vault verisi içerdiğini unutmayın.
