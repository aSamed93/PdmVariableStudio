# Katkıda bulunma

Katkılar memnuniyetle karşılanır: hata bildirimi, öneri, belge düzeltmesi ya da kod.
Bu belge, bir PR'ın hızlıca birleşmesi için bilinmesi gerekenleri toplar.

## Kısa yol

1. Önce bir **issue** açın ya da mevcut birini seçin. Büyük bir değişikliği kodlamadan önce
   yaklaşımı issue'da konuşmak, geri çevrilen emeği önler.
2. Depoyu **fork** edin, `main`'den bir dal açın.
3. Değişikliği yapın, testleri koşturun, `CHANGELOG.md`'ye satır ekleyin.
4. **PR** açın; şablondaki kontrol listesini doldurun.

`main` dalı korumalıdır: doğrudan push kapalı, her PR CI'dan geçmeli ve depo sahibi
tarafından onaylanmalıdır.

## Geliştirme ortamı

| Gerek | Not |
|---|---|
| .NET SDK (8+) | `dotnet build` net481 hedefini derler; Visual Studio şart değil |
| .NET Framework 4.8.1 targeting pack | |
| SOLIDWORKS PDM Professional istemcisi | **yalnızca** `App` ve `AddIn` için (`EPDM.Interop.epdm.dll`). `Core` ve testler onsuz derlenir |
| Python 3 + `reportlab` | yalnızca kullanım kılavuzu PDF'ini yeniden üretmek için |

```bash
dotnet build PdmVariableStudio.sln -c Release
dotnet test tests/PdmVariableStudio.Tests -c Debug
```

PDM istemcisi olmayan bir makinede yalnızca `Core` ve testleri derleyin:

```bash
dotnet build src/PdmVariableStudio.Core/PdmVariableStudio.Core.csproj -c Release
dotnet test tests/PdmVariableStudio.Tests -c Debug
```

CI de tam olarak bunu yapar.

## Dokunmadan önce okuyun

[CLAUDE.md](CLAUDE.md) mimari kararların **gerekçelerini** anlatır: iki süreçli mimari,
three-way karşılaştırma, günlük yazma sırası, yazma stratejisi, sayı ayrıştırma sırası.
Dosya adı bir yapay zekâ aracına hitap ediyor ama içerik her katkıcı için yazıldı. Oradaki
kurallar geçmişte gerçek hatalardan çıktı; bir kuralı değiştirmek istiyorsanız PR'da
gerekçeyi yazın.

Özellikle:

- **`Core`'a `EPDM.Interop.epdm` referansı eklemeyin.** Testlerin PDM'siz koşması bu kurala
  bağlı.
- **`AddIn`'e proje referansı eklemeyin.** Eklenti paketi iki dosyada kalmalı.
- **`catch (Exception) { }` yok.** Her hata durumu bir `IssueCode` ve Türkçe metin taşır.
- **Önizleme olmadan yazma, onay olmadan check-out, günlüksüz değişiklik yok.** Bu üçü
  ürünün varlık sebebi; performans ya da kolaylık için gevşetilmez.

## Stil

- Tanımlayıcılar (sınıf, metot, alan, enum) **İngilizce**; XML doküman etiketleri, kod
  yorumları, belgeler ve kullanıcıya görünen tüm metinler **Türkçe**.
- Lint/format aracı yok; çevredeki kodun düzenine uyun.
- XAML'de onaltılık renk yazılmaz; renkler yalnızca `Themes/Palette.xaml` içinde.
- Her durum arayüzde simge + metin + renk ile gösterilir; renk tek başına anlam taşımaz.

## Testler

- Yeni davranış için test ekleyin; PDM tarafını `tests/Fakes/FakePdm.cs` içindeki
  `FakeVault` ile kurun.
- Bir hatayı düzeltiyorsanız önce hatayı yakalayan testi yazın
  (`tests/Diff/RegressionTests.cs` örnek).
- Gerçek vault'ta doğruladığınız bir API davranışı varsa
  [docs/SPIKE-PHASE0.md](docs/SPIKE-PHASE0.md)'a not düşün; şirket, kullanıcı ve dosya
  adlarını anonimleştirin.

## Sürüm ve belgeler

- Arayüzde düğme adı, durum, ayar ya da kurulum adımı değiştiyse
  `docs/guide/build_guide.py` ve `docs/KULLANIM.md` aynı değişiklikle güncellenir, PDF
  yeniden üretilir (`python docs/guide/build_guide.py`) ve birlikte commit edilir.
- Sürüm numaralarına dokunmayın; yayımı depo sahibi yapar. Dört ayrı sürüm vardır ve
  hangisinin ne zaman arttığı [CLAUDE.md](CLAUDE.md#sürüm-numaraları) içinde yazılı.
- Eklentiyi (`AddIn`) değiştiren bir PR nadirdir ve her istemcide vault yeniden yüklemesi
  demektir; `CHANGELOG.md`'de **(eklenti güncellendi)** ile işaretleyin.

## Commit ve PR

- Commit mesajı Türkçe, ilk satır 72 karakteri geçmesin, ne yapıldığını değil neyi
  değiştirdiğini söylesin.
- Bir PR bir konu. Yeniden biçimlendirme ve davranış değişikliği ayrı PR'larda.
- PR açıklamasında gerçek vault'ta denenip denenmediğini yazın; denenmediyse sorun değil,
  ama bilinmeli.

## Lisans

Katkınız [MIT lisansı](LICENSE) altında yayımlanır. PR göndererek bunu kabul etmiş
olursunuz.
