using System;
using System.Windows.Markup;
using PdmVariableStudio.Core.Localization;

namespace PdmVariableStudio.App.Views;

/// <summary>
/// XAML'de iki dilli metin: <c>{views:T 'Dışa Aktar', 'Export'}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Değer pencere yüklenirken bir kez çözülür; dil süreç boyunca değişmediği için
/// (<see cref="Loc"/>) bağlama ya da değişiklik bildirimi gerekmiyor.
/// </para>
/// <para>
/// Metinde kesme işareti varsa ters bölüyle kaçırılır: <c>'Excel\'de Aç'</c>. Virgül ve
/// süslü parantez de tırnak içinde güvenle kullanılabilir.
/// </para>
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string tr, string en)
    {
        Tr = tr;
        En = en;
    }

    [ConstructorArgument("tr")]
    public string Tr { get; set; } = string.Empty;

    [ConstructorArgument("en")]
    public string En { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Tr, En);
}
