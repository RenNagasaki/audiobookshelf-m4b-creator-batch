using System.Text.RegularExpressions;

namespace AudiobookshelfM4bCreator;

public enum BookKind { Audiobook, Ebook }

/// <summary>One book to process: a folder (audiobook or ebook) or a loose ebook file.</summary>
public sealed record SourceItem(string Path, BookKind Kind)
{
    // Captured once: after MoveToDone the path no longer exists.
    public bool IsFile { get; } = File.Exists(Path);

    /// <summary>The name the title is derived from: folder name, or file name without extension.</summary>
    public string Name => IsFile
        ? System.IO.Path.GetFileNameWithoutExtension(Path)
        : System.IO.Path.GetFileName(Path)!;
}

public sealed record ScanResult(IReadOnlyList<SourceItem> Items, IReadOnlyList<string> Ignored);

/// <summary>Finds the books in a series folder and decides whether each is an audiobook or an ebook.</summary>
public static class SourceScanner
{
    public static readonly string[] EbookExtensions = [".epub", ".pdf", ".mobi", ".azw3", ".azw", ".fb2", ".cbz", ".cbr"];

    public static bool IsEbookFile(string path) =>
        EbookExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Subfolders with MP3s are audiobooks, subfolders with only ebook files are ebooks, loose ebook
    /// files in <paramref name="parentFolder"/> are ebooks too. Output folders (<c>Vol. NNN - …</c>,
    /// <c>Done/</c>, <c>Audiobook/</c>, <c>Book/</c>, legacy <c>&lt;Author&gt;/</c>) are skipped; other folders end up in <see cref="ScanResult.Ignored"/>.
    /// </summary>
    public static ScanResult Scan(string parentFolder, string author)
    {
        var items = new List<SourceItem>();
        var ignored = new List<string>();

        foreach (string dir in Directory.GetDirectories(parentFolder))
        {
            string name = Path.GetFileName(dir)!;
            if (Regex.IsMatch(name, @"^Vol\.\s\d{3}(\.\d)?\s-\s")
                || string.Equals(name, "Done", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, BookPlanner.AudiobookOutputFolder, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, BookPlanner.EbookOutputFolder, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, author, StringComparison.OrdinalIgnoreCase))  // output of runs before 1.2.0
                continue;

            if (Directory.EnumerateFiles(dir, "*.mp3", SearchOption.AllDirectories).Any())
                items.Add(new SourceItem(dir, BookKind.Audiobook));
            else if (Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any(IsEbookFile))
                items.Add(new SourceItem(dir, BookKind.Ebook));
            else
                ignored.Add(name);
        }

        items.AddRange(Directory.GetFiles(parentFolder).Where(IsEbookFile).Select(f => new SourceItem(f, BookKind.Ebook)));

        return new ScanResult(
            items.OrderBy(i => i.Name, NaturalStringComparer.Instance).ToArray(),
            ignored);
    }

    /// <summary>First .jpg/.jpeg/.png in the folder, else in its subfolders (natural order); null if none.</summary>
    public static string? FindCoverImage(string folder)
    {
        foreach (string ext in new[] { "*.jpg", "*.jpeg", "*.png" })
        {
            string? image = Directory.GetFiles(folder, ext).FirstOrDefault();
            if (image is not null)
                return image;
        }

        return Directory.GetDirectories(folder)
            .OrderBy(d => d, NaturalStringComparer.Instance)
            .Select(FindCoverImage)
            .FirstOrDefault(image => image is not null);
    }

    /// <summary>Moves a finished source (folder or loose file) into <c>&lt;parent&gt;/Done/</c>.</summary>
    public static string MoveToDone(string parentFolder, SourceItem item)
    {
        string doneDir = Path.Combine(parentFolder, "Done");
        Directory.CreateDirectory(doneDir);
        string target = Path.Combine(doneDir, Path.GetFileName(item.Path)!);
        if (item.IsFile)
            File.Move(item.Path, target);
        else
            Directory.Move(item.Path, target);
        return target;
    }

    /// <summary>All ebook files of an item, naturally sorted (a loose file is its own only file).</summary>
    public static string[] CollectEbookFiles(SourceItem item) =>
        item.IsFile
            ? [item.Path]
            : Directory.EnumerateFiles(item.Path, "*", SearchOption.AllDirectories)
                .Where(IsEbookFile)
                .OrderBy(f => f, NaturalStringComparer.Instance)
                .ToArray();
}
