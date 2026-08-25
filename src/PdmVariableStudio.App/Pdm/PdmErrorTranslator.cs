using System;
using System.Runtime.InteropServices;
using EPDM.Interop.epdm;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.App.Pdm;

/// <summary>
/// COM hatalarını alan seviyesinde anlamlı <see cref="IssueCode"/> değerlerine çevirir.
/// </summary>
/// <remarks>
/// Kullanıcıya asla ham HRESULT ya da istisna metni gösterilmez. Çeviri üç adımlı:
/// bilinen sabitler, PDM'in kendi hata metni (<c>IEdmVault11.GetErrorName</c>), ve son çare
/// olarak genel bir kod + günlüğe tam ayrıntı.
/// </remarks>
internal static class PdmErrorTranslator
{
    /// <summary>Dosya başka bir uygulama tarafından özel olarak açık.</summary>
    /// <remarks>
    /// Kardeş projede (ErpSecim) doğrulandı: dosya SOLIDWORKS'te açıkken dosya üzerinden
    /// yazma bu kodla düşüyor. Aynı kod Windows Gezgini'nden çalıştırıldığında görülmüyor —
    /// iki ortamda farklı davranmanın sebebi tam olarak bu.
    /// </remarks>
    public const int FileExclusivelyOpen = unchecked((int)0x8004020B);

    public const int AccessDenied = unchecked((int)0x80070005);
    public const int FileNotFound = unchecked((int)0x80070002);

    public static IssueCode Translate(COMException exception) => exception.ErrorCode switch
    {
        FileExclusivelyOpen => IssueCode.FileExclusivelyOpen,
        AccessDenied => IssueCode.PermissionDenied,
        FileNotFound => IssueCode.FileNotFound,
        _ => IssueCode.PdmApiError,
    };

    /// <summary>
    /// Günlüğe yazılacak teknik ayrıntı. Varsa PDM'in kendi hata metnini de ekler.
    /// </summary>
    public static string Describe(COMException exception, IEdmVault5? vault)
    {
        var hresult = $"HRESULT 0x{exception.ErrorCode:X8}";
        var pdmText = TryGetPdmErrorName(vault, exception.ErrorCode);

        return pdmText.Length > 0
            ? $"{hresult}: {pdmText} ({exception.Message})"
            : $"{hresult}: {exception.Message}";
    }

    /// <summary>
    /// PDM'in kendi hata metnini alır. Bu metin kullanıcının vault dilinde gelir ve bizim
    /// yazdığımız genel açıklamalardan çok daha kesin olabilir.
    /// </summary>
    private static string TryGetPdmErrorName(IEdmVault5? vault, int errorCode)
    {
        if (vault is not IEdmVault11 vault11)
        {
            return string.Empty;
        }

        try
        {
            return vault11.GetErrorName(errorCode) ?? string.Empty;
        }
        catch (COMException)
        {
            // Hata metni alınamadı. Asıl hatanın raporlanmasını engellememeli.
            return string.Empty;
        }
    }
}
