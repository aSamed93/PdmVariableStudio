using EPDM.Interop.epdm;
using PdmVariableStudio.App.Threading;
using PdmVariableStudio.Core.Domain;

namespace PdmVariableStudio.App.Pdm;

/// <summary>
/// Bir <see cref="PdmFileIdentity"/>'yi, hâlâ kendi klasöründe yaşayan dosya nesnesine çözer.
/// Dosyaya dokunan her adaptör dosyayı buradan almalıdır.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden <c>GetObject</c> yetmiyor.</b> PDM silinen bir dosyanın nesnesini geri dönüşüm
/// kutusunda tutar ve <c>IEdmVault5.GetObject</c> onu hata vermeden döndürür. Gerçek vault'ta
/// gözlendi (2026-09-27): klasördeki dosyalar silinip aynı adla yeniden eklendi, yeni dosyalar
/// yeni kimlik aldı (#83 → #97). Eski bir çalışma kitabı içe aktarıldığında #83:
/// </para>
/// <list type="bullet">
/// <item><c>GetObject</c> ile sorunsuz döndü; <c>GetFirstFolderPosition</c> boştu — hiçbir
/// klasörde değildi.</item>
/// <item><c>LockFile(3, …)</c> hata VERMEDİ. Yerel yol aynı olduğu için yeni dosyanın (#97)
/// yerel kopyasının salt okunur özniteliği kalktı, <c>Flush()</c> değerleri #97'nin yerel
/// kopyasına yazdı — #97 hiç çekilmemişken.</item>
/// <item><c>UnlockFile</c> <c>E_EDM_FILE_NOT_LOCKED_BY_YOU</c> ile düştü; #97 "Local file
/// modified" durumunda, #83 ise görünmez biçimde kilitli kaldı.</item>
/// </list>
/// <para>
/// Yani ölü bir kimlik, başka bir dosyanın fiziksel kopyasına yazmaya yol açıyor. Bu yüzden
/// dosyanın <see cref="PdmFileIdentity.FolderId"/> klasöründe hâlâ bulunduğu doğrulanır.
/// Ad KONTROL EDİLMEZ: yeniden adlandırılmış dosya aynı kimliği korur ve geçerlidir.
/// </para>
/// </remarks>
internal static class PdmFileLookup
{
    /// <summary>
    /// Dosya canlıysa ve kimlikteki klasörde bulunuyorsa nesnesini döner; aksi hâlde <c>null</c>.
    /// Dönen nesne <paramref name="scope"/> tarafından izlenir.
    /// </summary>
    public static IEdmFile5? GetLiveFile(IEdmVault5 vault, PdmFileIdentity file, ComScope scope)
    {
        if (vault.GetObject(EdmObjectType.EdmObject_File, file.FileId) is not IEdmFile5 pdmFile)
        {
            return null;
        }

        scope.Track(pdmFile);
        pdmFile.Refresh();

        return IsInFolder(pdmFile, file.FolderId, scope) ? pdmFile : null;
    }

    private static bool IsInFolder(IEdmFile5 pdmFile, int folderId, ComScope scope)
    {
        var position = scope.Track(pdmFile.GetFirstFolderPosition());

        while (position is not null && !position.IsNull)
        {
            var folder = scope.Track(pdmFile.GetNextFolder(position));
            if (folder is not null && folder.ID == folderId)
            {
                return true;
            }
        }

        // Klasörü hiç yoksa dosya silinmiştir; başka klasördeyse taşınmıştır. İkisinde de
        // kimlik bu satırın gösterdiği dosya değildir — satırı atlamak, yanlış dosyaya
        // yazmaktan her zaman iyidir.
        return false;
    }
}
