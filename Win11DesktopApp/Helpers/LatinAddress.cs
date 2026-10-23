using System.Text;

namespace Win11DesktopApp.Helpers
{
    /// <summary>
    /// Writes Ukrainian/Russian Cyrillic in Latin. Czech and Polish letters stay as they are.
    /// </summary>
    public static class LatinAddress
    {
        public static string ToLatin(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            if (!ContainsCyrillic(value))
                return value.Trim();

            var sb = new StringBuilder(value.Length * 2);
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                var lower = char.ToLowerInvariant(ch);
                if (lower == 'з' && i + 1 < value.Length && char.ToLowerInvariant(value[i + 1]) == 'г')
                {
                    Append(sb, "zgh", char.IsUpper(ch));
                    i++;
                    continue;
                }

                var wordStart = i == 0 || !char.IsLetter(value[i - 1]);
                var piece = lower switch
                {
                    'а' => "a",
                    'б' => "b",
                    'в' => "v",
                    'г' => "h",
                    'ґ' => "g",
                    'д' => "d",
                    'е' => "e",
                    'ё' => "yo",
                    'є' => wordStart ? "ye" : "ie",
                    'ж' => "zh",
                    'з' => "z",
                    'и' => "y",
                    'і' => "i",
                    'ї' => wordStart ? "yi" : "i",
                    'й' => wordStart ? "y" : "i",
                    'к' => "k",
                    'л' => "l",
                    'м' => "m",
                    'н' => "n",
                    'о' => "o",
                    'п' => "p",
                    'р' => "r",
                    'с' => "s",
                    'т' => "t",
                    'у' => "u",
                    'ф' => "f",
                    'х' => "kh",
                    'ц' => "ts",
                    'ч' => "ch",
                    'ш' => "sh",
                    'щ' => "shch",
                    'ь' or 'ъ' or '’' or '\'' => "",
                    'ы' => "y",
                    'э' => "e",
                    'ю' => wordStart ? "yu" : "iu",
                    'я' => wordStart ? "ya" : "ia",
                    _ => null
                };

                if (piece == null)
                    sb.Append(ch);
                else
                    Append(sb, piece, char.IsUpper(ch));
            }

            return sb.ToString().Trim();
        }

        public static bool ContainsCyrillic(string value)
        {
            foreach (var ch in value)
            {
                if (ch is >= '\u0400' and <= '\u04FF')
                    return true;
            }

            return false;
        }

        private static void Append(StringBuilder sb, string piece, bool upper)
        {
            if (piece.Length == 0)
                return;

            if (upper)
                sb.Append(char.ToUpperInvariant(piece[0])).Append(piece, 1, piece.Length - 1);
            else
                sb.Append(piece);
        }
    }
}
