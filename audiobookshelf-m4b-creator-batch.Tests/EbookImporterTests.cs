using System.Xml.Linq;
using AudiobookshelfM4bCreator;

namespace AudiobookshelfM4bCreator.Tests;

public sealed class EbookImporterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("m4b-ebook-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private BookPlan PlanEbook(params string[] files)
    {
        string src = Path.Combine(_root, "01 Alpha");
        foreach (string file in files)
        {
            string path = Path.Combine(src, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Path.GetExtension(file));
        }
        var plan = BookPlanner.Plan(_root, [new SourceItem(src, BookKind.Ebook)], "Author", "Series", null);
        Assert.Empty(plan.Errors);
        return Assert.Single(plan.Books);
    }

    [Fact]
    public void BuildOpf_ContainsSeriesAndEscapesText()
    {
        XNamespace opf = "http://www.idpf.org/2007/opf";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";

        string xml = EbookImporter.BuildOpf("Tom & Jerry <1>", "A. Author", "Series \"X\"", 7);
        var doc = XDocument.Parse(xml);
        var metadata = doc.Root!.Element(opf + "metadata")!;
        Assert.Contains("<metadata ", xml); // unprefixed, Calibre style

        Assert.Equal("Tom & Jerry <1>", metadata.Element(dc + "title")!.Value);
        Assert.Equal("A. Author", metadata.Element(dc + "creator")!.Value);
        var metas = metadata.Elements(opf + "meta").ToDictionary(m => (string)m.Attribute("name")!, m => (string)m.Attribute("content")!);
        Assert.Equal("Series \"X\"", metas["calibre:series"]);
        Assert.Equal("7", metas["calibre:series_index"]);
    }

    [Fact]
    public void Import_CopiesBookOpfAndCover()
    {
        var book = PlanEbook("alpha.epub", "img/Front.JPG");

        EbookImporter.Import(book, "Author", "Series");

        Assert.Equal(".epub", File.ReadAllText(Path.Combine(book.OutputFolder, "Alpha.epub")));
        Assert.True(File.Exists(Path.Combine(book.OutputFolder, EbookImporter.OpfFileName)));
        Assert.True(File.Exists(Path.Combine(book.OutputFolder, "cover.jpg")));
        Assert.True(File.Exists(Path.Combine(book.Source.Path, "alpha.epub"))); // source untouched
    }

    [Fact]
    public void Import_FailureRemovesWrittenFilesButKeepsForeignOnes()
    {
        var book = PlanEbook("alpha.epub", "alpha.pdf");
        Directory.CreateDirectory(book.OutputFolder);
        File.WriteAllText(Path.Combine(book.OutputFolder, "Alpha.pdf"), "existing"); // second copy collides

        Assert.ThrowsAny<IOException>(() => EbookImporter.Import(book, "Author", "Series"));

        Assert.False(File.Exists(Path.Combine(book.OutputFolder, "Alpha.epub")));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(book.OutputFolder, "Alpha.pdf")));
    }
}
