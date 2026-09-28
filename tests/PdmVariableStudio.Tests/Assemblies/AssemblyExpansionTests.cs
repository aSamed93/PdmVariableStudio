using System.Linq;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Services;
using Xunit;

namespace PdmVariableStudio.Tests.Assemblies;

/// <summary>
/// Montaj ağacının dışa aktarım listesine çevrilmesi: adet çarpımı, tekilleştirme, konfigürasyon.
/// </summary>
public class AssemblyExpansionTests
{
    private static readonly ConfigurationKey Default = ConfigurationKey.Named("Default");

    private static readonly PdmFileIdentity Root = new(1, 10, "KOK.sldasm");
    private static readonly PdmFileIdentity SubAssembly = new(2, 10, "ALT.sldasm");
    private static readonly PdmFileIdentity Bolt = new(3, 11, "CIVATA.sldprt");
    private static readonly PdmFileIdentity Pin = new(4, 11, "PIM.sldprt");
    private static readonly PdmFileIdentity Washer = new(5, 11, "PUL.sldprt");

    private static AssemblyOccurrence At(PdmFileIdentity file, int level, int quantity, string parent, ConfigurationKey? configuration = null) =>
        new(file, configuration ?? Default, level, quantity, parent);

    [Fact]
    public void AltMontajdakiAdet_YolBoyuncaCarpilir()
    {
        // KOK
        //  ├─ ALT ×2
        //  │   └─ CIVATA ×3   → kök için 6
        //  └─ PIM ×1
        var expanded = AssemblyExpansion.Expand(Root, Default, new[]
        {
            At(SubAssembly, 1, 2, "KOK.sldasm"),
            At(Bolt, 2, 3, "ALT.sldasm"),
            At(Pin, 1, 1, "KOK.sldasm"),
        }, includeRoot: false);

        Assert.Equal(2, Placement(expanded, SubAssembly, Default).TotalQuantity);
        Assert.Equal(6, Placement(expanded, Bolt, Default).TotalQuantity);
        Assert.Equal(1, Placement(expanded, Pin, Default).TotalQuantity);
        Assert.Equal(2, Placement(expanded, Bolt, Default).Level);
        Assert.Equal("ALT.sldasm", Placement(expanded, Bolt, Default).ParentName);
    }

    [Fact]
    public void AyniDosyaIkiDaldaGecerse_TekSatirVeAdetlerToplanir()
    {
        // CIVATA hem ALT(×2) içinde ×3 hem kökte doğrudan ×4 → 6 + 4 = 10.
        var expanded = AssemblyExpansion.Expand(Root, Default, new[]
        {
            At(SubAssembly, 1, 2, "KOK.sldasm"),
            At(Bolt, 2, 3, "ALT.sldasm"),
            At(Bolt, 1, 4, "KOK.sldasm"),
        }, includeRoot: false);

        Assert.Single(expanded, e => e.File.Equals(Bolt));

        var placement = Placement(expanded, Bolt, Default);
        Assert.Equal(10, placement.TotalQuantity);

        // Seviye: ağaçtaki en üst konumu. Üst montaj: ilk görüldüğü yer.
        Assert.Equal(1, placement.Level);
        Assert.Equal("ALT.sldasm", placement.ParentName);
    }

    [Fact]
    public void AgactaYukariCikinca_DerinCarpanlarSifirlanir()
    {
        // KOK
        //  ├─ ALT ×2
        //  │   └─ PIM ×5        → 10
        //  └─ PUL ×3            → 3 (ALT'ın ×2'si PUL'a SIZMAMALI)
        var expanded = AssemblyExpansion.Expand(Root, Default, new[]
        {
            At(SubAssembly, 1, 2, "KOK.sldasm"),
            At(Pin, 2, 5, "ALT.sldasm"),
            At(Washer, 1, 3, "KOK.sldasm"),
        }, includeRoot: false);

        Assert.Equal(10, Placement(expanded, Pin, Default).TotalQuantity);
        Assert.Equal(3, Placement(expanded, Washer, Default).TotalQuantity);
    }

    [Fact]
    public void YalnizcaKullanilanKonfigurasyonlarVeDosyaDuzeyiSatirOlur()
    {
        var kisa = ConfigurationKey.Named("Kısa");
        var uzun = ConfigurationKey.Named("Uzun");

        var expanded = AssemblyExpansion.Expand(Root, Default, new[]
        {
            At(Pin, 1, 2, "KOK.sldasm", kisa),
            At(Pin, 1, 1, "KOK.sldasm", uzun),
        }, includeRoot: false);

        var scope = expanded.Single(e => e.File.Equals(Pin)).Scope;

        // Dosya düzeyi (@) önce, sonra montajın kullandığı konfigürasyonlar — başka hiçbiri.
        Assert.Equal(new[] { ConfigurationKey.FileLevel, kisa, uzun }, scope.Configurations.ToArray());
        Assert.False(scope.Includes(ConfigurationKey.Named("Varsayılan")));

        Assert.Equal(2, scope.PlacementFor(kisa)!.TotalQuantity);
        Assert.Equal(1, scope.PlacementFor(uzun)!.TotalQuantity);

        // @ satırının adedi dosyanın montajdaki tüm kullanımı.
        Assert.Equal(3, scope.PlacementFor(ConfigurationKey.FileLevel)!.TotalQuantity);
    }

    [Fact]
    public void KokMontaj_IstenirseSeviyeSifirVeAdetBirIleEklenir()
    {
        var withRoot = AssemblyExpansion.Expand(Root, Default, new[] { At(Pin, 1, 1, "KOK.sldasm") }, includeRoot: true);
        var withoutRoot = AssemblyExpansion.Expand(Root, Default, new[] { At(Pin, 1, 1, "KOK.sldasm") }, includeRoot: false);

        Assert.Equal(Root, withRoot[0].File);
        var rootPlacement = Placement(withRoot, Root, Default);
        Assert.Equal(0, rootPlacement.Level);
        Assert.Equal(1, rootPlacement.TotalQuantity);
        Assert.Equal(string.Empty, rootPlacement.ParentName);

        Assert.DoesNotContain(withoutRoot, e => e.File.Equals(Root));
    }

    [Fact]
    public void Sira_IlkGorulmeSirasiylaKorunur()
    {
        var expanded = AssemblyExpansion.Expand(Root, Default, new[]
        {
            At(SubAssembly, 1, 1, "KOK.sldasm"),
            At(Bolt, 2, 1, "ALT.sldasm"),
            At(Pin, 1, 1, "KOK.sldasm"),
            At(Bolt, 1, 1, "KOK.sldasm"),
        }, includeRoot: true);

        Assert.Equal(new[] { Root, SubAssembly, Bolt, Pin }, expanded.Select(e => e.File).ToArray());
    }

    [Fact]
    public void KapsamBirlestirme_KonfigurasyonlariBirlestirirIlkYeriKorur()
    {
        var kisa = ConfigurationKey.Named("Kısa");
        var uzun = ConfigurationKey.Named("Uzun");

        var first = AssemblyExpansion.Expand(Root, Default, new[] { At(Pin, 1, 2, "KOK.sldasm", kisa) }, false)[0].Scope;
        var second = AssemblyExpansion.Expand(SubAssembly, Default, new[] { At(Pin, 1, 7, "ALT.sldasm", uzun), At(Pin, 1, 9, "ALT.sldasm", kisa) }, false)[0].Scope;

        var merged = first.Merge(second);

        Assert.Equal(new[] { ConfigurationKey.FileLevel, kisa, uzun }, merged.Configurations.ToArray());

        // Aynı konfigürasyonda ilk montajın bilgisi kalır: iki farklı ürünün adetleri toplanamaz.
        Assert.Equal(2, merged.PlacementFor(kisa)!.TotalQuantity);
        Assert.Equal("KOK.sldasm", merged.PlacementFor(kisa)!.ParentName);
        Assert.Equal(7, merged.PlacementFor(uzun)!.TotalQuantity);
    }

    private static AssemblyPlacement Placement(
        System.Collections.Generic.IReadOnlyList<ExpandedFile> expanded,
        PdmFileIdentity file,
        ConfigurationKey configuration) =>
        expanded.Single(e => e.File.Equals(file)).Scope.PlacementFor(configuration)!;
}
