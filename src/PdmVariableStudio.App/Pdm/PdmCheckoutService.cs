using System;
using System.Runtime.InteropServices;
using EPDM.Interop.epdm;
using PdmVariableStudio.App.Threading;
using PdmVariableStudio.Core.Abstractions;
using PdmVariableStudio.Core.Domain;
using PdmVariableStudio.Core.Results;

namespace PdmVariableStudio.App.Pdm;

/// <summary>
/// Check-out / check-in işlemleri. Tüm çağrılar adanmış STA worker thread'inden yapılmalıdır.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sessiz lifecycle değişikliği yoktur.</b> Bu servis yalnızca çağrıldığında iş yapar ve
/// çağıran taraf (<c>ApplyService</c>) kullanıcının açık onayı olmadan çağırmaz.
/// </para>
/// <para>
/// <b>Check-in bir VERSION üretir</b>, revizyon artırmaz ve iş akışı geçişi tetiklemez.
/// Bu ayrım önemli: kullanıcıya "dosyalar iade edilecek" derken revizyon numarasının
/// artmayacağını da bilmesi gerekiyor.
/// </para>
/// </remarks>
internal sealed class PdmCheckoutService : IPdmCheckoutService
{
    private readonly IEdmVault5 _vault;
    private readonly IStudioLog _log;
    private readonly PdmVariableReader _reader;

    public PdmCheckoutService(IEdmVault5 vault, PdmVariableReader reader, IStudioLog log)
    {
        _vault = vault;
        _reader = reader;
        _log = log;
    }

    public OperationOutcome<CheckoutState> GetCheckoutState(PdmFileIdentity file)
    {
        using var scope = new ComScope();

        try
        {
            if (_vault.GetObject(EdmObjectType.EdmObject_File, file.FileId) is not IEdmFile5 pdmFile)
            {
                return OperationOutcome<CheckoutState>.Failure(IssueCode.FileNotFound, file.ToString());
            }

            scope.Track(pdmFile);
            pdmFile.Refresh();

            if (!pdmFile.IsLocked)
            {
                return OperationOutcome<CheckoutState>.Success(CheckoutState.NotCheckedOut);
            }

            var computer = pdmFile.LockedOnComputer ?? string.Empty;
            var isSameComputer = string.Equals(computer, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
            var isSameUser = pdmFile.LockedByUserID == CurrentUserId();

            return OperationOutcome<CheckoutState>.Success(
                isSameUser && isSameComputer
                    ? CheckoutState.ByMe(computer)
                    : CheckoutState.ByOther(SafeUserName(pdmFile), computer));
        }
        catch (COMException exception)
        {
            return OperationOutcome<CheckoutState>.Failure(
                PdmErrorTranslator.Translate(exception),
                PdmErrorTranslator.Describe(exception, _vault));
        }
    }

    public CheckoutResult EnsureCheckedOut(PdmFileIdentity file)
    {
        using var scope = new ComScope();

        try
        {
            if (_vault.GetObject(EdmObjectType.EdmObject_File, file.FileId) is not IEdmFile5 pdmFile)
            {
                return CheckoutResult.Failed(IssueCode.FileNotFound, file.ToString());
            }

            scope.Track(pdmFile);
            pdmFile.Refresh();

            if (pdmFile.IsLocked)
            {
                var computer = pdmFile.LockedOnComputer ?? string.Empty;
                var isSameComputer = string.Equals(computer, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

                if (pdmFile.LockedByUserID == CurrentUserId() && isSameComputer)
                {
                    // Zaten kullanıcıda çekili: olduğu gibi kullanılır ve işlem sonunda
                    // check-in EDİLMEZ. Kullanıcı onu başka bir iş için çekmiş olabilir.
                    return CheckoutResult.AlreadyMine();
                }

                return CheckoutResult.Failed(IssueCode.LockedByOtherUser, SafeUserName(pdmFile));
            }

            // EdmLock_Simple: yalnızca bu dosyayı çeker, referanslarına dokunmaz.
            // Referansları da çekmek, kullanıcının hiç istemediği dosyaları kilitlerdi.
            pdmFile.LockFile(file.FolderId, 0, (int)EdmLockFlag.EdmLock_Simple);

            _log.Info($"{file}: check-out edildi.");
            return CheckoutResult.CheckedOut();
        }
        catch (COMException exception)
        {
            var detail = PdmErrorTranslator.Describe(exception, _vault);
            _log.Warn($"{file}: check-out başarısız. {detail}");

            var code = PdmErrorTranslator.Translate(exception);
            return CheckoutResult.Failed(
                code == IssueCode.PdmApiError ? IssueCode.CheckoutFailed : code,
                detail);
        }
    }

    public OperationOutcome CheckIn(PdmFileIdentity file, string comment)
    {
        using var scope = new ComScope();

        try
        {
            if (_vault.GetObject(EdmObjectType.EdmObject_File, file.FileId) is not IEdmFile5 pdmFile)
            {
                return OperationOutcome.Failure(IssueCode.FileNotFound, file.ToString());
            }

            scope.Track(pdmFile);
            pdmFile.Refresh();

            if (!pdmFile.IsLocked)
            {
                // Zaten iade edilmiş. Hata değil.
                return OperationOutcome.Success();
            }

            // EdmUnlock_Simple: referansları check-in etmez, revizyon artırmaz.
            pdmFile.UnlockFile(0, comment ?? string.Empty, (int)EdmUnlockFlag.EdmUnlock_Simple, null);

            _log.Info($"{file}: check-in edildi.");
            return OperationOutcome.Success();
        }
        catch (COMException exception)
        {
            var detail = PdmErrorTranslator.Describe(exception, _vault);
            _log.Warn($"{file}: check-in başarısız. {detail}");
            return OperationOutcome.Failure(IssueCode.CheckInFailed, detail);
        }
    }

    public OperationOutcome UndoCheckout(PdmFileIdentity file)
    {
        using var scope = new ComScope();

        try
        {
            if (_vault.GetObject(EdmObjectType.EdmObject_File, file.FileId) is not IEdmFile5 pdmFile)
            {
                return OperationOutcome.Failure(IssueCode.FileNotFound, file.ToString());
            }

            scope.Track(pdmFile);
            pdmFile.Refresh();

            if (!pdmFile.IsLocked)
            {
                return OperationOutcome.Success();
            }

            pdmFile.UndoLockFile(0, false);

            _log.Info($"{file}: check-out geri alındı (yazma başarısız olduğu için).");
            return OperationOutcome.Success();
        }
        catch (COMException exception)
        {
            var detail = PdmErrorTranslator.Describe(exception, _vault);
            _log.Warn($"{file}: check-out geri alınamadı. {detail}");
            return OperationOutcome.Failure(IssueCode.PdmApiError, detail);
        }
    }

    private static string SafeUserName(IEdmFile5 file)
    {
        try
        {
            return file.LockedByUser?.Name ?? string.Empty;
        }
        catch (COMException)
        {
            // Kullanıcı nesnesini okuma yetkimiz olmayabilir; kilit bilgisi yine geçerli.
            return string.Empty;
        }
    }

    private int CurrentUserId()
    {
        try
        {
            return ((IEdmUserMgr5)_vault).GetLoggedInUser()?.ID ?? 0;
        }
        catch (COMException)
        {
            return 0;
        }
    }
}
