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
/// Vault kimliği ve tanımları. Tüm çağrılar adanmış STA worker thread'inden yapılmalıdır.
/// </summary>
internal sealed class PdmVaultContext : IPdmVaultContext
{
    private readonly IEdmVault5 _vault;
    private readonly IStudioLog _log;

    public PdmVaultContext(IEdmVault5 vault, IStudioLog log, string? databaseName = null)
    {
        _vault = vault;
        _log = log;
        Vault = new VaultIdentity(vault.Name, vault.RootFolderPath, databaseName);
    }

    public VaultIdentity Vault { get; }

    public string CurrentUserName
    {
        get
        {
            try
            {
                var userManager = (IEdmUserMgr5)_vault;
                var user = userManager.GetLoggedInUser();
                return user?.Name ?? string.Empty;
            }
            catch (COMException exception)
            {
                _log.Warn("Oturum açmış PDM kullanıcısı okunamadı: " + PdmErrorTranslator.Describe(exception, _vault));
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Vault'ta tanımlı, kullanıcıya gösterilebilecek değişkenler.
    /// </summary>
    /// <remarks>
    /// SOLIDWORKS'ün iç değişkenleri (<c>_SW_</c> öneki) ve GUID adlı sistem kayıtları
    /// (<c>{</c> ile başlayanlar) listeden çıkarılır: bunlar kullanıcının düzenlemesi gereken
    /// alanlar değil ve çalışma kitabını okunamaz hâle getirirler.
    /// </remarks>
    public OperationOutcome<IReadOnlyList<PdmVariableDefinition>> GetVariables()
    {
        try
        {
            using var scope = new ComScope();

            if (_vault is not IEdmVault7 vault7)
            {
                return OperationOutcome<IReadOnlyList<PdmVariableDefinition>>.Failure(
                    IssueCode.PdmApiError, "IEdmVault7 desteklenmiyor.");
            }

            var manager = scope.Track((IEdmVariableMgr5)vault7.CreateUtility(EdmUtility.EdmUtil_VariableMgr));
            var position = scope.Track(manager.GetFirstVariablePosition());

            var variables = new List<PdmVariableDefinition>();

            while (!position.IsNull)
            {
                var variable = scope.Track(manager.GetNextVariable(position));
                if (variable is null)
                {
                    continue;
                }

                var name = variable.Name ?? string.Empty;

                if (name.Length == 0
                    || name.StartsWith("_SW_", StringComparison.Ordinal)
                    || name.StartsWith("{", StringComparison.Ordinal))
                {
                    continue;
                }

                var flags = (EdmVariableFlags)variable.Flags;

                variables.Add(new PdmVariableDefinition(
                    variable.ID,
                    name,
                    Translate(variable.VariableType),
                    isMandatory: flags.HasFlag(EdmVariableFlags.EdmVar_Mandatory),
                    isUnique: flags.HasFlag(EdmVariableFlags.EdmVar_Unique)));
            }

            variables.Sort((left, right) =>
                string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));

            _log.Info($"Vault değişkenleri okundu: {variables.Count} adet.");
            return OperationOutcome<IReadOnlyList<PdmVariableDefinition>>.Success(variables);
        }
        catch (COMException exception)
        {
            _log.Error("Vault değişkenleri okunamadı.", exception);
            return OperationOutcome<IReadOnlyList<PdmVariableDefinition>>.Failure(
                PdmErrorTranslator.Translate(exception),
                PdmErrorTranslator.Describe(exception, _vault));
        }
    }

    public OperationOutcome<string> GetFolderPath(int folderId)
    {
        try
        {
            using var scope = new ComScope();

            if (_vault.GetObject(EdmObjectType.EdmObject_Folder, folderId) is not IEdmFolder5 folder)
            {
                return OperationOutcome<string>.Failure(IssueCode.FolderNotFound, folderId.ToString());
            }

            scope.Track(folder);
            return OperationOutcome<string>.Success(ToRelativePath(folder.LocalPath, _vault.RootFolderPath));
        }
        catch (ArgumentException exception)
        {
            // Geçersiz bir klasör numarası (örn. 0) için PDM COM hatası DEĞİL,
            // ArgumentException fırlatıyor — gerçek vault'ta günlükle görüldü.
            _log.Warn($"Klasör {folderId} için geçersiz numara: {exception.Message}");
            return OperationOutcome<string>.Failure(IssueCode.FolderNotFound, folderId.ToString());
        }
        catch (COMException exception)
        {
            return OperationOutcome<string>.Failure(
                PdmErrorTranslator.Translate(exception),
                PdmErrorTranslator.Describe(exception, _vault));
        }
    }

    /// <summary>Yerel yolu vault köküne göre göreli hâle getirir.</summary>
    internal static string ToRelativePath(string? localPath, string? rootPath)
    {
        if (string.IsNullOrEmpty(localPath))
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(rootPath))
        {
            return localPath!;
        }

        var root = rootPath!.TrimEnd('\\');

        if (localPath!.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var relative = localPath.Substring(root.Length).TrimEnd('\\');
            return relative.Length == 0 ? "\\" : relative;
        }

        return localPath;
    }

    /// <summary>
    /// Interop enum'unu Core'un kendi enum'una çevirir.
    /// </summary>
    /// <remarks>
    /// Bu çeviri, Core'un interop'a bağımlı olmamasının bedeli — ve karşılığında Core, PDM
    /// istemcisi olmayan bir makinede derlenip test edilebiliyor. Değerler bilerek aynı
    /// sırada tutuldu ki olası bir kayma gözle yakalanabilsin.
    /// </remarks>
    internal static PdmVariableType Translate(EdmVariableType type) => type switch
    {
        EdmVariableType.EdmVarType_Text => PdmVariableType.Text,
        EdmVariableType.EdmVarType_Int => PdmVariableType.Int,
        EdmVariableType.EdmVarType_Float => PdmVariableType.Float,
        EdmVariableType.EdmVarType_Bool => PdmVariableType.Bool,
        EdmVariableType.EdmVarType_Date => PdmVariableType.Date,
        _ => PdmVariableType.Text,
    };
}
