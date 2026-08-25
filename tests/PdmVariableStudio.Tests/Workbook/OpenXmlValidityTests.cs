using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using PdmVariableStudio.Core.Workbook;
using Xunit;

namespace PdmVariableStudio.Tests.Workbook;

/// <summary>
/// Uretilen dosyanin SpreadsheetML semasina uydugunu dogrular.
/// </summary>
/// <remarks>
/// Excel, sema disi bir dosyayi "onarilamaz icerik" diyerek acmayi reddeder ve kullanici
/// icin bu, eklentinin hic calismamasi demektir. Bu testler Excel kurulu olmayan bir
/// makinede de o hatayi yakalar.
/// </remarks>
public class OpenXmlValidityTests
{
    [Fact]
    public void UretilenKitap_SemayaUygun()
    {
        using var fixture = new WorkbookFixture().Write();

        using var document = SpreadsheetDocument.Open(fixture.Path_, isEditable: false);
        var validator = new OpenXmlValidator();
        var errors = validator.Validate(document).ToList();

        Assert.True(
            errors.Count == 0,
            "Sema hatalari:\n" + string.Join("\n", errors.Select(e => $"  {e.Path?.XPath}: {e.Description}")));
    }

    [Fact]
    public void TeknikSayfalar_VeryHiddenOlarakIsaretli()
    {
        using var fixture = new WorkbookFixture().Write();

        using var document = SpreadsheetDocument.Open(fixture.Path_, isEditable: false);
        var sheets = document.WorkbookPart!.Workbook.Descendants<Sheet>().ToList();

        Assert.Equal(SheetStateValues.VeryHidden, State(sheets, WorkbookSchema.MetadataSheet));
        Assert.Equal(SheetStateValues.VeryHidden, State(sheets, WorkbookSchema.RowsSheet));

        // Variables sayfasinda State ya hic yazilmaz ya da Visible olur; ikisi de gorunur demek.
        var variables = sheets.Single(s => s.Name?.Value == WorkbookSchema.VariablesSheet);
        Assert.True(
            variables.State is null || variables.State.Value == SheetStateValues.Visible,
            "Variables sayfasi gorunur olmali.");
    }

    [Fact]
    public void VariablesSayfasi_KorumaliVeDegiskenSutunlariDuzenlenebilir()
    {
        using var fixture = new WorkbookFixture().Write();

        using var document = SpreadsheetDocument.Open(fixture.Path_, isEditable: false);
        var workbookPart = document.WorkbookPart!;

        var sheet = workbookPart.Workbook.Descendants<Sheet>()
            .Single(s => s.Name?.Value == WorkbookSchema.VariablesSheet);

        var part = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);

        // Sayfa korumasi acik olmali, yoksa kimlik sutunlari kazara duzenlenebilir.
        var protection = part.Worksheet.Elements<SheetProtection>().SingleOrDefault();
        Assert.NotNull(protection);
        Assert.True(protection!.Sheet?.Value);

        // Stil tablosundaki kilit bayraklari: kimlik kilitli, duzenlenebilir degil.
        var formats = workbookPart.WorkbookStylesPart!.Stylesheet.CellFormats!;
        var identity = (CellFormat)formats.ElementAt(2);
        var editable = (CellFormat)formats.ElementAt(3);

        Assert.True(identity.Protection!.Locked!.Value);
        Assert.False(editable.Protection!.Locked!.Value);
    }

    private static SheetStateValues State(System.Collections.Generic.List<Sheet> sheets, string name) =>
        sheets.Single(s => s.Name?.Value == name).State!.Value;
}
