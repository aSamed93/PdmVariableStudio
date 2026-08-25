using System;
using System.Collections.Generic;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Journal;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.Core.Abstractions;

/// <summary>
/// Uygulanan her değişikliğin kalıcı kaydı. Undo'nun tek bilgi kaynağı.
/// </summary>
/// <remarks>
/// <para>
/// <b>Yazma sırası sözleşmesi.</b> Her hücre için önce <see cref="WriteIntent"/>, PDM'ye
/// yazdıktan SONRA <see cref="WriteResult"/> çağrılır. Bu sıra, çökme durumunda iki hatayı
/// birden önler:
/// </para>
/// <list type="bullet">
/// <item>Sonuç önce yazılsaydı ve yazma başarısız olsaydı, hiç yapılmamış bir değişiklik
/// geri alınabilir görünürdü.</item>
/// <item>Yalnızca sonuç yazılsaydı ve yazmadan sonra çökme olsaydı, gerçekten yapılmış bir
/// değişikliğin geri alma bilgisi kaybolurdu.</item>
/// </list>
/// <para>
/// Niyet var ama sonuç yoksa kayıt <see cref="EntryResult.Unknown"/> olur ve Undo önizlemesi
/// bunu PDM'deki güncel değere bakarak çözer.
/// </para>
/// <para>
/// Arayüz bilinçli olarak dosya sistemine bağlı değil: MVP JSONL kullanıyor, ama ileride
/// SQLite'a geçiş bu sözleşmeyi değiştirmeden yapılabilir.
/// </para>
/// </remarks>
public interface IOperationJournal
{
    /// <summary>
    /// Yeni bir işlem açar ve başlık satırını kalıcı olarak yazar.
    /// </summary>
    /// <remarks>
    /// Başarısız olursa işlem HİÇ BAŞLATILMAZ. Günlüğe yazılamayan bir değişiklik geri
    /// alınamaz; bunu sessizce kabul etmek ürünün temel vaadini bozar.
    /// </remarks>
    OperationOutcome Begin(ApplyOperation operation);

    /// <summary>Bir hücreye yazmadan ÖNCE çağrılır.</summary>
    OperationOutcome WriteIntent(Guid operationId, ApplyOperationEntry entry);

    /// <summary>Bir hücreye yazdıktan SONRA çağrılır.</summary>
    OperationOutcome WriteResult(Guid operationId, ApplyOperationEntry entry);

    /// <summary>İşlemi kapatır ve özetini indekse ekler.</summary>
    OperationOutcome Complete(ApplyOperation operation);

    /// <summary>Bir işlemi geri alındı olarak işaretler (append-only bir bağlantı satırı).</summary>
    OperationOutcome LinkUndo(Guid originalOperationId, Guid undoOperationId);

    /// <summary>İşlem özetlerini en yeniden eskiye doğru listeler.</summary>
    IReadOnlyList<ApplyOperation> ListOperations(VaultIdentity vault, int maxCount = 100);

    /// <summary>Bir işlemin tüm kayıtlarıyla birlikte tam hâlini okur.</summary>
    OperationOutcome<ApplyOperation> LoadOperation(VaultIdentity vault, Guid operationId);
}
