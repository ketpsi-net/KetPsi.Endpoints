using System.Collections.ObjectModel;
using System.Globalization;
using KetPsi.Results;

namespace KetPsi.Endpoints.Results;

/// <summary>Shared, allocation-conscious helpers used by the factories and the domain mappers.</summary>
internal static class ApiResultErrorHelpers
{
    /// <summary>
    /// Returns the <see cref="ErrorType"/> name without going through <see cref="Enum.ToString()"/>
    /// (which allocates and uses reflection-backed name lookup).
    /// </summary>
    internal static string GetTitle(ErrorType type) => type switch
    {
        ErrorType.Validation => nameof(ErrorType.Validation),
        ErrorType.Unauthorized => nameof(ErrorType.Unauthorized),
        ErrorType.Forbidden => nameof(ErrorType.Forbidden),
        ErrorType.NotFound => nameof(ErrorType.NotFound),
        ErrorType.Conflict => nameof(ErrorType.Conflict),
        ErrorType.RuleViolation => nameof(ErrorType.RuleViolation),
        ErrorType.QuotaExceeded => nameof(ErrorType.QuotaExceeded),
        ErrorType.Canceled => nameof(ErrorType.Canceled),
        ErrorType.Failure => nameof(ErrorType.Failure),
        ErrorType.DependencyFailure => nameof(ErrorType.DependencyFailure),
        _ => ((int)type).ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Converts a domain metadata bag into the string-valued API metadata. Values are formatted with
    /// <see cref="CultureInfo.InvariantCulture"/> so the wire format does not depend on the server culture.
    /// </summary>
    internal static IReadOnlyDictionary<string, string?>? ToStringMetadata(IReadOnlyDictionary<string, object?>? source)
    {
        if (source is null)
            return null;

        var count = source.Count;
        if (count == 0)
            return ReadOnlyDictionary<string, string?>.Empty;

        var comparer = source is Dictionary<string, object?> dictionary ? dictionary.Comparer : null;
        var result = new Dictionary<string, string?>(count, comparer);
        foreach (var pair in source)
            result[pair.Key] = FormatInvariant(pair.Value);

        return result;
    }

    internal static string? FormatInvariant(object? value) => value switch
    {
        null => null,
        string s => s,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
