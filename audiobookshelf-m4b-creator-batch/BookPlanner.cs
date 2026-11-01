using System.Text.RegularExpressions;

namespace AudiobookshelfM4bCreator;

/// <summary>Everything decided about one audiobook folder before any conversion starts.</summary>
public sealed record BookPlan(
    string SourceFolder,
    int Number,
    string AudiobookName,
    string Narrators,
    string Title,
    string OutputFolder,
    string OutputFile);

public sealed record PlanResult(IReadOnlyList<BookPlan> Books, IReadOnlyList<string> Errors);

/// <summary>
/// Turns the source folders of one run into <see cref="BookPlan"/>s: volume number,
/// narrators (per-folder <c>narrator.txt</c> or the run default), title and output paths.
/// </summary>
public static class BookPlanner
{
    public const string NarratorFileName = "narrator.txt";

    /// <summary>Splits "A, B; C" into a canonical "A, B, C".</summary>
    public static string NormalizeNarrators(string input) =>
        string.Join(", ", Regex.Split(input.Trim(), @"\s*[,;\r\n]+\s*").Where(n => n.Length > 0));

    /// <summary>Leading 1-3 digit number followed by a separator, e.g. "04 Title" or "12 - Title".</summary>
    public static int? ParseVolumeNumber(string folderName)
    {
        var m = Regex.Match(folderName, @"^(\d{1,3})(?=[\s._\-–—])");
        return m.Success && int.Parse(m.Groups[1].Value) > 0 ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>Narrators from the folder's narrator.txt, or null if absent/empty.</summary>
    public static string? ReadNarratorFile(string folder)
    {
        string path = Path.Combine(folder, NarratorFileName);
        if (!File.Exists(path)) return null;
        string normalized = NormalizeNarrators(File.ReadAllText(path));
        return normalized.Length > 0 ? normalized : null;
    }

    public static PlanResult Plan(string parentFolder, IReadOnlyList<string> sourceFolders,
        string author, string seriesName, string? defaultNarrators)
    {
        var errors = new List<string>();
        string[] folderNames = sourceFolders.Select(d => Path.GetFileName(d)!).ToArray();
        string commonPrefix = NameCleaner.FindCommonPrefix(folderNames);
        string commonSuffix = NameCleaner.FindCommonSuffix(folderNames);

        // Volume numbers: all from folder names, or all from position. A mix is ambiguous.
        int?[] parsed = folderNames.Select(ParseVolumeNumber).ToArray();
        bool useFolderNumbers = parsed.All(n => n.HasValue);
        if (!useFolderNumbers && parsed.Any(n => n.HasValue))
            errors.Add("Some folders start with a volume number and some don't: " +
                       string.Join(", ", folderNames.Where((_, i) => !parsed[i].HasValue).Select(n => $"\"{n}\"")) +
                       " — number all folders or none.");

        var books = new List<BookPlan>();
        for (int i = 0; i < sourceFolders.Count; i++)
        {
            string folder = sourceFolders[i];
            string folderName = folderNames[i];
            int number = useFolderNumbers ? parsed[i]!.Value : i + 1;

            string? narrators = ReadNarratorFile(folder) ?? defaultNarrators;
            if (string.IsNullOrWhiteSpace(narrators))
            {
                errors.Add($"No narrator for \"{folderName}\": add a {NarratorFileName} or enter a default narrator.");
                continue;
            }

            string audiobookName = NameCleaner.CleanAudiobookName(folderName, seriesName, author, commonPrefix, commonSuffix);
            string displayName = $"{audiobookName} {{{narrators}}}";
            string outputFolder = Path.Combine(parentFolder, author, seriesName, $"Vol. {number:D3} - {displayName}");
            books.Add(new BookPlan(
                folder, number, audiobookName, narrators,
                Title: $"{audiobookName}: {seriesName} {number}",
                OutputFolder: outputFolder,
                OutputFile: Path.Combine(outputFolder, $"{displayName}.m4b")));
        }

        foreach (var dup in books.GroupBy(b => b.OutputFile, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Several folders would produce the same file \"{Path.GetFileName(dup.Key)}\": " +
                       string.Join(", ", dup.Select(b => $"\"{Path.GetFileName(b.SourceFolder)}\"")));

        return new PlanResult(books, errors);
    }
}
