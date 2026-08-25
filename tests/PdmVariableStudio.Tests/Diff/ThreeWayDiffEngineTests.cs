using System;
using System.Globalization;
using PdmVariableStudio.Core.Diff;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;
using Xunit;

namespace PdmVariableStudio.Tests.Diff;

/// <summary>
/// Three-way karşılaştırmanın dört temel durumu (A/B/C/D) ve tip farkındalığı.
/// Bu dosya ürünün veri bütünlüğü garantisinin testidir; buradaki bir gerileme
/// başka bir kullanıcının işinin sessizce yok olması demektir.
/// </summary>
public class ThreeWayDiffEngineTests
{
    private static VariableValue Text(string? s) => VariableValue.FromRawText(s, PdmVariableType.Text);

    private static ChangeStatus Classify(string? original, string? current, string? requested) =>
        ThreeWayDiffEngine.Classify(Text(original), Text(current), Text(requested));

    // ------------------------------------------------------------------ Case A/B/C/D

    [Fact]
    public void CaseA_ExcelDegismemisse_Unchanged()
    {
        Assert.Equal(ChangeStatus.Unchanged, Classify(original: "Ç1040", current: "Ç1040", requested: "Ç1040"));
    }

    [Fact]
    public void CaseA_PdmDegismisAmaExcelDegismemisse_YineUnchanged()
    {
        // Kullanıcı bu hücreye dokunmamış. PDM tarafındaki değişiklik onun işi değil ve
        // üzerine yazılmamalı; "değişiklik yok" doğru karardır.
        Assert.Equal(ChangeStatus.Unchanged, Classify(original: "A", current: "B", requested: "A"));
    }

    [Fact]
    public void CaseB_YalnizcaExcelDegismisse_SafeChange()
    {
        Assert.Equal(ChangeStatus.SafeChange, Classify(original: "Ç1040", current: "Ç1040", requested: "Ç4140"));
    }

    [Fact]
    public void CaseC_IkisiDeFarkliYonlereDegismisse_Conflict()
    {
        Assert.Equal(ChangeStatus.Conflict, Classify(original: "A", current: "C", requested: "B"));
    }

    [Fact]
    public void CaseD_IstenenDegerPdmdeZatenVarsa_AlreadyApplied()
    {
        Assert.Equal(ChangeStatus.AlreadyApplied, Classify(original: "A", current: "B", requested: "B"));
    }

    [Fact]
    public void AlreadyApplied_SafeChangeDenOnceDegerlendirilir()
    {
        // Original == Current == "A", Requested == "A" olsaydı Unchanged olurdu.
        // Burada kritik olan: Current zaten istenen değere eşitken SafeChange denmemeli,
        // yoksa gereksiz bir PDM yazması üretilirdi.
        var status = Classify(original: "eski", current: "yeni", requested: "yeni");
        Assert.Equal(ChangeStatus.AlreadyApplied, status);
        Assert.NotEqual(ChangeStatus.SafeChange, status);
    }

    // ------------------------------------------------------------------ boş değerler

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "   ")]
    [InlineData(null, "\t")]
    [InlineData("  ", null)]
    public void NullBosVeBoslukBirbirineEsittir(string? a, string? b)
    {
        Assert.Equal(ChangeStatus.Unchanged, Classify(original: a, current: a, requested: b));
    }

    [Fact]
    public void DoluDegerinBosaltilmasi_SafeChange()
    {
        Assert.Equal(ChangeStatus.SafeChange, Classify(original: "Ç1040", current: "Ç1040", requested: ""));
    }

    [Fact]
    public void BastakiSondakiBoslukDegisiklikSayilmaz()
    {
        Assert.Equal(ChangeStatus.Unchanged, Classify(original: "Mil", current: "Mil", requested: "  Mil  "));
    }

    // ------------------------------------------------------------------ tip farkındalığı

    [Fact]
    public void Int_MetinVeSayiAyniDegeriTemsilEderse_Unchanged()
    {
        var original = VariableValue.From(10, PdmVariableType.Int);
        var current = VariableValue.From(10, PdmVariableType.Int);
        var requested = VariableValue.FromRawText("10", PdmVariableType.Int);

        Assert.Equal(ChangeStatus.Unchanged, ThreeWayDiffEngine.Classify(original, current, requested));
    }

    [Fact]
    public void Float_TurkceOndalikAyracli_MetinInvariantDegereEsittir()
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");

        var original = VariableValue.From(12.4d, PdmVariableType.Float);
        var current = VariableValue.From(12.4d, PdmVariableType.Float);

        // Kullanıcı Excel'de metin olarak "12,4" bırakmış olabilir.
        var requested = VariableValue.FromRawText("12,4", PdmVariableType.Float, tr);

        Assert.Equal(ChangeStatus.Unchanged, ThreeWayDiffEngine.Classify(original, current, requested));
    }

    [Fact]
    public void Float_GercekSayiHucresi_LocaleDenBagimsizdir()
    {
        // Excel'in gerçek sayı hücresi hiç ayrıştırmaya girmez; asıl çözüm budur.
        var fromCell = VariableValue.From(12.4d, PdmVariableType.Float);
        var fromInvariantText = VariableValue.FromRawText("12.4", PdmVariableType.Float);

        Assert.Equal(fromInvariantText, fromCell);
    }

    [Fact]
    public void Int_OndalikliAmaTamsayiyaDenkMetin_KabulEdilir()
    {
        var value = VariableValue.FromRawText("12.0", PdmVariableType.Int);
        Assert.Equal(VariableValueKind.Int, value.Kind);
        Assert.Equal("12", value.ToStorageString());
    }

    [Fact]
    public void Bool_FarkliYazimlarAyniDegereCevrilir()
    {
        foreach (var token in new[] { "true", "TRUE", "1", "-1", "evet", "Evet", "yes" })
        {
            var value = VariableValue.FromRawText(token, PdmVariableType.Bool);
            Assert.Equal(VariableValueKind.Bool, value.Kind);
            Assert.Equal("true", value.ToStorageString());
        }
    }

    [Fact]
    public void Bool_GercekBooleanHucresiMetinleEsittir()
    {
        Assert.Equal(
            VariableValue.FromRawText("TRUE", PdmVariableType.Bool),
            VariableValue.From(true, PdmVariableType.Bool));
    }

    [Fact]
    public void Date_SaatBileseniAtilir()
    {
        var sabah = VariableValue.From(new DateTime(2026, 3, 14, 8, 30, 0), PdmVariableType.Date);
        var aksam = VariableValue.From(new DateTime(2026, 3, 14, 19, 45, 0), PdmVariableType.Date);

        // PDM kart tarihleri gün hassasiyetinde; saat farkı yanlış "değişti" kararı üretiyordu.
        Assert.Equal(sabah, aksam);
    }

    [Fact]
    public void Date_FarkliGunlerFarklidir()
    {
        Assert.NotEqual(
            VariableValue.From(new DateTime(2026, 3, 14), PdmVariableType.Date),
            VariableValue.From(new DateTime(2026, 3, 15), PdmVariableType.Date));
    }

    [Fact]
    public void Date_TurkceBicimliMetinAyristirilir()
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var value = VariableValue.FromRawText("14.03.2026", PdmVariableType.Date, tr);

        Assert.Equal(VariableValueKind.Date, value.Kind);
        Assert.Equal("2026-03-14", value.ToStorageString());
    }

    // ------------------------------------------------------------------ ayrıştırılamayan

    [Fact]
    public void SayisalAlanaMetinYazilirsa_ValidationError()
    {
        var original = VariableValue.From(3.2m, PdmVariableType.Float);
        var current = VariableValue.From(3.2m, PdmVariableType.Float);
        var requested = VariableValue.FromRawText("abc", PdmVariableType.Float);

        Assert.True(requested.IsUnparseable);
        Assert.Equal(ChangeStatus.ValidationError, ThreeWayDiffEngine.Classify(original, current, requested));
    }

    [Fact]
    public void AyristirilamayanDeger_PdmyeYazilamaz()
    {
        var value = VariableValue.FromRawText("abc", PdmVariableType.Int);
        Assert.Throws<InvalidOperationException>(() => value.ToPdmObject());
    }

    [Fact]
    public void IkiFarkliBozukDeger_AyniSayilmaz()
    {
        var a = VariableValue.FromRawText("abc", PdmVariableType.Int);
        var b = VariableValue.FromRawText("xyz", PdmVariableType.Int);

        Assert.NotEqual(a, b);
    }

    // ------------------------------------------------------------------ yazılabilirlik

    [Fact]
    public void SafeChange_KilitliDosyada_UygulanamazAmaDurumuKorunur()
    {
        var cell = MakeCell(original: "A", current: "A", requested: "B");
        var context = new WriteContext(CheckoutState.ByOther("ayse"), hasWritePermission: true);

        ThreeWayDiffEngine.Evaluate(cell, context);

        // Durum hâlâ SafeChange: kullanıcı "değişiklik var ama yazılamıyor" bilgisini görmeli.
        Assert.Equal(ChangeStatus.SafeChange, cell.Status);
        Assert.False(cell.CanApply);
        Assert.Equal(IssueCode.LockedByOtherUser, cell.Reason);
        Assert.False(cell.IsSelected);
    }

    [Fact]
    public void SafeChange_YetkiYoksa_Uygulanamaz()
    {
        var cell = MakeCell("A", "A", "B");
        ThreeWayDiffEngine.Evaluate(cell, new WriteContext(CheckoutState.NotCheckedOut, hasWritePermission: false));

        Assert.False(cell.CanApply);
        Assert.Equal(IssueCode.PermissionDenied, cell.Reason);
    }

    [Fact]
    public void SafeChange_SaltOkunurDegiskende_Uygulanamaz()
    {
        var cell = MakeCell("A", "A", "B", readOnly: true);
        ThreeWayDiffEngine.Evaluate(cell, WriteContext.Writable);

        Assert.False(cell.CanApply);
        Assert.Equal(IssueCode.ReadOnlyVariable, cell.Reason);
    }

    [Fact]
    public void ZorunluDegiskenBosaltilamaz()
    {
        var cell = MakeCell("A", "A", "", mandatory: true);
        ThreeWayDiffEngine.Evaluate(cell, WriteContext.Writable);

        Assert.False(cell.CanApply);
        Assert.Equal(IssueCode.MandatoryEmpty, cell.Reason);
    }

    [Fact]
    public void SafeChange_TemizBaglamda_VarsayilanOlarakSecilidir()
    {
        var cell = MakeCell("A", "A", "B");
        ThreeWayDiffEngine.Evaluate(cell, WriteContext.Writable);

        Assert.True(cell.CanApply);
        Assert.True(cell.IsSelected);
    }

    [Fact]
    public void Conflict_AslaSecilmez()
    {
        var cell = MakeCell(original: "A", current: "C", requested: "B");
        ThreeWayDiffEngine.Evaluate(cell, WriteContext.Writable);

        Assert.Equal(ChangeStatus.Conflict, cell.Status);
        Assert.False(cell.CanApply);
        Assert.False(cell.IsSelected);
        Assert.Equal(IssueCode.Conflict, cell.Reason);
    }

    [Fact]
    public void OnizlemedenSonraDegisenDeger_CakismayaDoner()
    {
        var cell = MakeCell("A", "A", "B");
        ThreeWayDiffEngine.Evaluate(cell, WriteContext.Writable);
        Assert.True(cell.CanApply);

        cell.DemoteToConflict(Text("C"), IssueCode.ValueChangedSincePreview);

        Assert.Equal(ChangeStatus.Conflict, cell.Status);
        Assert.False(cell.CanApply);
        Assert.False(cell.IsSelected);
        Assert.Equal(IssueCode.ValueChangedSincePreview, cell.Reason);
    }

    [Fact]
    public void SilinmisDosya_Uygulanamaz()
    {
        var cell = MakeCell("A", "A", "B");
        ThreeWayDiffEngine.Evaluate(cell, new WriteContext(CheckoutState.NotCheckedOut, true, fileExists: false));

        Assert.False(cell.CanApply);
        Assert.Equal(IssueCode.FileNotFound, cell.Reason);
    }

    // ------------------------------------------------------------------ yardımcılar

    private static CellChange MakeCell(
        string? original,
        string? current,
        string? requested,
        bool readOnly = false,
        bool mandatory = false)
    {
        var file = new PdmFileIdentity(8814, 142, "MIL-001.sldprt", "\\Parts");
        var variable = new PdmVariableDefinition(
            23, "Material", PdmVariableType.Text, isMandatory: mandatory, isReadOnly: readOnly);

        return new CellChange(
            new CellCoordinate(file, ConfigurationKey.FileLevel, variable.VariableId),
            variable,
            exportRowId: 1,
            Text(original),
            Text(current),
            Text(requested));
    }
}
