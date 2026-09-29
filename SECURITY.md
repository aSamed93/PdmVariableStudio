# Güvenlik

## Desteklenen sürümler

Yalnızca [son yayımlanan sürüm](https://github.com/aSamed93/PdmVariableStudio/releases/latest)
düzeltme alır.

## Açık bildirme

Güvenlikle ilgili bir bulguyu **herkese açık issue olarak açmayın.** Bunun yerine GitHub'ın
özel bildirim kanalını kullanın:

**https://github.com/aSamed93/PdmVariableStudio/security/advisories/new**

Bildirimde şunlar yardımcı olur: etkilenen sürüm, yeniden üretme adımları, etkisi
(hangi veri, hangi kullanıcı). Bildirimi aldığımı 7 gün içinde bildiririm; düzeltme
süresi bulgunun ağırlığına bağlıdır ve sizinle konuşulur.

## Kapsam

Bu araç SOLIDWORKS PDM vault'una **yazar**. Güvenlik açısından ilgili alanlar:

- Kullanıcının PDM izinlerini aşan bir yazma ya da check-out
- Çalışma kitabı (`.xlsx`) yoluyla yanlış dosyaya yazdırma (bkz.
  [docs/WORKBOOK-CONTRACT.md](docs/WORKBOOK-CONTRACT.md))
- İşlem günlüğünün (`%LOCALAPPDATA%\PdmVariableStudio\journal`) bozulması ya da geri
  almanın yanlış değer yazması
- Kurulum betiklerinin ve kurulum dosyasının yükseltilmiş yetkiyle yaptıkları

Kapsam dışı: PDM sunucusunun ya da SOLIDWORKS'ün kendi açıkları, kullanıcının zaten yazma
hakkı olan verinin değiştirilmesi.

## Uygulamanın veri davranışı

Uygulama hiçbir yere ağ isteği göndermez; telemetri yoktur. Günlük, işlem geçmişi ve
ayarlar yalnızca yerel diskte (`%LOCALAPPDATA%\PdmVariableStudio`) ya da yöneticinin
`JournalRoot` ile belirlediği paylaşımda tutulur. Dışa aktarılan `.xlsx` dosyası vault
verisi içerir; paylaşırken buna göre davranın.
