using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace PdmVariableStudio.App;

/// <summary>Bu istemcide görünen bir vault.</summary>
internal sealed class VaultEntry
{
    public VaultEntry(string name, string shellRoot, string databaseName, string databaseServer)
    {
        Name = name;
        ShellRoot = shellRoot;
        DatabaseName = databaseName;
        DatabaseServer = databaseServer;
    }

    public string Name { get; }

    /// <summary>Yerel vault view kök yolu.</summary>
    public string ShellRoot { get; }

    public string DatabaseName { get; }

    public string DatabaseServer { get; }

    public override string ToString() =>
        ShellRoot.Length > 0 ? $"{Name}   ({ShellRoot})" : Name;
}

/// <summary>
/// İstemcide kayıtlı vault view'larını okur.
/// </summary>
/// <remarks>
/// <para>
/// Kaynak: <c>HKLM\SOFTWARE\SolidWorks\Applications\PDMWorks Enterprise\Databases</c>.
/// Kardeş projede (PDMetry) SQL bağlantısını çözmek için aynı anahtar kullanılıyor.
/// </para>
/// <para>
/// Bu bir <b>okuma</b>dır; kayıt defterine yazılmaz. Uygulama eklenti olmadan
/// başlatıldığında hangi vault'lara bağlanılabileceğini buradan öğrenir, ayrıca
/// <c>DbName</c> alanı vault kimliğini güçlendirir — PDM API'si vault için GUID vermediği
/// için veritabanı adı kimliğin üçüncü bileşeni.
/// </para>
/// </remarks>
internal static class VaultRegistry
{
    private const string KeyPath = @"SOFTWARE\SolidWorks\Applications\PDMWorks Enterprise\Databases";

    public static IReadOnlyList<VaultEntry> List()
    {
        var vaults = new List<VaultEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 64 bit ve 32 bit görünümlerin ikisi de okunur: PDM istemcisi ikisine birden
        // yazıyor ve hangisinin dolu olduğu kuruluma göre değişebiliyor.
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var entry in ReadView(view))
            {
                if (seen.Add(entry.Name))
                {
                    vaults.Add(entry);
                }
            }
        }

        vaults.Sort((left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));

        return vaults;
    }

    /// <summary>Adı verilen vault'un kaydını bulur; yoksa <c>null</c>.</summary>
    public static VaultEntry? Find(string vaultName)
    {
        foreach (var entry in List())
        {
            if (string.Equals(entry.Name, vaultName, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static IEnumerable<VaultEntry> ReadView(RegistryView view)
    {
        RegistryKey? databases = null;

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            databases = baseKey.OpenSubKey(KeyPath);
        }
        catch (System.Security.SecurityException)
        {
            // Kayıt defteri okunamıyor. Uygulama yine de eklentiden gelen vault adıyla
            // çalışabilir; yalnızca vault seçme listesi boş kalır.
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        if (databases is null)
        {
            yield break;
        }

        using (databases)
        {
            foreach (var name in databases.GetSubKeyNames())
            {
                VaultEntry? entry = null;

                try
                {
                    using var vaultKey = databases.OpenSubKey(name);
                    if (vaultKey is not null)
                    {
                        entry = new VaultEntry(
                            name,
                            Convert.ToString(vaultKey.GetValue("ShellRoot")) ?? string.Empty,
                            Convert.ToString(vaultKey.GetValue("DbName")) ?? string.Empty,
                            Convert.ToString(vaultKey.GetValue("DbServer")) ?? string.Empty);
                    }
                }
                catch (System.Security.SecurityException)
                {
                    entry = null;
                }

                if (entry is not null)
                {
                    yield return entry;
                }
            }
        }
    }
}
