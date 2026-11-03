using System.Globalization;
using System.Text.RegularExpressions;

namespace AudiobookshelfM4bCreator;

/// <summary>A source file and where it is copied to (ebooks only).</summary>
public sealed record FileCopy(string Source, string Target);

/// <summary>Everything decided about one book before any conversion or copying starts.</summary>
/// <param name="Narrators">Audiobooks only; null for ebooks.</param>
/// <param name="Title">Title tag of the M4B: <c>&lt;Name&gt;: &lt;Series&gt; &lt;N&gt;</c>.</param>
/// <param name="OutputFile">The M4B (audiobook) or the first copied ebook file. Exists → book is skipped.</param>
/// <param name="EbookCopies">Ebook files to copy; empty for audiobooks.</param>
public sealed record BookPlan(
    SourceItem Source,
    decimal Number,
    string Name,
    string? Narrators,
    string Title,
    string OutputFolder,
    string OutputFile,
    IReadOnlyList<FileCopy> EbookCopies)
{
    public BookKind Kind => Source.Kind;

    public IEnumerable<string> OutputFiles =>
        Kind == BookKind.Ebook ? EbookCopies.Select(c => c.Target) : [OutputFile];
}

public sealed record PlanResult(IReadOnlyList<BookPlan> Books, IReadOnlyList<string> Errors);

/// <summary>
/// Turns the source items of one run into <see cref="BookPlan"/>s: volume number,
/// narrators (audiobooks: per-folder <c>narrator.txt</c> or the run default), title and output paths.
/// </summary>
public static class BookPlanner
{
    public const string NarratorFileName = "narrator.txt";

    /// <summary>Output roots below the parent folder: audiobooks and ebooks go to separate Audiobookshelf libraries.</summary>
    public const string AudiobookOutputFolder = "Audiobook";
    public const string EbookOutputFolder = "Book";

    public static string OutputRoot(string parentFolder, BookKind kind) =>
        Path.Combine(parentFolder, kind == BookKind.Ebook ? EbookOutputFolder : AudiobookOutputFolder);

    /// <summary>Splits "A, B; C" into a canonical "A, B, C".</summary>
    public static string NormalizeNarrators(string input) =>
        string.Join(", ", Regex.Split(input.Trim(), @"\s*[,;\r\n]+\s*").Where(n => n.Length > 0));

    /// <summary>
    /// Leading 1-3 digit number followed by a separator, e.g. "04 Title", "12 - Title", "00 Prequel".
    /// Half volumes: "15.5 - Title", "15,5 - Title", or "15 5 - Title" (single digit, then a dash).
    /// </summary>
    public static decimal? ParseVolumeNumber(string folderName)
    {
        var m = Regex.Match(folderName, @"^(\d{1,3})(?:[.,](\d)|\s(\d)(?=\s*[-–—]))?(?=[\s._\-–—])");
        if (!m.Success) return null;
        decimal number = int.Parse(m.Groups[1].Value);
        string fraction = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
        return fraction.Length > 0 ? number + int.Parse(fraction) / 10m : number;
    }

    /// <summary>Volume as used in folder names: "007", "015.5".</summary>
    public static string FormatVolumeFolder(decimal number) =>
        ((int)number).ToString("D3", CultureInfo.InvariantCulture) +
        (number % 1 == 0 ? "" : (number % 1).ToString("0.#", CultureInfo.InvariantCulture)[1..]);

    /// <summary>Volume as used in titles and metadata: "7", "15.5".</summary>
    public static string FormatVolume(decimal number) => number.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>
    /// Volume numbers of all names: from the start of the name, or — when the names share a prefix
    /// such as "Reichs, Kathy - Tempe Brennan " — right after it. Null entries: no number found.
    /// </summary>
    private static decimal?[] ParseVolumeNumbers(string[] names, string commonPrefix)
    {
        decimal?[] parsed = names.Select(ParseVolumeNumber).ToArray();
        if (parsed.All(n => n.HasValue) || commonPrefix.Length == 0) return parsed;

        decimal?[] afterPrefix = names
            .Select(n => ParseVolumeNumber(n[commonPrefix.Length..].TrimStart(' ', '-', '–', '—', '_', '.')))
            .ToArray();
        return afterPrefix.Count(n => n.HasValue) > parsed.Count(n => n.HasValue) ? afterPrefix : parsed;
    }

    /// <summary>Narrators from the folder's narrator.txt, or null if absent/empty.</summary>
    public static string? ReadNarratorFile(string folder)
    {
        string path = Path.Combine(folder, NarratorFileName);
        if (!File.Exists(path)) return null;
        string normalized = NormalizeNarrators(File.ReadAllText(path));
        return normalized.Length > 0 ? normalized : null;
    }

    public static PlanResult Plan(string parentFolder, IReadOnlyList<SourceItem> sources,
        string author, string seriesName, string? defaultNarrators)
    {
        var errors = new List<string>();
        string[] names = sources.Select(s => s.Name).ToArray();
        string commonPrefix = NameCleaner.FindCommonPrefix(names);
        string commonSuffix = NameCleaner.FindCommonSuffix(names);

        // Volume numbers: all from names, or all from position. A mix is ambiguous.
        decimal?[] parsed = ParseVolumeNumbers(names, commonPrefix);
        bool useNameNumbers = parsed.All(n => n.HasValue);
        if (!useNameNumbers && parsed.Any(n => n.HasValue))
            errors.Add("Some folders/files start with a volume number and some don't: " +
                       string.Join(", ", names.Where((_, i) => !parsed[i].HasValue).Select(n => $"\"{n}\"")) +
                       " — number all or none.");

        var books = new List<BookPlan>();
        for (int i = 0; i < sources.Count; i++)
        {
            SourceItem source = sources[i];
            decimal number = useNameNumbers ? parsed[i]!.Value : i + 1;
            string volumeFolder = FormatVolumeFolder(number);
            string name = NameCleaner.CleanAudiobookName(names[i], seriesName, author, commonPrefix, commonSuffix);
            string title = $"{name}: {seriesName} {FormatVolume(number)}";
            string seriesFolder = Path.Combine(OutputRoot(parentFolder, source.Kind), author, seriesName);

            if (source.Kind == BookKind.Ebook)
            {
                // Ebooks have no narrator; folder and files carry the plain name.
                string outputFolder = Path.Combine(seriesFolder, $"Vol. {volumeFolder} - {name}");
                var copies = SourceScanner.CollectEbookFiles(source)
                    .Select(f => new FileCopy(f, Path.Combine(outputFolder, name + Path.GetExtension(f).ToLowerInvariant())))
                    .ToArray();
                books.Add(new BookPlan(source, number, name, null, title, outputFolder, copies[0].Target, copies));
                continue;
            }

            string? narrators = ReadNarratorFile(source.Path) ?? defaultNarrators;
            if (string.IsNullOrWhiteSpace(narrators))
            {
                errors.Add($"No narrator for \"{names[i]}\": add a {NarratorFileName} or enter a default narrator.");
                continue;
            }

            string displayName = $"{name} {{{narrators}}}";
            string audioFolder = Path.Combine(seriesFolder, $"Vol. {volumeFolder} - {displayName}");
            books.Add(new BookPlan(source, number, name, narrators, title, audioFolder,
                Path.Combine(audioFolder, $"{displayName}.m4b"), []));
        }

        var outputs = books.SelectMany(b => b.OutputFiles.Select(f => (File: f, Book: b)));
        foreach (var dup in outputs.GroupBy(o => o.File, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Several sources would produce the same file \"{Path.GetFileName(dup.Key)}\": " +
                       string.Join(", ", dup.Select(o => $"\"{o.Book.Source.Name}\"").Distinct()));

        return new PlanResult(books, errors);
    }
}
