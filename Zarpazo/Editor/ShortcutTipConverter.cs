using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;

namespace Zarpazo.Editor;

/// <summary>
/// Splits a tooltip such as "Texto (T). Enter confirma" into its name, its key and the rest, so the bars can show the key as a
/// small key cap. A tooltip that doesn't end its name with a short "(key)" is shown whole, as the name. The parameter says which
/// part: title, key, detail, or keyShown and detailShown for the visibility of those two.
/// </summary>
internal sealed partial class ShortcutTipConverter : IValueConverter
{
    [GeneratedRegex(@"^(?<title>.+?) \((?<key>[^()]{1,12})\)(?:[.:]\s*(?<detail>.+))?$", RegexOptions.Singleline)]
    private static partial Regex Pattern();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string text = value as string ?? "";
        Match match = Pattern().Match(text);
        string title = match.Success ? match.Groups["title"].Value : text;
        string? key = match.Success ? match.Groups["key"].Value : null;
        string? detail = match.Success && match.Groups["detail"].Success ? match.Groups["detail"].Value : null;
        return parameter as string switch
        {
            "title" => title,
            "key" => key,
            "detail" => detail,
            "keyShown" => key == null ? Visibility.Collapsed : Visibility.Visible,
            "detailShown" => detail == null ? Visibility.Collapsed : Visibility.Visible,
            _ => text,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
