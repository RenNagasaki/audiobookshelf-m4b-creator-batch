using System.Globalization;
using System.Security;
using System.Text;

namespace AudiobookshelfM4bCreator;

/// <summary>
/// Puts an ebook into its Audiobookshelf folder: copies the ebook file(s) unchanged, writes a
/// <c>metadata.opf</c> (title, author, series, volume — Audiobookshelf reads it) and copies a cover image.
/// </summary>
public static class EbookImporter
{
    public const string OpfFileName = "metadata.opf";

    /// <summary>Calibre-style OPF (unprefixed <c>metadata</c>/<c>meta</c>, as Audiobookshelf expects).</summary>
    public static string BuildOpf(string title, string author, string series, decimal number) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="2.0">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
            <dc:title>{Escape(title)}</dc:title>
            <dc:creator opf:role="aut">{Escape(author)}</dc:creator>
            <meta name="calibre:series" content="{Escape(series)}" />
            <meta name="calibre:series_index" content="{BookPlanner.FormatVolume(number)}" />
          </metadata>
        </package>

        """;

    private static string Escape(string text) => SecurityElement.Escape(text);

    /// <summary>
    /// Copies the book into <see cref="BookPlan.OutputFolder"/>. Never overwrites; on failure every
    /// file written so far is removed again, so a re-run does not skip a half-imported book.
    /// </summary>
    public static void Import(BookPlan book, string author, string series)
    {
        if (book.Kind != BookKind.Ebook)
            throw new ArgumentException("Not an ebook plan.", nameof(book));

        var written = new List<string>();
        try
        {
            Directory.CreateDirectory(book.OutputFolder);
            foreach (var copy in book.EbookCopies)
            {
                File.Copy(copy.Source, copy.Target, overwrite: false);
                written.Add(copy.Target);
            }

            string opfPath = Path.Combine(book.OutputFolder, OpfFileName);
            File.WriteAllText(opfPath, BuildOpf(book.Name, author, series, book.Number), new UTF8Encoding(false));
            written.Add(opfPath);

            string? cover = book.Source.IsFile ? null : SourceScanner.FindCoverImage(book.Source.Path);
            if (cover is not null)
            {
                string coverTarget = Path.Combine(book.OutputFolder, "cover" + Path.GetExtension(cover).ToLowerInvariant());
                File.Copy(cover, coverTarget, overwrite: false);
                written.Add(coverTarget);
            }
        }
        catch
        {
            foreach (string file in written)
                File.Delete(file);
            throw;
        }
    }
}
