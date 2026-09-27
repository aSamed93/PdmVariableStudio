# PDM Variable Studio

**SOLIDWORKS PDM Professional 2025 için data card değişkenlerini Excel ile toplu düzenleme
aracı — önizlemeli, çakışma korumalı ve geri alınabilir.**

```
PDM Explorer'da klasöre sağ tık
        ↓
  .xlsx dışa aktar
        ↓
  Excel'de düzenle
        ↓
  geri yükle  →  three-way karşılaştırma  →  ÖNİZLEME
        ↓
      onayla  →  PDM güncellenir  →  işlem günlüğüne yazılır
        ↓
  gerektiğinde GÜVENLE geri al
```

| | |
|---|---|
| **Hedef** | SOLIDWORKS PDM Professional 2022 (30.0) ve üstü; 2025 (33.5) üzerinde geliştirildi |
| **Platform** | .NET Framework 4.8.1, WPF, AnyCPU |
| **Ürün sürümü** | 1.1.0 — [değişiklikler](CHANGELOG.md) |
| **Lisans** | [MIT](LICENSE) — ücretsiz, lisans anahtarı yok |
| **Gizlilik** | Hiçbir yere veri göndermez; günlük ve geçmiş yalnızca yerel diskte |
| **Test** | 121 birim testi, PDM istemcisi olmadan koşar |
| **Durum** | Gerçek vault'ta uçtan uca doğrulandı (dışa aktar → düzenle → içe aktar → uygula → geri al) |

> **Kurulum ve kullanım kılavuzu (PDF):**
> [docs/PdmVariableStudio-Kurulum-ve-Kullanim.pdf](docs/PdmVariableStudio-Kurulum-ve-Kullanim.pdf)
> — indirme, kurulum, eklentiyi vault'a yükleme, günlük kullanım ve sorun giderme; tek belgede,
> tıklanabilir içindekiler ile.

> SOLIDWORKS ve SOLIDWORKS PDM, Dassault Systèmes'in tescilli markalarıdır. Bu proje
> bağımsızdır; Dassault Systèmes ile bağlantılı değildir. Araç PDM'ye **yazar** — ilk
> kullanımdan önce küçük bir klasörde deneyin ve vault yedeğinizin güncel olduğundan emin olun.

---

## Neden basit bir betik değil

Toplu metadata güncellemesinin asıl riski Excel değil, **eşzamanlılık**. Kullanıcı dosyayı
dışa aktardıktan sonra Excel'de saatlerce çalışırken PDM tarafı da değişiyor. Bunu görmezden
gelen bir araç, başka bir kullanıcının işini sessizce yok eder.

Bu yüzden her hücre için **üç değer** birden tutulur:

| | Anlamı |
|---|---|
| `Original` | Dışa aktarım anındaki PDM değeri |
| `Requested` | Kullanıcının Excel'de bıraktığı değer |
| `Current` | İçe aktarım anındaki taze PDM değeri |

ve karşılaştırma buna göre yapılır:

| Durum | Sonuç |
|---|---|
| `Requested == Original` | Değişmemiş — dokunulmaz |
| `Requested == Current` | Zaten uygulanmış — yazma yok |
| `Current == Original` | **Güvenli değişiklik** — tek yazılan durum |
| hiçbiri | **Çakışma** — otomatik yazma YOK |

Aynı mantık geri almada da geçerlidir. Biz `A → B` yazdıktan sonra başkası `B → C` yaptıysa,
`C → A` yazmak onun işini yok ederdi; geri alma bunu **çakışma** olarak işaretler ve yazmaz.

## Öne çıkanlar

- **Önizleme olmadan yazma yok.** Her hücrenin neden yazılabilir/yazılamaz olduğu belirlenmiş
  olarak gösterilir.
- **Sessiz lifecycle değişikliği yok.** Kullanıcı açıkça onaylamadan hiçbir dosya check-out
  edilmez. Check-in yalnızca **bizim çektiğimiz** dosyalar için yapılır.
- **Konfigürasyonlar ayrı yönetilir.** Konfigürasyonlu SOLIDWORKS dosyalarında her
  konfigürasyon ayrı bir satırdır; "tüm konfigürasyonlar" davranışı kendiliğinden kullanılmaz.
  Dosya seviyesi (`@`) SOLIDWORKS'ün **Custom** sekmesine karşılık gelir ve adlandırılmış
  konfigürasyonlardan ayrı tutulur.
- **Veritabanı ile dosya ayrışmaz.** Değerler `IEdmEnumeratorVariable` + `Flush()` ile
  yazılır; hem PDM veritabanına hem fiziksel dosyanın custom property'lerine gider.
- **Kısmi başarısızlık izole edilir.** Bir dosyanın başarısızlığı diğerlerini durdurmaz;
  yazma başarısız olursa kendi check-out'umuz geri alınır.
- **Çökmeye dayanıklı işlem günlüğü.** Her hücre için PDM'ye yazmadan önce "niyet", yazdıktan
  sonra "sonuç" satırı diske indirilir. Commit ortasında çökme olsa bile geri alma doğru
  karar verebilir.
- **Renk tek başına anlam taşımaz.** Her durum simge + metin + renk ile gösterilir.

## İki süreçli mimari

Uygulama PDM Explorer'ın **içinde değil, ayrı bir süreçte** çalışır:

```
PDM Explorer                          ayrı süreç
─────────────                         ──────────────────────────
klasöre sağ tık
    ↓
PdmVariableStudio.AddIn.dll  ──────>  PdmVariableStudio.exe
(vault'ta, 2 DLL, ~390 KB)   Process   (diskte, ~7 MB)
                             .Start    kendi vault oturumunu açar
```

Gerekçe:

1. **Güncelleme.** Vault'a yüklenen bir bileşeni değiştirmek her istemcide tüm Explorer
   pencerelerinin kapatılmasını gerektirir. Sık değişen kısım (arayüz, Excel, karşılaştırma)
   vault'un dışında — güncelleme tek bir klasörü değiştirmek.
2. **Yükleme süresi.** 6 MB'lık OpenXml'i vault'a yüklemek her sürümde tekrarlanan uzun bir
   işti.
3. **Kararlılık.** Uygulamadaki bir hata Explorer'ı düşüremez.

Ayrım ucuz oldu çünkü kod baştan böyle kurulmuştu: Explorer'dan **hiçbir COM nesnesi
taşınmıyor**, yalnızca vault adı ve klasör numarası geçiyor.

Uygulama eklenti olmadan da çalışır: birden fazla vault view'ı varsa hangisine bağlanacağını
sorar, sonra boş bir listeyle açılır ve dosyaları arayüzden eklersiniz.

## Ekran akışı

Tek pencere, üç sekme.

### Dışa Aktar

Üstte kaynak düğmeleri, ortada **işleme alınacak dosyalar** listesi, altta değişken seçimi.

| Kaynak | Ne yapar |
|---|---|
| **Klasör Ekle** | PDM'in klasör seçme penceresi; `Alt klasörler` işaretliyse ağacın tamamı |
| **Dosya Ekle** | PDM'in kendi dosya seçme penceresi, çoklu seçim |
| **Ara ve Ekle** | Dosya adı deseni ve/veya kart değerine göre PDM araması |

Üçü de **aynı listeye** eklenir; yinelenenler atlanır ve her satır hangi kaynaktan geldiğini
gösterir. Böylece farklı klasörlerden ve arama sonuçlarından tek bir işlem kümesi kurulabilir.

Eklentiden bir klasöre sağ tıklayarak gelindiyse o klasörün dosyaları hazır gelir.

### İçe Aktar

Çalışma kitabı seç, önizleme tablosu, süzgeçler, check-out onayı, uygula.

### İşlem Geçmişi

Son işlemler, geri alma önizlemesi, geri al.

## Kurulum

Son kullanıcı için adım adım anlatım ve sorun giderme:
**[Kurulum ve Kullanım Kılavuzu (PDF)](docs/PdmVariableStudio-Kurulum-ve-Kullanim.pdf)** ya da
kısa hâli **[docs/KULLANIM.md](docs/KULLANIM.md)**.
Hazır paket: [Releases](https://github.com/aSamed93/PdmVariableStudio/releases).

Kaynaktan: iki ayrı paket var, uygulama diske kurulur, eklentinin **iki DLL'i** vault'a
yüklenir.

```powershell
dotnet build PdmVariableStudio.sln -c Release
powershell -ExecutionPolicy Bypass -File docs\verify-package.ps1
powershell -ExecutionPolicy Bypass -File docs\install-app.ps1
```

Yayım paketi üretmek: `powershell -ExecutionPolicy Bypass -File docs\package-release.ps1`
(`artifacts\` altına zip + SHA-256; `EPDM.Interop.epdm.dll` bilerek pakete girmez,
kurulum betiği onu PDM istemcisinden kopyalar).

Ardından `src\PdmVariableStudio.AddIn\bin\Release\net481\` altındaki **iki dosyayı**
(`PdmVariableStudio.AddIn.dll` ve `EPDM.Interop.epdm.dll`) Administration → Add-ins ile
vault'a yükleyin ve tüm Explorer pencerelerini kapatıp açın.

> Eksik bırakılan bir DLL, eklenti yüklenirken tip çözümlemesini düşürür ve PDM bunu
> yanıltıcı bir *"not a multi-threaded COM-server"* iletisiyle gösterir — hata mesajı eksik
> dosyayı **söylemez**. `docs/verify-package.ps1` her iki paketi de denetler.

Uygulama ağ paylaşımına da kurulabilir; yol `HKLM\SOFTWARE\PdmVariableStudio\InstallPath`
ile bildirilir. İşlem geçmişi de ekip için bir paylaşıma yönlendirilebilir
(`HKLM\SOFTWARE\PdmVariableStudio\JournalRoot`). Ayrıntı ve kabul kontrolleri:
[docs/LOCAL_TESTING.md](docs/LOCAL_TESTING.md).

## Depo yapısı

```
src/PdmVariableStudio.Core     alan modelleri, diff motoru, workbook sözleşmesi,
                               işlem günlüğü, akış servisleri  — INTEROP YOK, WPF YOK
src/PdmVariableStudio.App      PdmVariableStudio.exe: STA çalışma kuyruğu, PDM adaptörleri,
                               WPF arayüz, vault/klasör seçimi
src/PdmVariableStudio.AddIn    yalnızca IEdmAddIn5 + Process.Start — BAŞKA REFERANSI YOK
tests/PdmVariableStudio.Tests  xUnit — PDM istemcisi olmadan çalışır
docs/                          yerel test rehberi, workbook sözleşmesi, spike sonuçları,
                               kurulum ve paket doğrulama betikleri
```

Bağımlılık yönü tek yönlü: `AddIn → (hiçbir şey)`, `App → Core`.
`Core`'da **interop referansı yoktur** — bu, katman ayrımının derleme zamanındaki kanıtıdır
ve testlerin PDM'siz koşmasını sağlar.

```bash
dotnet test tests/PdmVariableStudio.Tests -c Debug
```

## Belgeler

| Belge | İçerik |
|---|---|
| [docs/PdmVariableStudio-Kurulum-ve-Kullanim.pdf](docs/PdmVariableStudio-Kurulum-ve-Kullanim.pdf) | **Kurulum ve kullanım kılavuzu (PDF):** indirme, kurulum, eklenti, kullanım, sorun giderme, kaldırma |
| [docs/KULLANIM.md](docs/KULLANIM.md) | Son kullanıcı kılavuzunun kısa hâli (yayım paketine de girer) |
| [CHANGELOG.md](CHANGELOG.md) | Sürüm notları |
| [CLAUDE.md](CLAUDE.md) | Mimari kararların gerekçeleri ve **dokunmadan önce bilinmesi gerekenler** |
| [docs/WORKBOOK-CONTRACT.md](docs/WORKBOOK-CONTRACT.md) | `.xlsx` formatının tam tanımı ve "kullanıcı şunu yaparsa ne olur" tablosu |
| [docs/LOCAL_TESTING.md](docs/LOCAL_TESTING.md) | Kurulum adımları ve kabul kontrol listeleri |
| [docs/SPIKE-PHASE0.md](docs/SPIKE-PHASE0.md) | Gerçek vault'ta doğrulanan PDM API davranışları |
