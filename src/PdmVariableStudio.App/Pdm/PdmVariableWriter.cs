using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EPDM.Interop.epdm;
using PdmVariableStudio.App.Threading;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.App.Pdm;

/// <summary>
/// Kart değerlerini yazar. Tüm çağrılar adanmış STA worker thread'inden yapılmalıdır.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden <c>IEdmBatchUpdate2</c> değil.</b> Toplu güncelleme yalnızca PDM SQL
/// veritabanına yazıyor; fiziksel dosyanın custom property'lerine dokunmuyor. Dosya
/// özniteliğine eşlenmiş bir değişkende bu, veritabanı ile CAD dosyası arasında sessiz bir
/// ayrışma (drift) yaratır ve dosya SOLIDWORKS'te açıldığında eski değer geri döner.
/// <c>IEdmEnumeratorVariable</c> + <c>Flush()</c> ikisine birden yazar. Bedeli dosya başına
/// check-out ve daha yavaş çalışma; veri bütünlüğü bu ürünün birinci önceliği olduğu için
/// bilinçli bir takas.
/// </para>
/// <para>
/// <b>Numaralandırıcı serbest bırakma.</b> <c>Flush()</c>'tan sonra numaralandırıcı AÇIKÇA
/// bırakılır. Bırakılmazsa yerel dosya açık kalır ve hemen ardından gelen check-in
/// <c>"exclusively opened by another application"</c> ile düşer. Kardeş projede
/// (PDMetry.DemoSeed) yaşanmış, doğrulanmış bir tuzak.
/// </para>
/// </remarks>
internal sealed class PdmVariableWriter : IPdmVariableWriter
{
    private readonly IEdmVault5 _vault;
    private readonly IStudioLog _log;

    public PdmVariableWriter(IEdmVault5 vault, IStudioLog log)
    {
        _vault = vault;
        _log = log;
    }

    public OperationOutcome<IReadOnlyList<VariableWriteResult>> WriteValues(
        PdmFileIdentity file,
        IReadOnlyList<VariableWrite> writes)
    {
        if (writes.Count == 0)
        {
            return OperationOutcome<IReadOnlyList<VariableWriteResult>>.Success(
                Array.Empty<VariableWriteResult>());
        }

        using var scope = new ComScope();
        object? enumerator = null;

        try
        {
            if (_vault.GetObject(EdmObjectType.EdmObject_File, file.FileId) is not IEdmFile5 pdmFile)
            {
                return OperationOutcome<IReadOnlyList<VariableWriteResult>>.Failure(
                    IssueCode.FileNotFound, file.ToString());
            }

            scope.Track(pdmFile);
            pdmFile.Refresh();

            if (!pdmFile.IsLocked)
            {
                // Çağıran taraf check-out'u sağlamış olmalıydı. Buraya düşmek bir akış
                // hatasıdır; sessizce check-out etmiyoruz çünkü lifecycle değiştiren her
                // davranış kullanıcının açık onayına bağlı.
                return OperationOutcome<IReadOnlyList<VariableWriteResult>>.Failure(
                    IssueCode.CheckoutRequired, file.ToString());
            }

            enumerator = scope.Track(pdmFile.GetEnumeratorVariable(string.Empty));
            var results = new List<VariableWriteResult>(writes.Count);

            foreach (var write in writes)
            {
                results.Add(WriteSingle(enumerator, file, write));
            }

            // Flush hem veritabanına hem fiziksel dosyaya yazmayı dener.
            try
            {
                ((IEdmEnumeratorVariable5)enumerator).Flush();
            }
            catch (COMException exception)
            {
                var code = PdmErrorTranslator.Translate(exception);
                _log.Error($"{file}: Flush başarısız. " + PdmErrorTranslator.Describe(exception, _vault));

                return OperationOutcome<IReadOnlyList<VariableWriteResult>>.Failure(
                    code, PdmErrorTranslator.Describe(exception, _vault));
            }
            finally
            {
                // Başarılı da olsa başarısız da olsa numaralandırıcı hemen bırakılır:
                // ardından gelen check-in ya da check-out geri alma dosyayı açık bulmamalı.
                scope.ReleaseNow(enumerator);
                enumerator = null;
            }

            return OperationOutcome<IReadOnlyList<VariableWriteResult>>.Success(results);
        }
        catch (COMException exception)
        {
            if (enumerator is not null)
            {
                scope.ReleaseNow(enumerator);
            }

            _log.Error($"{file}: yazma başarısız.", exception);

            return OperationOutcome<IReadOnlyList<VariableWriteResult>>.Failure(
                PdmErrorTranslator.Translate(exception),
                PdmErrorTranslator.Describe(exception, _vault));
        }
    }

    private VariableWriteResult WriteSingle(object enumerator, PdmFileIdentity file, VariableWrite write)
    {
        try
        {
            if (write.Value.IsUnparseable)
            {
                // Buraya asla düşmemeli: diff motoru ayrıştırılamayan değeri zaten
                // ValidationError olarak işaretliyor ve uygulanamaz kılıyor. Savunmacı kontrol.
                return new VariableWriteResult(
                    write.Configuration, write.Variable.VariableId, succeeded: false, IssueCode.TypeMismatch);
            }

            // SetVar imzası 'ref object' istiyor: değeri kutulayıp bir değişkende tutmak zorunlu.
            var value = write.Value.ToPdmObject();
            var configuration = write.Configuration.ToPdmString(file.IsSolidWorksFile);

            // Son parametre "tüm konfigürasyonlara uygula" anlamına geliyor ve HER ZAMAN
            // false gönderiliyor: her konfigürasyon ayrı bir hücre olarak yönetiliyor ve
            // kullanıcı açıkça istemeden birden fazla konfigürasyona yazmıyoruz.
            // PHASE 0'DA DOĞRULANACAK: bu parametrenin tam anlamı dokümantasyondan teyit
            // edilemedi; false göndermek gözlemlenen davranışa uygun ve güvenli taraf.
            ((IEdmEnumeratorVariable5)enumerator).SetVar(write.Variable.Name, configuration, ref value, false);

            return new VariableWriteResult(write.Configuration, write.Variable.VariableId, succeeded: true);
        }
        catch (COMException exception)
        {
            var code = PdmErrorTranslator.Translate(exception);
            var detail = PdmErrorTranslator.Describe(exception, _vault);

            _log.Warn($"{file}: '{write.Variable.Name}' yazılamadı (konfig " +
                      $"'{write.Configuration}'). {detail}");

            return new VariableWriteResult(
                write.Configuration, write.Variable.VariableId, succeeded: false, code, detail);
        }
    }
}
