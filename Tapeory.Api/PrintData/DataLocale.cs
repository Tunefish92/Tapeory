using System.Globalization;

namespace Tapeory.Api.PrintData;

/// <summary>How numbers and region-dependent dates from Excel are written for a reader's
/// language. A small table of its own, because the desktop app's engine runs without the
/// system's culture data.</summary>
public static class DataLocale
{
    public static CultureInfo For(string? locale)
    {
        var name = (locale ?? string.Empty).Trim().Replace('_', '-').ToLowerInvariant();
        var language = name.Split('-')[0];

        var (shortDate, decimalSeparator, groupSeparator) = language switch
        {
            "de" => ("dd.MM.yyyy", ",", "."),
            "fr" => ("dd/MM/yyyy", ",", " "),
            "es" or "it" => ("dd/MM/yyyy", ",", "."),
            "en" when name is not ("en" or "en-us") => ("dd/MM/yyyy", ".", ","),
            _ => ("M/d/yyyy", ".", ",")
        };

        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.ShortDatePattern = shortDate;
        culture.DateTimeFormat.DateSeparator = shortDate.Contains('.') ? "." : "/";
        culture.NumberFormat.NumberDecimalSeparator = decimalSeparator;
        culture.NumberFormat.NumberGroupSeparator = groupSeparator;
        culture.NumberFormat.CurrencyDecimalSeparator = decimalSeparator;
        culture.NumberFormat.CurrencyGroupSeparator = groupSeparator;
        culture.NumberFormat.PercentDecimalSeparator = decimalSeparator;
        culture.NumberFormat.PercentGroupSeparator = groupSeparator;
        return culture;
    }
}
