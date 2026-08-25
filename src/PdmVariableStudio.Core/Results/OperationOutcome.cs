using System;
using System.Collections.Generic;

namespace PdmVariableStudio.Core.Results;

/// <summary>
/// Doğrulama boru hattının ürettiği tek bir bulgu.
/// </summary>
/// <remarks>
/// <see cref="Severity"/> ile <see cref="IssueCode"/> ayrı: aynı kod bağlama göre uyarı ya da
/// hata olabilir. Örneğin <see cref="IssueCode.ColumnRelocated"/> bir uyarıdır (uygulanabilir
/// ama kontrol edilmeli), <see cref="IssueCode.RowTampered"/> hatadır.
/// </remarks>
public sealed class ValidationIssue
{
    public ValidationIssue(IssueCode code, IssueSeverity severity, string? context = null, string? technicalDetail = null)
    {
        Code = code;
        Severity = severity;
        Context = context ?? string.Empty;
        TechnicalDetail = technicalDetail ?? string.Empty;
    }

    public IssueCode Code { get; }

    public IssueSeverity Severity { get; }

    /// <summary>Bulgunun nerede oluştuğu; örn. "satır 42" ya da "MIL-001.sldprt".</summary>
    public string Context { get; }

    /// <summary>Yalnızca günlüğe yazılan teknik ayrıntı. Kullanıcıya gösterilmez.</summary>
    public string TechnicalDetail { get; }

    public string Summary => IssueText.Summary(Code);

    public string Detail => IssueText.Detail(Code);

    public static ValidationIssue Error(IssueCode code, string? context = null, string? technical = null) =>
        new(code, IssueSeverity.Error, context, technical);

    public static ValidationIssue Warning(IssueCode code, string? context = null, string? technical = null) =>
        new(code, IssueSeverity.Warning, context, technical);

    /// <summary>Tüm işlemi durduran bulgu. Tek bir hücreyi değil, çalışma kitabının tamamını etkiler.</summary>
    public static ValidationIssue Fatal(IssueCode code, string? context = null, string? technical = null) =>
        new(code, IssueSeverity.Fatal, context, technical);

    public override string ToString() =>
        Context.Length > 0 ? $"[{Severity}] {Summary} ({Context})" : $"[{Severity}] {Summary}";
}

public enum IssueSeverity
{
    /// <summary>Uygulanabilir ama kullanıcı bilmeli.</summary>
    Warning = 0,

    /// <summary>Bu hücre/satır uygulanamaz; diğerleri etkilenmez.</summary>
    Error = 1,

    /// <summary>Çalışma kitabının tamamı reddedilir; hiçbir şey uygulanmaz.</summary>
    Fatal = 2,
}

/// <summary>
/// Değer döndüren bir işlemin sonucu. Beklenen hata durumları istisna fırlatmaz, bunu döner.
/// </summary>
/// <remarks>
/// İstisna yalnızca gerçekten beklenmeyen durumlar için ayrılmıştır. "Dosya başkası tarafından
/// çekili" beklenen bir durumdur ve akış kontrolü için istisna kullanmak hem pahalı hem de
/// çağıran tarafın durumu ele almasını kolay atlanır hâle getiriyor.
/// </remarks>
public sealed class OperationOutcome<T>
{
    private readonly T _value;

    private OperationOutcome(bool ok, T value, IssueCode code, string detail)
    {
        IsSuccess = ok;
        _value = value;
        Code = code;
        TechnicalDetail = detail ?? string.Empty;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public IssueCode Code { get; }

    public string TechnicalDetail { get; }

    /// <summary>Başarılı sonucun değeri. Başarısızsa erişim istisna atar.</summary>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException(
            $"Başarısız sonucun değeri okunamaz (kod: {Code}). Çağıran taraf IsSuccess kontrolünü atlamış.");

    /// <summary>Başarısızsa varsayılana düşer; akışı kesmez.</summary>
    public T ValueOr(T fallback) => IsSuccess ? _value : fallback;

    public static OperationOutcome<T> Success(T value) =>
        new(true, value, IssueCode.None, string.Empty);

    public static OperationOutcome<T> Failure(IssueCode code, string? technicalDetail = null) =>
        new(false, default!, code, technicalDetail ?? string.Empty);

    public string Summary => IsSuccess ? string.Empty : IssueText.Summary(Code);

    public string Detail => IsSuccess ? string.Empty : IssueText.Detail(Code);
}

/// <summary>Değer döndürmeyen işlemler için <see cref="OperationOutcome{T}"/> karşılığı.</summary>
public sealed class OperationOutcome
{
    private OperationOutcome(bool ok, IssueCode code, string detail)
    {
        IsSuccess = ok;
        Code = code;
        TechnicalDetail = detail ?? string.Empty;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public IssueCode Code { get; }

    public string TechnicalDetail { get; }

    public static OperationOutcome Success() => new(true, IssueCode.None, string.Empty);

    public static OperationOutcome Failure(IssueCode code, string? technicalDetail = null) =>
        new(false, code, technicalDetail ?? string.Empty);

    public string Summary => IsSuccess ? string.Empty : IssueText.Summary(Code);

    public string Detail => IsSuccess ? string.Empty : IssueText.Detail(Code);
}

/// <summary>Bir bulgu listesini biriktirmek için küçük yardımcı.</summary>
public sealed class IssueCollector
{
    private readonly List<ValidationIssue> _issues = new();

    public IReadOnlyList<ValidationIssue> Issues => _issues;

    public bool HasFatal
    {
        get
        {
            foreach (var issue in _issues)
            {
                if (issue.Severity == IssueSeverity.Fatal)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void Add(ValidationIssue issue) => _issues.Add(issue);

    public void Warn(IssueCode code, string? context = null, string? technical = null) =>
        _issues.Add(ValidationIssue.Warning(code, context, technical));

    public void Error(IssueCode code, string? context = null, string? technical = null) =>
        _issues.Add(ValidationIssue.Error(code, context, technical));

    public void Fatal(IssueCode code, string? context = null, string? technical = null) =>
        _issues.Add(ValidationIssue.Fatal(code, context, technical));
}
