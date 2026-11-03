using System.Text.RegularExpressions;

namespace AudiobookshelfM4bCreator;

/// <summary>Turns messy download folder names into clean audiobook titles.</summary>
public static class NameCleaner
{
    public static string FindCommonPrefix(string[] names)
    {
        if (names.Length < 2) return "";
        string first = names[0];
        int len = 0;
        for (int i = 0; i < first.Length; i++)
        {
            char c = char.ToUpperInvariant(first[i]);
            if (names.All(n => i < n.Length && char.ToUpperInvariant(n[i]) == c))
                len = i + 1;
            else
                break;
        }
        // Trim to last word boundary to avoid cutting mid-word
        string prefix = first[..len];
        int lastSpace = prefix.LastIndexOfAny([' ', '-', '_', '.']);
        return lastSpace > 0 ? prefix[..lastSpace] : "";
    }

    public static string FindCommonSuffix(string[] names)
    {
        if (names.Length < 2) return "";
        string first = names[0];
        int len = 0;
        for (int i = 1; i <= first.Length; i++)
        {
            char c = char.ToUpperInvariant(first[^i]);
            if (names.All(n => i <= n.Length && char.ToUpperInvariant(n[^i]) == c))
                len = i;
            else
                break;
        }
        // Trim to last word boundary to avoid cutting mid-word
        string suffix = first[^len..];
        int firstSpace = suffix.IndexOfAny([' ', '-', '_', '.']);
        return firstSpace >= 0 ? suffix[(firstSpace + 1)..] : "";
    }

    public static string CleanAudiobookName(string folderName, string seriesName, string author,
        string commonPrefix, string commonSuffix)
    {
        // Normalize underscores and multiple dashes/hyphens to spaces
        string cleaned = Regex.Replace(folderName, @"[_]+", " ");
        cleaned = Regex.Replace(cleaned, @"[-–—]{1,}", " - ");

        // Remove common prefix/suffix shared by all folders
        string normalizedPrefix = Regex.Replace(commonPrefix, @"[_]+", " ");
        normalizedPrefix = Regex.Replace(normalizedPrefix, @"[-–—]{1,}", " - ");
        string normalizedSuffix = Regex.Replace(commonSuffix, @"[_]+", " ");
        normalizedSuffix = Regex.Replace(normalizedSuffix, @"[-–—]{1,}", " - ");
        if (!string.IsNullOrWhiteSpace(normalizedPrefix))
            cleaned = Regex.Replace(cleaned, "^" + Regex.Escape(normalizedPrefix), "", RegexOptions.IgnoreCase).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSuffix))
            cleaned = Regex.Replace(cleaned, Regex.Escape(normalizedSuffix) + "$", "", RegexOptions.IgnoreCase).Trim();

        // Remove series name and author (also normalized for matching)
        string normalizedSeries = Regex.Replace(seriesName, @"[_]+", " ");
        string normalizedAuthor = Regex.Replace(author, @"[_]+", " ");
        cleaned = Regex.Replace(cleaned, Regex.Escape(normalizedSeries), "", RegexOptions.IgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, Regex.Escape(normalizedAuthor), "", RegexOptions.IgnoreCase).Trim();

        // Remove anything in square brackets or parentheses like [1980], (2013), [FLAC], etc.
        cleaned = Regex.Replace(cleaned, @"\[.*?\]", "").Trim();
        cleaned = Regex.Replace(cleaned, @"\(.*?\)", "").Trim();

        // Remove episode/Folge labels (German and English) with their number
        cleaned = Regex.Replace(cleaned, @"\b(Folge|Episode|Ep\.?|Part|Teil|Chapter|Kapitel|Nr\.?|No\.?|Volume|Vol\.?|Band|Heft)\s*\d*\b", "", RegexOptions.IgnoreCase).Trim();

        // Remove bitrate patterns like "320 kbit", "128kbps", "192 kbit/s"
        cleaned = Regex.Replace(cleaned, @"\b\d+\s*kbit(/s)?\b", "", RegexOptions.IgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, @"\b\d+\s*kbps\b", "", RegexOptions.IgnoreCase).Trim();

        // Remove common source/quality tags and release group tags
        cleaned = Regex.Replace(cleaned, @"\b(TbT|Webrip|WEB-DL|WEB|WEBRip|MP3|FLAC|AAC|OGG|CBR|VBR|Lossless|Lossy|HQ|LQ|Remastered|Repack|Proper|VOiCE|SAM|BIGFN|CD[\s-]?Rip|Rip)\b", "", RegexOptions.IgnoreCase).Trim();

        // Remove disc/CD labels like "3CD", "CD1", "2CD", "Disc 1"
        cleaned = Regex.Replace(cleaned, @"\b\d*\s*CDs?\s*\d*\b", "", RegexOptions.IgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, @"\bDisc\s*\d*\b", "", RegexOptions.IgnoreCase).Trim();

        // Remove language codes like "DE", "EN", "GER", "ENG"
        cleaned = Regex.Replace(cleaned, @"\b(DE|EN|GER|ENG|FR|FRA|ES|IT|NL|PT)\b", "").Trim();

        // Remove standalone 4-digit years like "2015"
        cleaned = Regex.Replace(cleaned, @"\b(19|20)\d{2}\b", "").Trim();

        // Strip leftover leading/trailing dashes, whitespace, and common separators
        cleaned = Regex.Replace(cleaned, @"^[\s\-–—_.,;:]+", "");
        cleaned = Regex.Replace(cleaned, @"[\s\-–—_.,;:]+$", "");

        // Strip a trailing genre label after a dash, as ebook releases add it: "Knochenarbeit - Roman"
        cleaned = Regex.Replace(cleaned, @"\s+-\s+(Roman|Kurzroman|Thriller|Psychothriller|Krimi|Kriminalroman|Erzählung|Novelle|Kurzgeschichte|A Novel|Novel|Novella)$", "", RegexOptions.IgnoreCase).Trim();

        // Strip leading episode/Folge labels that survived earlier cleaning (with or without number)
        cleaned = Regex.Replace(cleaned, @"^(Folge|Episode|Ep\.?|Part|Teil|Chapter|Kapitel|Nr\.?|No\.?|Volume|Vol\.?|Band|Heft)\s*\d*\s*", "", RegexOptions.IgnoreCase).Trim();

        // Strip leftover separators again after episode label removal
        cleaned = Regex.Replace(cleaned, @"^[\s\-–—_.,;:]+", "");

        // Strip leading digits and separators so the name starts with a letter
        cleaned = Regex.Replace(cleaned, @"^[\d\s\-–—_.,;:]+", "");

        // Collapse multiple spaces
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();

        // If nothing meaningful remains, fall back to original
        if (string.IsNullOrWhiteSpace(cleaned) || !char.IsLetter(cleaned[0]))
            return folderName;

        return cleaned;
    }
}
