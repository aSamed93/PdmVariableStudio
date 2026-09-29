# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> **Dil kuralı:** Tanımlayıcılar (sınıf, metot, alan, enum) **İngilizce**; XML doküman
> etiketleri, kod yorumları, belgeler ve kullanıcıya görünen tüm metinler **Türkçe**.
> Bu ayrım bilinçli ve kardeş projelerle (PDMetry, ErpSecim) aynı. Bu düzeni koru.

## Ne olduğu

SOLIDWORKS PDM Professional eklentisi (`IEdmAddIn5`, COM, .NET Framework 4.8.1, WPF).
PDM Explorer'da Araçlar menüsünden (ya da bağlam menüsünden) açılır. Seçilen dosyaların
**kart değişkenlerini** `.xlsx` dosyasına aktarır, kullanıcı Excel'de düzenler, dosyayı geri
yükler; uygulama **three-way karşılaştırma** yapıp bir **önizleme** gösterir ve ancak onaydan
sonra PDM'yi günceller. Her işlem yerel bir **işlem günlüğüne** yazılır ve daha sonra
**güvenle geri alınabilir**.

Bu bir Excel içe/dışa aktarma betiği değil. Ürünün varlık sebebi veri bütünlüğü:
çakışma tespiti, checkout politikası ve mevcut değeri kontrol eden geri alma.

## Komutlar

```bash
dotnet build PdmVariableStudio.sln -c Release
dotnet test tests/PdmVariableStudio.Tests -c Debug
powershell -File docs/verify-package.ps1      # iki paketin dosya listesini denetler
powershell -File docs/install-app.ps1         # uygulamayı kurar, kayıt defteri değerini yazar
python docs/guide/build_guide.py              # kurulum ve kullanım kılavuzunu (PDF) yeniden üretir
```

Tek bir test sınıfı ya da testi çalıştırmak (xUnit, `FullyQualifiedName` ile süzülür;
test ad alanları `PdmVariableStudio.Tests.<Klasör>`):

```bash
dotnet test tests/PdmVariableStudio.Tests -c Debug --filter "FullyQualifiedName~UndoServiceTests"
```

```bash
dotnet test tests/PdmVariableStudio.Tests -c Debug --filter "FullyQualifiedName~NumberParsingRegressionTests.TurkceOndalik_BinlikAyraciSanilipOnKatBuyutulmez"
```

Uygulamayı eklentisiz denemek (vault'u kendi sorar, dosya listesi boş açılır):

```bash
src/PdmVariableStudio.App/bin/Release/net481/PdmVariableStudio.exe
```

`dotnet build` net481 hedefinde sorunsuz çalışıyor (dotnet SDK 10); Visual Studio'nun
MSBuild'ini aramaya gerek yok. `dotnet test` çıktısı bu makinede Türkçe gelir
("Başarılı! - Başarısız: 0 ..."). Testler PDM istemcisi olmadan koşar; `App` ve `AddIn`
derlemesi için `C:\Program Files\SOLIDWORKS PDM\EPDM.Interop.epdm.dll` gerekir.

Lint/format aracı yok; stil kaynak dosyaların kendisidir.

### Kullanıcı kılavuzu (PDF)

`docs/PdmVariableStudio-Kurulum-ve-Kullanim.pdf` **üretilmiş bir dosyadır**; kaynağı
`docs/guide/build_guide.py` (metin, tablolar ve bölümler betiğin içinde). PDF'i elle
düzenlemeyin. Arayüzde bir düğme adı, durum, ayar ya da kurulum adımı değiştiğinde betiği
aynı değişiklikle güncelleyip PDF'i yeniden üretin ve ikisini birlikte commit edin —
`docs/KULLANIM.md` de aynı içeriğin kısa hâlidir, o da güncellenir. Tek bağımlılık
`reportlab` (`docs/guide/requirements.txt`); derlemeyi ve testleri etkilemez. Sürüm
numarası `ProductInfo.Version`'dan okunur, çıktı deterministiktir (aynı kaynak → aynı bayt).

## İki süreçli mimari — en önemli tasarım kararı

Uygulama PDM Explorer'ın **içinde çalışmaz**. Vault'a yüklenen eklenti yalnızca bir
başlatıcıdır:

```
PDM Explorer                          ayrı süreç
─────────────                         ──────────────────────────
menüden komut
    ↓
PdmVariableStudio.AddIn.dll  ──────>  PdmVariableStudio.exe
(vault'ta, 2 DLL, ~390 KB)   Process   (diskte, ~7 MB)
                             .Start    kendi vault oturumunu açar
```

Gerekçe (ilk sürüm her şeyi vault'a koyuyordu ve bu üç sorunu birden üretti):

1. **Güncelleme.** Vault'a yüklenen bir bileşeni değiştirmek her istemcide tüm Explorer
   pencerelerinin kapatılmasını gerektiriyor. Sık değişen kısım (arayüz, Excel,
   karşılaştırma) artık vault'un dışında — güncelleme tek bir klasörü değiştirmek.
2. **Yükleme süresi.** 6.2 MB'lık OpenXml'i vault'a yüklemek uzun sürüyordu ve her sürümde
   tekrarlanıyordu.
3. **Kararlılık.** Uygulamadaki bir hata artık Explorer'ı düşüremez.

Bu ayrım ucuz oldu çünkü kod baştan böyle kurulmuştu: Explorer'dan **hiçbir COM nesnesi
taşınmıyor**, yalnızca vault adı ve klasör numarası geçiyor.

```
src/PdmVariableStudio.Core     alan modelleri, diff motoru, workbook sözleşmesi,
                               işlem günlüğü, akış servisleri  — INTEROP YOK, WPF YOK
src/PdmVariableStudio.App      PdmVariableStudio.exe: STA çalışma kuyruğu, PDM adaptörleri,
                               WPF arayüz, vault seçimi
src/PdmVariableStudio.AddIn    yalnızca IEdmAddIn5 + Process.Start — BAŞKA REFERANSI YOK
tests/PdmVariableStudio.Tests  xUnit — PDM istemcisi olmadan çalışır
```

Bağımlılık yönü tek yönlü: `AddIn → (hiçbir şey)`, `App → Core`, `Tests → Core`.

> **Core'a `EPDM.Interop.epdm` referansı EKLEMEYİN.** Eklenirse testler PDM istemcisi
> olmayan bir makinede derlenemez hâle gelir ve bunu hiçbir tasarım kuralı değil, yalnızca
> bu kural yakalar. Gerekçe `PdmVariableStudio.Core.csproj` içinde yazılı.

> **AddIn'e HİÇBİR proje referansı EKLEMEYİN** — `Core` dahil. Eklenti paketi iki dosyada
> kalmalı; eklentinin ihtiyaç duyduğu birkaç satır günlük `AddIn/AddInLog.cs` içinde ayrıca
> duruyor. Yeni kod `App` projesine aittir. Gerekçe `PdmVariableStudio.AddIn.csproj`
> içinde yazılı.

### Süreçler arası sözleşme: komut satırı

Eklenti uygulamayı `--vault "<ad>" --folder <id>` ile başlatır (`App/Program.cs` →
`StartupOptions.Parse`). Hiçbiri zorunlu değil; tanınmayan argümanlar **sessizce atlanır**.
Bu bilinçli: eski eklenti sürümleri `--parent <hwnd>` gönderiyor ve uygulama onu artık
kullanmıyor — argüman sözleşmesini geriye uyumlu tutmak, uygulamayı güncellerken eklentiyi
(dolayısıyla vault'u) yeniden yüklememek demek. **Yeni bir argümanı zorunlu yapmayın.**

Eklenti exe'yi `AddIn/AppLocator.cs` ile bulur: `HKLM\SOFTWARE\PdmVariableStudio\InstallPath`
→ `HKCU\...` → `%ProgramFiles%\PDM Variable Studio\` → `%ProgramFiles(x86)%\...`. Yalnızca
exe'nin varlığına bakmaz; eşlik eden DLL'ler eksikse (`FindMissingCompanions`) süreci hiç
başlatmaz ve eksik dosyaları söyler — çünkü `Process.Start` eksik derlemede de başarılı
döner, süreç sonra sessizce ölür.

## Core'un iç akışı — birden çok dosyaya yayılan resim

Core, PDM'yi yalnızca `Core/Abstractions/IPdmAbstractions.cs` içindeki arayüzler üzerinden
görür: `IPdmVaultContext`, `IPdmFolderScanner`, `IPdmFileBrowser`, `IPdmAssemblyReader`,
`IPdmVariableReader`, `IPdmVariableWriter`, `IPdmCheckoutService`, `IStudioLog` ve
`IOperationJournal.cs`.
İki gerçekleştirim vardır:

- `App/Pdm/*` — gerçek interop (`PdmVaultContext`, `PdmVariableReader`, `PdmVariableWriter`,
  `PdmCheckoutService`, `PdmFolderScanner`, `PdmFileBrowser`)
- `tests/Fakes/FakePdm.cs` — `FakeVault` tek sınıfta dört arayüzü birden uygular; bir test
  yazarken PDM tarafını buradan kurun

Dört akış servisi (`Core/Services/`) bu arayüzleri alır ve `OperationOutcome<T>` döner:

| Servis | Giriş noktası | Ne üretir |
|---|---|---|
| `ExportService` | `BuildSession` | `ExportSession` → `WorkbookWriter` `.xlsx` yazar |
| `ImportService` | `BuildChangeSet` | `WorkbookReader` okur, PDM'den taze değer çeker, `ThreeWayDiffEngine` sınıflandırır → `ChangeSet` |
| `ApplyService` | `Apply` | checkout → günlüğe niyet → yazma → günlüğe sonuç → (bizimse) check-in |
| `UndoService` | `BuildPreview`, `Undo` | günlük kaydını güncel PDM değeriyle karşılaştırır; `Undo` içeride `ApplyService`'i kullanır |

**Sonuç deseni:** başarısızlık istisnayla değil `OperationOutcome<T>` + `ValidationIssue`
(`IssueCode` + `IssueSeverity` + bağlam) ile taşınır. Kullanıcıya gösterilen metin
`Results/IssueCode.cs` içindeki `IssueText`'ten gelir; COM hataları `App/Pdm/PdmErrorTranslator.cs`
ile `IssueCode`'a çevrilir (bilinen HRESULT → PDM'in `GetErrorName` metni → genel kod +
günlüğe tam ayrıntı). Yeni bir hata durumu eklerken önce `IssueCode` + Türkçe metin ekleyin.

**App tarafında kablolama** tek yerde: `App/ViewModels/StudioViewModel.cs` içinde, vault
oturumu açıldıktan sonra `_queue.RunAsync` bloğunda tüm adaptörler ve servisler kurulur.
PDM'ye dokunan **her** çağrı `PdmWorkQueue.RunAsync` ile STA thread'ine gönderilir;
arayüz thread'inden doğrudan interop çağrısı yapılmaz.

## Dokunmadan önce bilinmesi gerekenler

### Yazma stratejisi bilinçli seçildi

`IEdmBatchUpdate2` **kullanılmıyor.** Toplu güncelleme yalnızca PDM SQL veritabanına yazıyor;
fiziksel dosyanın custom property'lerine dokunmuyor. Dosya özniteliğine eşlenmiş bir
değişkende bu, veritabanı ile CAD dosyası arasında sessiz bir ayrışma yaratır. Bunun yerine
`IEdmEnumeratorVariable` + `Flush()` kullanılıyor: ikisine birden yazar, ama dosya başına
check-out gerektirir ve daha yavaştır. **Performans için bu takası geri çevirmeyin** —
ürünün birinci önceliği veri bütünlüğü.

### Üç değer olmadan karşılaştırma yapılmaz

`CellChange` her zaman **Original / Current / Requested** üçünü birden taşır. İki değerle
(eski/yeni) çalışan bir tasarım "PDM tarafı da değişmiş" durumunu göremez ve başkasının işini
sessizce yok eder. `ThreeWayDiffEngine.Classify` içindeki **sıra da önemlidir**:
`AlreadyApplied` kontrolü `SafeChange`'den ÖNCE gelir, yoksa "başkası zaten aynı değeri
yazmış" durumu gereksiz bir PDM yazması üretir.

### Undo körlemesine eski değeri yazmaz

Journal her kayıtta hem `PreviousValue` (A) hem `AppliedValue` (B) tutar. Geri alma öncesi
PDM'deki güncel değer (C) okunur:

- `C == B` → güvenli, `B → A` yazılır
- `C == A` → zaten geri alınmış, no-op
- aksi → **ÇAKIŞMA, yazma yok**

Yalnızca `PreviousValue` saklamak bu kontrolü imkânsız kılar. `UndoService.Evaluate` bu
kuralın tek kaynağıdır ve testleri `tests/Undo/UndoServiceTests.cs` içinde.

### Günlük yazma sırası: intent → yazma → result

`IOperationJournal` sözleşmesi: her hücre için PDM'ye yazmadan ÖNCE `WriteIntent`, yazdıktan
SONRA `WriteResult`. Çökme durumunda "niyet var, sonuç yok" durumu `EntryResult.Unknown`
olur ve Undo önizlemesi bunu güncel PDM değerine bakarak çözer. Sırayı değiştirmek iki
hatadan birini davet eder: ya hiç yapılmamış bir değişiklik geri alınabilir görünür, ya da
gerçekten yapılmış bir değişikliğin geri alma bilgisi kaybolur.

**Günlük açılamıyorsa işlem HİÇ BAŞLATILMAZ.** Geri alınamayacak bir değişiklik yapmaktansa
hiç yapmamak yeğdir.

Günlük `Core/Journal/JsonlOperationJournal.cs` içinde; vault başına bir klasör, işlem
başına bir `.jsonl` dosyası ve bir indeks. JSON için dış bağımlılık yok — `FlatJson.cs`
eldeki küçük yazıcı/okuyucu.

### Montajdan dışa aktarım: referans ağacı, BOM şablonu değil

`App/Pdm/PdmAssemblyReader.cs` bileşenleri `IEdmReference10` ağacından okur (dosya, klasör,
montajın kullandığı konfigürasyon, adet — şablondan bağımsız). Ağacı dosya listesine çeviren
saf hesap `Core/Services/AssemblyExpansion.cs` içinde ve PDM'siz test ediliyor: adetler yol
boyunca **çarpılır**, aynı dosya × konfigürasyon **tek satır** olur, dosya düzeyi (`@`) satırı
her zaman eklenir. `GetFirstChildPosition3`'e **boş konfigürasyon vermeyin** — her bileşen
`"@"` döner (bkz. [docs/SPIKE-PHASE0.md](docs/SPIKE-PHASE0.md) madde 10).

Excel'deki *Üst montaj / Seviye / Adet* sütunları yalnızca gösterimdir; değişken sütunlarını
kaydırırlar ama okuyucu sütunları `_Metadata`'daki `ColumnIndex`'ten bulduğu için şema
sürümü değişmedi. Okuyucuya "değişkenler 5. sütundan başlar" varsayımı **eklemeyin**.

### Menü bayrağı: TEK bayrak, süzgeç yok

`RegisterCommand` yalnızca `EdmMenu_ShowInMenuBarTools` veriyor. Buraya iki başarısız
denemeden sonra gelindi ve gerekçe `AddIn/VariableStudioAddIn.cs` içinde ayrıntılı yazılı:

- `OnlyFolders | MustHaveSelection | OnlySingleSelection` (0x1–0x10) **süzgeçtir**, dosya
  listesi seçimi semantiğine ait; klasör ağacında komutu tamamen bastırdı.
- `ContextMenuItem` (0x400) ve `ContextMenuItemFolder` (0x800) verildiğinde `AddCmd` hata
  vermedi ama komut **hiçbir yerde** görünmedi — kayıt sessizce geçersiz kaldı.
- Bağlam menüsü varsayılandır (`EdmMenu_NeverInContextMenu` diye bir çıkarma bayrağı olması
  bunun kanıtı). Kardeş proje PDMetry de aynı tek bayrakla çalışıyor.

Hangi bağlamdan gelindiği `OnCmd` içinde, loglanabilir biçimde çözülür. **Bayrak eklemeyin:**
her kısıtlayıcı bayrak, komutun görünmez kalması için yeni bir yol ve bu sessiz bir
başarısızlık.

### COM nesnesi thread geçmez — ve süreç hiç geçmez

`EdmCmd.mpoVault` Explorer'ın STA apartment'ına aittir. `VariableStudioAddIn` ondan yalnızca
**değerler** okur (vault adı, klasör numarası) ve bunları komut satırı argümanı olarak
uygulamaya geçirir. Uygulama kendi sürecinde, `PdmWorkQueue` içindeki adanmış STA
thread'inde kendi vault oturumunu kurar.

Bu desen gerçek vault'ta doğrulandı (bkz. [docs/SPIKE-PHASE0.md](docs/SPIKE-PHASE0.md)
madde 1): `LoginAuto` arka plan STA thread'inde çalışıyor.

Alınan her COM nesnesi `ComScope` ile deterministik biçimde bırakılır. Özellikle:
**değişken numaralandırıcısı `Flush()` ile check-in arasında AÇIKÇA bırakılmalıdır**;
bırakılmazsa yerel dosya açık kalır ve check-in `0x8004020B` ile düşer. Referansı çöp
toplayıcıya bırakmak çare değil — check-in aynı metot içinde yapılıyor.

`OnCmd` içinden sızan bir istisna COM sınırını geçer ve Explorer'ı düşürebilir; eklentinin
her giriş noktası bu yüzden kendi `try/catch`'i içinde ve hatayı `AddInLog`'a yazar.

### Sayı ayrıştırmada sıra kritik

`VariableValue.TryParseDecimal` binlik ayracına izin veren denemeleri **en sona** bırakır.
İlk sürümde invariant kültür `AllowThousands` ile deneniyordu ve Türkçe yazılmış `"12,4"`
değeri `124` olarak ayrıştırılıyordu — sessiz ve on kat büyük bir veri bozulması.
Nöbetçi testler `tests/Diff/RegressionTests.cs` içinde; bu sırayı değiştirmeyin.

### Çalışma kitabı kullanıcı tarafından bozulmuş olabilir

`WorkbookReader`'ın temel varsayımı: **kullanıcı dosyaya her şeyi yapmış olabilir.** Satır
indisine, sütun indisine ve başlık metnine tek başına güvenilmez. Eşleme `_Rows` sayfasındaki
`ExportRowId` ve `_Metadata`'daki `VariableId → ColumnIndex` tablosu üzerinden yapılır;
`RowFingerprint` ve `MetadataChecksum` kazara bozulmayı yakalar.

**Şüpheli bir satır uygulanmaz.** Bir satırı atlamak, yanlış dosyaya yazmaktan her zaman
iyidir. Ayrıntı: [docs/WORKBOOK-CONTRACT.md](docs/WORKBOOK-CONTRACT.md).

### Sessiz lifecycle değişikliği yasak

Hiçbir dosya kullanıcının açık onayı olmadan check-out edilmez (`ApplyOptions.CheckoutConsent`).
Check-in **yalnızca bizim çektiğimiz** dosyalar için yapılır — kullanıcının kendi işi için
çekili tuttuğu bir dosyayı iade etmek onun işini bozar (`FileApplyPlan.WeCheckedOut`).
Yazma başarısız olursa kendi check-out'umuz geri alınır; dosyada iz bırakmayız.

### Renk tek başına anlam taşımaz

Her durum arayüzde **simge + metin + renk** ile gösterilir. Renk körlüğü ve yüksek kontrast
temalarında bilgi kaybolmamalı. Renkler yalnızca `Themes/Palette.xaml` içinde tanımlıdır;
XAML'in başka hiçbir yerine onaltılık renk yazılmaz.

Tema `Program.cs` içinde **uygulama** kaynaklarına birleştirilir, pencereye değil:
`StatusBrushConverter` fırçaları çalışma anında `Application.Current.TryFindResource` ile
çözer ve o arama yalnızca uygulama kaynaklarına bakar. Pencereye taşınırsa durum rozetleri
saydam kalır — hata vermez, sadece görünmez.

### `catch (Exception) { }` yasak

Beklenen her durum bir `IssueCode` taşır ve `Results/IssueCode.cs` içinde Türkçe karşılığı
vardır (kısa metin + neden + önerilen eylem). Kullanıcıya asla ham HRESULT ya da istisna
metni gösterilmez.

## Çalışma zamanı dosyaları (hata ayıklarken ilk bakılacak yerler)

Hepsi `%LOCALAPPDATA%\PdmVariableStudio\` altında; arayüzdeki **Hakkında** penceresi
(`Views/AboutDialog.cs`) hepsini tek tıkla açar ve "Bilgileri Kopyala" ile destek için
derler:

| Dosya | Kim yazar |
|---|---|
| `studio.log` (+ `.1`…`.N` döndürülmüş kopyalar) | hem uygulama (`Core/Diagnostics/StudioLog.cs`) hem eklenti (`AddIn/AddInLog.cs`) — aynı dosya. Arayüz dışı çökmeler de buraya düşer (`Program.cs` → `AppDomain.UnhandledException`) |
| `journal\<vault>\index.jsonl` + `ops\<id>.jsonl` | işlem günlüğü. Kök **ayarlanabilir**: `App/JournalRootResolver.cs` — öncelik `HKLM\SOFTWARE\PdmVariableStudio\JournalRoot` > `settings.json` `journalRoot` > varsayılan. Erişilemeyen kök varsayılana DÜŞMEZ; işlem başlamaz |
| `settings.json` | kullanıcı tercihleri (`Core/Settings/StudioSettings.cs`, FlatJson, tek satır). Yok/bozuk → varsayılanlar; yazma hatası sessiz. Yeni alan eklerken `Load` **ve** `Save` ikisine birden |

Bu dosyalar `.gitignore` ile depo dışında tutulur (`studio.log`, `*.xlsx`, `settings.json`).
`journal/` kuralı **kaldırıldı** — Windows'ta `Core/Journal/` kaynak klasörünü de yutuyordu.

## Doğrulanmamış API davranışları

Bazı PDM davranışları dokümantasyondan teyit edilemedi (help.solidworks.com bu makineden 403
döndürüyor). Kodda `PHASE 0'DA DOĞRULANACAK` yorumuyla işaretliler (şu an tek yer:
`App/Pdm/PdmVariableReader.cs` — `GetVarFromDb` okuma yolu; diğerleri gerçek vault'ta
doğrulandı) ve her biri **tek bir yerde** toplandı.

**Gerçek vault'ta doğrulanmadan üretime kurmayın:**
[docs/SPIKE-PHASE0.md](docs/SPIKE-PHASE0.md).

## Test döngüsü

PDM .NET eklentileri çalışan istemciye yeniden yüklenemez. Yeni derlemeyi denemeden önce
**tüm PDM Explorer ve Administration pencerelerini kapatın**, sonra açın. Unutulduğunda eski
DLL çalışmaya devam eder ve "değişiklik işe yaramadı" yanılgısı doğar.

Bu yalnızca **eklenti** için geçerli. Uygulama (`App`) ayrı süreç olduğu için Explorer açıkken
bile yeniden derlenip `install-app.ps1` ile değiştirilebilir — iki süreçli mimarinin asıl
kazancı bu.

Adım adım kurulum ve kabul kontrolleri: [docs/LOCAL_TESTING.md](docs/LOCAL_TESTING.md).

## Dağıtım — iki ayrı paket

### 1. Eklenti paketi (vault'a yüklenir) — **iki dosya**

`src\PdmVariableStudio.AddIn\bin\Release\net481\`

| Dosya | Boyut | Neden pakette |
|---|---|---|
| `PdmVariableStudio.AddIn.dll` | ~16 KB | eklentinin kendisi |
| `EPDM.Interop.epdm.dll` | ~374 KB | aşağıdaki gerekçe |

**Eksik bırakılan bir DLL, eklenti yüklenirken tip çözümlemesini düşürür ve PDM bunu
yanıltıcı bir *"not a multi-threaded COM-server"* iletisiyle gösterir — hata mesajı eksik
dosyayı SÖYLEMEZ.**

Bu liste **büyümemeli.** Vault'a yüklenen her dosya, her sürümde yeniden yüklenmesi ve her
istemcide Explorer kapatılması demek. Yeni kod `App` projesine aittir.

### 2. Uygulama (diske kurulur) — beş dosya

`src\PdmVariableStudio.App\bin\Release\net481\` → `%ProgramFiles%\PDM Variable Studio\`
(ya da bir ağ paylaşımı; yol `HKLM\SOFTWARE\PdmVariableStudio\InstallPath` ile bildirilir).

`PdmVariableStudio.exe`, `PdmVariableStudio.Core.dll`, `EPDM.Interop.epdm.dll`,
`DocumentFormat.OpenXml.dll`, `DocumentFormat.OpenXml.Framework.dll`

`docs/verify-package.ps1` iki listeyi de denetler; `docs/install-app.ps1` uygulamayı kurup
kayıt defteri değerini yazar. `docs/package-release.ps1` GitHub Releases için zip + SHA-256
üretir (`artifacts/`). `docs/build-installer.ps1` aynı dosyalardan Inno Setup kurulumu üretir
(`installer/PdmVariableStudio.iss` → `artifacts/PdmVariableStudio-Setup-<sürüm>.exe`); PDM
istemcisini denetler, .NET 4.8.1 eksikse kurar, eklentinin iki dosyasını `{app}\AddIn\`
altına hazırlar.

**Yayım paketine ve kuruluma `EPDM.Interop.epdm.dll` KONMAZ.** Dassault Systèmes'in dosyası; yeniden
dağıtım hakkımız yok ve her PDM istemcisinde zaten var. `install-app.ps1` ve kurulum (`.iss`'teki `external` bayrağı) onu
`C:\Program Files\SOLIDWORKS PDM\` altından kopyalar; eklenti için kullanıcı aynı yerden
alıp vault'a yükler (`docs/KULLANIM.md`). Yerel derleme çıktısında (`bin/`) bulunması
normaldir — `Private=true` gerekçesi aşağıda — ama pakete girmez.

**`EPDM.Interop.epdm.dll` pakete kopyalanır — bunu geri almayın.** Interop GAC'ta değil,
yalnızca PDM kurulum klasöründe. `IEdmAddIn5` uygulayan tip yüklenirken interop şart ve onu
yalnızca app base'i PDM klasörü olan süreçler bulabiliyor; Explorer'da eklenti ilk yüklenen
yönetilen bileşense çözümleme `0x80070002` ile düşer. Gerekçe `.csproj` içindeki `Reference`
düğümünde yazılı.

**ClosedXML bilinçli olarak seçilmedi:** net481'de dokuz ek DLL getiriyor. OpenXML SDK'nın
`net46` varlığının sıfır transitive bağımlılığı var (`System.IO.Packaging` .NET Framework'te
`WindowsBase` içinde geliyor). Bu karar eklenti vault'a yüklenirken alınmıştı; uygulama
ayrı sürece taşındıktan sonra boyut kısıtı gevşedi ama karar geçerli kaldı — daha az
bağımlılık, `App` klasöründe de daha az kırılma noktası.

## Sürüm numaraları

Dördü **ayrıdır**, birbirine bağlanmaz:

| Sürüm | Yer | Ne zaman artar |
|---|---|---|
| `ProductVersion` | `Core/Workbook/WorkbookWriter.cs` → `ProductInfo.Version`; `Core`/`App` csproj `Version`; `app.manifest` `assemblyIdentity` | ürün sürümü (SemVer); `package-release.ps1` zip adını, `build-installer.ps1` kurulum adını ve kılavuz PDF'i sürüm numarasını buradan alır (PDF yeniden üretilir), `CHANGELOG.md`'ye bölüm eklenir |
| `AddInVersion` | `AddIn/VariableStudioAddIn.GetAddInInfo` (`mlAddInVersion`) **ve** `AddIn/AssemblyInfo.cs` | **her vault yüklemesinde**, ikisi birlikte — eklenti nadiren değişir, bu yüzden nadiren artar. `CHANGELOG.md`'de o sürüm **(eklenti güncellendi)** ile işaretlenir |
| `WorkbookSchemaVersion` | `Core/Workbook/WorkbookSchema.cs` | `.xlsx` düzeni değiştiğinde |
| `JournalSchemaVersion` | `Core/Journal/JsonlOperationJournal.cs` | günlük satır düzeni değiştiğinde |

`AddInVersion` artırılmazsa PDM yeni paketi almaz. Şema sürümleri artırıldığında eski
dosyaların ne olacağı [docs/WORKBOOK-CONTRACT.md](docs/WORKBOOK-CONTRACT.md) içinde tanımlı;
**günlük hiçbir zaman silinmez**, çünkü silinen bir kayıt geri alınamayan bir değişiklik demek.

## Ön koşullar

- SOLIDWORKS PDM Professional client — geliştirme 2025 (33.5) üzerinde; eklenti en düşük
  **2022 (30.0)** istiyor (`mlRequiredVersionMajor/Minor`). Daha eski istemcide denenmedi.
- .NET Framework 4.8.1 targeting pack
- `C:\Program Files\SOLIDWORKS PDM\EPDM.Interop.epdm.dll` (csproj `HintPath` ile buradan okur)
