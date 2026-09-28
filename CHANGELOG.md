# Değişiklikler

Biçim [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/), sürümleme
[SemVer](https://semver.org/lang/tr/). Uygulama sürümü ile eklenti sürümü ayrıdır; eklentiyi
yeniden yüklemek gereken sürümler **(eklenti güncellendi)** ile işaretlenir.

## [Yayımlanmadı]

### Eklendi
- **Montajdan Ekle:** bir montaj seçilir, konfigürasyonu sorulur; montajın bütün bileşenleri
  (istenirse alt montajların içindekiler de) farklı klasörlerde olsalar bile listeye eklenir.
  Her parça için yalnızca **montajın kullandığı konfigürasyon** ve dosya düzeyi (`@`) satır
  olur. Çalışma kitabına *Üst montaj*, *Seviye* ve *Adet* bilgi sütunları eklenir (adetler
  alt montajlar boyunca çarpılır, aynı parça birden fazla yerde geçse de tek satırdır).
  Şema sürümü değişmedi; eklenti değişmedi.

## [1.1.1] — 2026-09-27

### Düzeltildi
- **Eski bir çalışma kitabı, silinip yeniden eklenmiş dosyaların yerel kopyasına
  yazıyordu.** Yeniden eklenen dosya yeni bir kimlik alır; PDM silinen dosyanın nesnesini
  hâlâ döndürdüğü için eski kimlik hata vermeden okunuyor, check-out "başarılı" oluyor ve
  aynı yerel yoldaki yeni dosyanın salt okunur özniteliği kalkıp değerler ona yazılıyordu
  (yeni dosya hiç çekilmeden); check-in ise `E_EDM_FILE_NOT_LOCKED_BY_YOU` ile düşüyordu.
  Artık her PDM çağrısından önce dosyanın dışa aktarımdaki klasöründe hâlâ bulunduğu
  doğrulanıyor (`PdmFileLookup`); bulunamayan dosyanın satırları önizlemede
  "Dosya bulunamadı" olarak engelleniyor.

## [1.1.0] — 2026-09-20 (eklenti güncellendi: 6)

### Eklendi
- **Hakkında** penceresi: sürüm, günlük / işlem geçmişi / ayar dosyası konumları (tek tıkla
  açılır), "Bilgileri Kopyala", proje ve sorun bildirme bağlantıları.
- Dışa aktarım sonrası **Excel'de Aç** ve **Klasörü Göster** düğmeleri.
- Tercihler `settings.json` içinde kalıcı: alt klasörler, check-in seçeneği ve yorumu,
  son dışa aktarım klasörü (dosya pencereleri oradan açılır).
- İşlem geçmişi kökü ayarlanabilir: `settings.json` → `journalRoot` ya da yönetici için
  `HKLM\SOFTWARE\PdmVariableStudio\JournalRoot` (ekip paylaşımı). Makine ilkesi kullanıcı
  ayarını geçersiz kılar.
- Arayüz dışındaki çökmeler de `studio.log`'a yazılıyor (`AppDomain.UnhandledException`,
  `TaskScheduler.UnobservedTaskException`).
- Son kullanıcı kılavuzu `docs/KULLANIM.md`; yayım paketi betiği `docs/package-release.ps1`;
  MIT lisansı.

### Düzeltildi
- **Dosya seçme penceresinde arama süreci düşürüyordu.** Manifest Common Controls v6
  bağımlılığını bildirmediği için sürece COMCTL32 v5 yükleniyor, PDM'nin yerel penceresi
  bellek ihlaliyle çöküyordu.
- **Tek başına açılışta "Başlatma başarısız".** Klasör verilmediğinde `GetFolderPath(0)`
  çağrılıyor ve PDM'nin fırlattığı `ArgumentException` yakalanmıyordu.
- `.gitignore`'daki `journal/` kuralı kaynak klasörünü de dışlıyordu; temiz klonda `Core`
  derlenmiyordu.

### Değişti
- **En düşük desteklenen PDM sürümü 2025 (33.5) → 2022 (30.0).** Eklenti sürümü 6;
  vault'taki eklentiyi yeniden yükleyin.
- `install-app.ps1` yayım paketinden de çalışıyor ve `EPDM.Interop.epdm.dll`'i PDM
  istemcisinden kopyalıyor; interop artık pakete konmuyor.

## [1.0.0] — 2026-08-25

İlk sürüm. Dışa aktar → Excel → içe aktar → three-way önizleme → uygula → geri al; işlem
günlüğü; iki süreçli mimari (ince eklenti + ayrı uygulama).

[1.1.0]: https://github.com/aSamed93/PdmVariableStudio/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/aSamed93/PdmVariableStudio/releases/tag/v1.0.0
