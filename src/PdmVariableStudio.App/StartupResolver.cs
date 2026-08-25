using System;
using System.Windows;
using PdmVariableStudio.Core.Abstractions;

namespace PdmVariableStudio.App;

/// <summary>Uygulamanın hangi vault ve klasörle açılacağı.</summary>
internal sealed class StartupContext
{
    public StartupContext(string vaultName, int folderId, string databaseName)
    {
        VaultName = vaultName;
        FolderId = folderId;
        DatabaseName = databaseName;
    }

    public string VaultName { get; }

    public int FolderId { get; }

    public string DatabaseName { get; }
}

/// <summary>
/// Eksik başlatma bilgilerini tamamlar.
/// </summary>
/// <remarks>
/// <para>
/// Yalnızca <b>vault</b> sorulur. Klasör sorulmaz: dosya seçimi artık ana pencerenin
/// "Dışa Aktar" sekmesinde yapılıyor (Klasör Ekle / Dosya Ekle / Ara ve Ekle) ve açılışta
/// ayrıca bir klasör seçme penceresi göstermek, kullanıcıyı henüz neyi seçeceğini bilmediği
/// bir karara zorluyordu — üstelik orada seçilen tek klasör, arayüzdeki çok kaynaklı
/// listenin yanında yetersiz kalıyordu.
/// </para>
/// <para>
/// Eklentiden bir klasörle başlatıldıysa o klasör doğrudan kullanılır ve hiçbir soru
/// sorulmaz. Tek başına açıldığında pencere boş listeyle açılır; kullanıcı kaynağı kendisi
/// seçer.
/// </para>
/// </remarks>
internal static class StartupResolver
{
    public static StartupContext? Resolve(StartupOptions options, IStudioLog log)
    {
        var vaultName = options.VaultName;
        var databaseName = string.Empty;

        // --- vault ---
        if (vaultName.Length == 0)
        {
            var vaults = VaultRegistry.List();

            if (vaults.Count == 0)
            {
                MessageBox.Show(
                    "Bu bilgisayarda kayıtlı bir PDM vault view'ı bulunamadı." + Environment.NewLine +
                    Environment.NewLine +
                    "SOLIDWORKS PDM istemcisinin kurulu ve en az bir vault view'ının " +
                    "oluşturulmuş olması gerekiyor.",
                    "PDM Variable Studio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return null;
            }

            if (vaults.Count == 1)
            {
                // Tek vault varsa sormaya gerek yok.
                vaultName = vaults[0].Name;
                databaseName = vaults[0].DatabaseName;
                log.Info($"Tek vault bulundu, otomatik seçildi: {vaultName}");
            }
            else
            {
                var chosen = VaultPicker.Show(vaults);
                if (chosen is null)
                {
                    return null;
                }

                vaultName = chosen.Name;
                databaseName = chosen.DatabaseName;
            }
        }
        else
        {
            databaseName = VaultRegistry.Find(vaultName)?.DatabaseName ?? string.Empty;
        }

        // Klasör sorulmaz; 0 ise pencere boş listeyle açılır ve kullanıcı kaynağı
        // "Dışa Aktar" sekmesinden seçer.
        return new StartupContext(vaultName, options.FolderId, databaseName);
    }
}
