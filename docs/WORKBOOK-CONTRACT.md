# Çalışma kitabı sözleşmesi — şema sürümü 1

Çalışma kitabı bizim kontrol ettiğimiz, sürümlenmiş bir formattır. İçe aktarma yalnızca sütun
başlıklarına güvenmez.

Kaynak: [`src/PdmVariableStudio.Core/Workbook/WorkbookSchema.cs`](../src/PdmVariableStudio.Core/Workbook/WorkbookSchema.cs)

---

## Sayfalar

| Sayfa | Görünürlük | İçerik |
|---|---|---|
| `Variables` | görünür | Kullanıcının düzenlediği tablo |
| `_Metadata` | `veryHidden` | Oturum bilgisi, vault kimliği, değişken tanımları, bütünlük damgası |
| `_Rows` | `veryHidden` | Satır kimliği ve **orijinal değer snapshot'ı** |

`veryHidden` sayfalar Excel arayüzündeki "Sayfayı Göster" ile geri getirilemez. Bu bir güvenlik
önlemi değil, kaza önleyicidir.

---

## `Variables` sayfası

| Sütun | Başlık | Kilit | Not |
|---|---|---|---|
| A | `#` | kilitli | `ExportRowId` — satırın kararlı numarası |
| B | Dosya Adı | kilitli | yalnızca gösterim |
| C | Klasör | kilitli | yalnızca gösterim |
| D | Konfigürasyon | kilitli | boş = dosya düzeyi |
| E… | değişken adları | **düzenlenebilir** | salt okunur değişkenlerde başlık `(salt okunur)` ile biter ve sütun kilitlidir |

Başlıklar **dışa aktarım anındaki arayüz dilinde** yazılır: İngilizcede `File Name`, `Folder`,
`Configuration`, salt okunur eki `(read-only)`, montaj sütunları `Assembly: Parent assembly`,
`Assembly: Level`, `Assembly: Quantity`. Şema sürümü bu yüzden değişmedi: okuyucu teknik
sütunları başlıktan değil sabit indisten, değişken sütunlarını `_Metadata`'daki `ColumnIndex`
tablosundan bulur. Başlığa yalnızca taşınmış bir değişken sütununu yeniden bulmak için bakılır
ve orada salt okunur ekinin **iki dildeki biçimi de** tanınır — bir dilde dışa aktarılan kitap
öbür dilde sorunsuz içe aktarılır (`LocalizationTests`). Sayfa adları (`Variables`,
`_Metadata`, `_Rows`) ve `_Metadata` anahtarları dile bağlı değildir.

Örnek:

| # | Dosya Adı | Klasör | Konfigürasyon | Açıklama | Malzeme | Ağırlık | Yayım Tarihi | Revizyon (salt okunur) |
|---|---|---|---|---|---|---|---|---|
| 1 | `MIL-001.sldprt` | `\Parts\Mil` | `@` | Ana mil | Ç1040 | 12,4 | 14.03.2026 | A |
| 2 | `MIL-001.sldprt` | `\Parts\Mil` | Uzun | Ana mil (uzun) | Ç1040 | 15,1 | | A |
| 3 | `GOVDE.sldasm` | `\Assemblies` | `@` | Gövde | | | | B |
| 4 | `sartname.docx` | `\Docs` | `@` | Şartname | | | | |

### Montaj bilgi sütunları

Dosyalar **Montajdan Ekle** ile eklendiyse `Konfigürasyon` ile ilk değişken sütunu arasına üç
bilgi sütunu girer ve değişken sütunları o kadar sağa kayar:

| Sütun | Başlık | Kilit | Not |
|---|---|---|---|
| E | `Montaj: Üst montaj` | kilitli | doğrudan üst montajın dosya adı; kök montajda boş |
| F | `Montaj: Seviye` | kilitli | 0 = kök montaj, 1 = doğrudan alt bileşen… |
| G | `Montaj: Adet` | kilitli | kök montajın **bir** kopyası için toplam adet (yol boyunca çarpılmış) |
| H… | değişken adları | düzenlenebilir | |

- Yalnızca **gösterim** içindir: `_Rows`'a ve damgaya girmez, içe aktarımda okunmaz.
  Şema sürümü bu yüzden değişmedi; eski sürümler bu kitapları okuyabilir.
- Kayma okuyucuyu etkilemez: değişken sütunları sabit bir konumdan değil `_Metadata`'daki
  `ColumnIndex` tablosundan bulunur (her zaman öyleydi).
- Başlıklar `Montaj:` önekiyle başlar ki `Adet` gibi bir vault değişkeniyle çakışıp sütun
  yeri kaymış bir değişken sanılmasın.
- Montajdan gelen bir dosyada yalnızca **montajın kullandığı konfigürasyonlar** ve dosya
  düzeyi (`@`) satır olur. Karışık listede montajdan gelmeyen satırların bilgi hücreleri boştur.
- Aynı dosya montajda birden fazla yerde geçse de dosya × konfigürasyon başına **tek satır**
  vardır; adetler toplanır.

**`@` ne demek:** konfigürasyondan bağımsız, dosya düzeyindeki değer — SOLIDWORKS'teki
**Custom** sekmesine karşılık gelir. Adlandırılmış konfigürasyonlar (`Uzun` gibi)
**Configuration Specific** sekmelerine karşılık gelir ve PDM veri kartında `@` ile **ayrı
ayrı** tutulurlar: birini değiştirmek diğerini etkilemez. Bu yüzden her konfigürasyon
çalışma kitabında ayrı bir satırdır.

Generic dosyalarda (`.docx` gibi) konfigürasyon kavramı yoktur; tek satır üretilir ve
`@` gösterilir. PDM'ye gönderilirken bu satır SOLIDWORKS dosyalarında `"@"`, generic
dosyalarda boş dize olur — ayrım `ConfigurationKey.ToPdmString` içinde, tek yerde.

**Neden görünür bir `#` sütunu, gizli sütunlar değil:** gizli sütunlar kopyala/yapıştır ve
sıralama sırasında sessizce bozuluyor. Görünür bir numara bozulduğunda kullanıcı fark ediyor.

**Değerler tipli yazılır:** sayı sütunları gerçek Excel sayısı, tarihler gerçek Excel tarihi
(seri numarası + tarih biçimi), boolean gerçek boolean hücresi. Bu, ondalık ayracı sorununu
kaynağında çözer — tr-TR Excel'de `12,4` görünen hücre aslında `12.4` sayısıdır ve hiç
ayrıştırmaya girmez.

Sayfa **parolasız** korumalıdır: yalnızca değişken sütunları düzenlenebilir. Kullanıcı korumayı
kaldırabilir; içe aktarma buna hazırlıklıdır ve kimlik doğrulamasını korumaya değil damgalara
dayandırır.

---

## `_Metadata` sayfası

Anahtar/değer bölümü (A sütunu anahtar, B değer):

```
SchemaVersion            1
ProductVersion           1.0.0
ExportSessionId          8f1c…-…-…
ExportedAtUtc            2026-08-25T09:14:33.0000000Z
ExportedByWindowsUser    MAKINA\ayse
ExportedByPdmUser        ayse
VaultName                MakinaVault
VaultRootPath            C:\MakinaVault
VaultDatabase            MakinaVaultDb
SourceFolderId           142
SourceFolderPath         \Parts\Mil
IncludeSubfolders        false
FileFilter               *.*
RowCount                 4
MetadataChecksum         a3f0…
```

Ardından `[Variables]` işaretçisi, bir başlık satırı ve **sütun eşlemesinin tek kaynağı**:

| VariableId | VariableName | DisplayName | DataType | ColumnIndex | IsMandatory | IsUnique | IsReadOnly |
|---|---|---|---|---|---|---|---|
| 17 | `Description` | Açıklama | 1 | 5 | 0 | 0 | 0 |
| 41 | `Weight` | Ağırlık | 3 | 7 | 0 | 0 | 0 |

`DataType`: 1 Text · 2 Int · 3 Float · 4 Bool · 5 Date

`MetadataChecksum`, damganın kendisi hariç tüm anahtar/değerler ve değişken tanımları üzerinden
hesaplanır (SHA-256).

---

## `_Rows` sayfası

| Sütun | Alan |
|---|---|
| A | `ExportRowId` |
| B | `FileId` |
| C | `FolderId` |
| D | `Configuration` (`@` = dosya düzeyi) |
| E | `FileVersion` |
| F | `IsSolidWorks` (0/1) |
| G | `RowFingerprint` |
| H… | orijinal değerler, `_Metadata` tanım sırasıyla |

Orijinal değerler **her zaman invariant metin** olarak yazılır. Bunlar karşılaştırmanın
referans noktası; Excel'in tipli hücre yorumuna ya da kullanıcının bölge ayarına bağlı
olmamaları şart.

`RowFingerprint` = SHA-256 ( `ExportSessionId | ExportRowId | FileId | FolderId | Configuration |`
uzunluk önekli orijinal değerler ). Oturum kimliği damgaya girdiği için bir kitaptaki satır
başka bir kitaba kopyalandığında damga tutmaz.

> **Kriptografik imza değildir** ve kötü niyetli bir değişikliği engellemez — anahtar dosyada
> olurdu, saklanacak yeri yok. Amacı KAZARA bozulmayı yakalamak.

---

## Kullanıcı ne yaparsa ne olur

| Kullanıcının yaptığı | Sonuç |
|---|---|
| Bir değeri değiştirdi | Normal akış: three-way karşılaştırma |
| Satırları sıraladı | **Etkisiz** — eşleme `#` numarasına göre |
| Satır sildi | O dosyaya dokunulmaz; hata değil |
| Yeni satır ekledi | `UnknownRow` — atlanır, PDM'ye yeni dosya EKLENMEZ |
| Aynı `#`'lı satırı çoğalttı | `DuplicateRow` — **ikisi de** uygulanmaz |
| `#` numarasını bozdu | `UnknownRow` — satır atlanır |
| Sütunu taşıdı | Başlık metniyle bulunur, `ColumnRelocated` **uyarısı** verilir, değer okunur |
| Değişken sütununu sildi | `VariableColumnMissing` — o değişken atlanır, PDM değeri korunur |
| Sütun başlığını değiştirdi | Eşleme `_Metadata`'daki `ColumnIndex` üzerinden yürür |
| Sayısal alana metin yazdı | `TypeMismatch` — uygulanmaz |
| Zorunlu alanı boşalttı | `MandatoryEmpty` — uygulanmaz |
| Boş bıraktı (zorunlu değil) | **Değeri sil** olarak yorumlanır |
| Dosyayı başka adla kaydetti | Etkisiz — kimlik dosya adına bağlı değil |
| `_Rows`'ta bir değeri değiştirdi | `RowTampered` — o satır uygulanmaz |
| `_Metadata`'da bir değeri değiştirdi | `MetadataChecksumMismatch` — **kitap tamamen reddedilir** |
| `_Rows` sayfasını sildi | **Kitap tamamen reddedilir** (snapshot olmadan karşılaştırma yapılamaz) |
| `_Metadata` sayfasını sildi | **Kitap tamamen reddedilir** |
| Başka vault'un kitabını yükledi | `WrongVault` — **kitap tamamen reddedilir** |
| Montaj bilgi sütunlarını değiştirdi/sildi | Etkisiz — bu sütunlar okunmaz |
| Sayfa korumasını kaldırdı | Etkisiz — doğrulama korumaya değil damgalara dayanıyor |

Bu tablonun her satırının bir testi var: [`tests/Workbook/WorkbookContractTests.cs`](../tests/PdmVariableStudio.Tests/Workbook/WorkbookContractTests.cs).

---

## Şema sürümü uyumsuzluğu

| Durum | Davranış |
|---|---|
| `SchemaVersion` == desteklenen | Normal akış |
| `SchemaVersion` < desteklenen | Göç denenir; başarısızsa salt-okunur uyarı, **Uygula kapalı** |
| `SchemaVersion` > desteklenen | **Reddedilir.** Tanımadığımız bir düzeni yorumlamaya çalışmak, yanlış dosyaya yazmanın en kısa yolu |

Yeni bir şema sürümü eklerken:

1. `WorkbookSchema.CurrentVersion` artırılır.
2. `MinimumSupportedVersion` **artırılmaz** (eski dosyalar okunmaya devam etsin).
3. `WorkbookReader` içine göç kodu eklenir.
4. `WorkbookContractTests` içine eski sürümün okunduğunu doğrulayan bir test eklenir.
