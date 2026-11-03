using AudiobookshelfM4bCreator;

namespace AudiobookshelfM4bCreator.Tests;

public sealed class BookPlannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("m4b-planner-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private SourceItem MakeFolder(string name, string? narratorFile = null)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        if (narratorFile is not null)
            File.WriteAllText(Path.Combine(dir, BookPlanner.NarratorFileName), narratorFile);
        return new SourceItem(dir, BookKind.Audiobook);
    }

    private SourceItem MakeEbookFolder(string name, params string[] files)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (string file in files)
            File.WriteAllText(Path.Combine(dir, file), "x");
        return new SourceItem(dir, BookKind.Ebook);
    }

    private SourceItem MakeEbookFile(string fileName)
    {
        string path = Path.Combine(_root, fileName);
        File.WriteAllText(path, "x");
        return new SourceItem(path, BookKind.Ebook);
    }

    [Theory]
    [InlineData("04 Kathy Reichs - Durch Mark und Bein", 4.0)]
    [InlineData("12 - Title", 12.0)]
    [InlineData("001_Title", 1.0)]
    [InlineData("Kathy Reichs - Title (Tempe Brennan 4)", null)]
    [InlineData("1984 - Title", null)]   // 4 digits look like a year, not a volume
    [InlineData("00 Title", 0.0)]          // prequels are volume 0
    [InlineData("3CD Title", null)]
    [InlineData("15.5 - Title", 15.5)]
    [InlineData("15,5 - Title", 15.5)]
    [InlineData("15 5 - Title", 15.5)]   // dot replaced by a space in folder names
    [InlineData("04 5 Freunde", 4.0)]      // no dash after the digit: part of the title
    public void ParseVolumeNumber(string folderName, double? expected) =>
        Assert.Equal((decimal?)expected, BookPlanner.ParseVolumeNumber(folderName));

    [Theory]
    [InlineData(7.0, "007", "7")]
    [InlineData(0.0, "000", "0")]
    [InlineData(15.5, "015.5", "15.5")]
    public void FormatVolume(double number, string folder, string plain)
    {
        Assert.Equal(folder, BookPlanner.FormatVolumeFolder((decimal)number));
        Assert.Equal(plain, BookPlanner.FormatVolume((decimal)number));
    }

    [Theory]
    [InlineData("A, B", "A, B")]
    [InlineData(" A ;B ,, C ", "A, B, C")]
    [InlineData("A\r\nB\n", "A, B")]
    [InlineData("   ", "")]
    public void NormalizeNarrators(string input, string expected) =>
        Assert.Equal(expected, BookPlanner.NormalizeNarrators(input));

    [Fact]
    public void Plan_UsesFolderNumbersAndNarratorFiles()
    {
        SourceItem[] folders =
        [
            MakeFolder("01 Kathy Reichs - Tote lügen nicht (Temperance Brennan 01)", "Ranja Bonalana\n"),
            MakeFolder("04 Kathy Reichs - Durch Mark und Bein (Temperance Brennan 04)", "Katharina Spiering"),
            MakeFolder("13 Kathy Reichs - Blut vergisst nicht (Temperance Brennan 13)"),
        ];

        var result = BookPlanner.Plan(_root, folders, "Kathy Reichs", "Temperance Brennan", "Hansi Jochmann");

        Assert.Empty(result.Errors);
        Assert.Collection(result.Books,
            b => { Assert.Equal(1, b.Number); Assert.Equal("Tote lügen nicht", b.Name); Assert.Equal("Ranja Bonalana", b.Narrators); },
            b => { Assert.Equal(4, b.Number); Assert.Equal("Durch Mark und Bein", b.Name); Assert.Equal("Katharina Spiering", b.Narrators); },
            b => { Assert.Equal(13, b.Number); Assert.Equal("Blut vergisst nicht", b.Name); Assert.Equal("Hansi Jochmann", b.Narrators); });

        var first = result.Books[0];
        Assert.Equal("Tote lügen nicht: Temperance Brennan 1", first.Title);
        Assert.Equal(Path.Combine(_root, "Audiobook", "Kathy Reichs", "Temperance Brennan", "Vol. 001 - Tote lügen nicht {Ranja Bonalana}"), first.OutputFolder);
        Assert.Equal(Path.Combine(first.OutputFolder, "Tote lügen nicht {Ranja Bonalana}.m4b"), first.OutputFile);
    }

    [Fact]
    public void Plan_WithoutFolderNumbers_NumbersByPosition()
    {
        SourceItem[] folders = [MakeFolder("Book Alpha"), MakeFolder("Book Beta")];

        var result = BookPlanner.Plan(_root, folders, "Author", "Series", "Narrator");

        Assert.Empty(result.Errors);
        Assert.Equal([1m, 2m], result.Books.Select(b => b.Number));
    }

    [Fact]
    public void Plan_NumbersAfterSharedPrefix_IncludingZeroAndHalfVolumes()
    {
        SourceItem[] items =
        [
            MakeEbookFolder("Reichs, Kathy - Tempe Brennan 00 - Tempe Brennans erster Fall - Kurzroman", "a.epub"),
            MakeEbookFolder("Reichs, Kathy - Tempe Brennan 02 - Knochenarbeit - Roman", "a.epub"),
            MakeEbookFolder("Reichs, Kathy - Tempe Brennan 15 5 - Fährte des Todes - Thriller", "a.epub"),
            MakeEbookFolder("Reichs, Kathy - Tempe Brennan 24 - Die Spur der Angst", "a.epub"),
        ];

        var result = BookPlanner.Plan(_root, items, "Kathy Reichs", "Temperance Brennan", null);

        Assert.Empty(result.Errors);
        Assert.Equal([0m, 2m, 15.5m, 24m], result.Books.Select(b => b.Number));
        Assert.Equal(["Tempe Brennans erster Fall", "Knochenarbeit", "Fährte des Todes", "Die Spur der Angst"],
            result.Books.Select(b => b.Name));
        Assert.Equal("Fährte des Todes: Temperance Brennan 15.5", result.Books[2].Title);
        Assert.Equal("Vol. 015.5 - Fährte des Todes", Path.GetFileName(result.Books[2].OutputFolder));
    }

    [Fact]
    public void Plan_MixedNumbering_IsAnError()
    {
        SourceItem[] folders = [MakeFolder("01 Alpha"), MakeFolder("Beta")];

        var result = BookPlanner.Plan(_root, folders, "Author", "Series", "Narrator");

        Assert.Single(result.Errors);
        Assert.Contains("\"Beta\"", result.Errors[0]);
    }

    [Fact]
    public void Plan_MissingNarrator_IsAnError()
    {
        SourceItem[] folders = [MakeFolder("01 Alpha", "Somebody"), MakeFolder("02 Beta")];

        var result = BookPlanner.Plan(_root, folders, "Author", "Series", defaultNarrators: null);

        Assert.Single(result.Errors);
        Assert.Contains("02 Beta", result.Errors[0]);
    }

    [Fact]
    public void Plan_EmptyNarratorFile_FallsBackToDefault()
    {
        SourceItem[] folders = [MakeFolder("01 Alpha", "  \n")];

        var result = BookPlanner.Plan(_root, folders, "Author", "Series", "Default");

        Assert.Equal("Default", Assert.Single(result.Books).Narrators);
    }

    [Fact]
    public void Plan_SameVolumeDifferentNarrators_CoexistButSameNarratorCollides()
    {
        // Same folder name (from two download locations) -> same title and volume.
        SourceItem[] ok = [MakeFolder("a/01 Alpha", "One"), MakeFolder("b/01 Alpha", "Two")];
        Assert.Empty(BookPlanner.Plan(_root, ok, "Author", "Series", null).Errors);

        SourceItem[] clash = [MakeFolder("c/01 Alpha", "One"), MakeFolder("d/01 Alpha", "One")];
        var result = BookPlanner.Plan(_root, clash, "Author", "Series", null);
        Assert.Contains(result.Errors, e => e.Contains("same file"));
    }

    [Fact]
    public void Plan_Ebooks_HaveNoNarratorAndCopyEveryFormat()
    {
        SourceItem[] items =
        [
            MakeEbookFolder("01 Kathy Reichs - Tote lügen nicht", "book.EPUB", "book.pdf", "cover.jpg"),
            MakeEbookFile("02 Kathy Reichs - Knochenarbeit.epub"),
        ];

        var result = BookPlanner.Plan(_root, items, "Kathy Reichs", "Temperance Brennan", defaultNarrators: null);

        Assert.Empty(result.Errors);
        var first = result.Books[0];
        Assert.Null(first.Narrators);
        Assert.Equal(Path.Combine(_root, "Book", "Kathy Reichs", "Temperance Brennan", "Vol. 001 - Tote lügen nicht"), first.OutputFolder);
        Assert.Equal(["Tote lügen nicht.epub", "Tote lügen nicht.pdf"], first.EbookCopies.Select(c => Path.GetFileName(c.Target)));

        var second = result.Books[1];
        Assert.Equal(2, second.Number);
        Assert.Equal("Knochenarbeit", second.Name);
        Assert.Equal(Path.Combine(second.OutputFolder, "Knochenarbeit.epub"), Assert.Single(second.OutputFiles));
    }

    [Fact]
    public void Plan_EbookWithTwoFilesOfSameFormat_IsAnError()
    {
        SourceItem[] items = [MakeEbookFolder("01 Alpha", "a.epub", "b.epub")];

        var result = BookPlanner.Plan(_root, items, "Author", "Series", null);

        Assert.Contains(result.Errors, e => e.Contains("same file") && e.Contains("01 Alpha"));
    }

    [Fact]
    public void Plan_EbookAndAudiobookOfSameVolume_Coexist()
    {
        SourceItem[] items = [MakeFolder("a/01 Alpha", "Reader"), MakeEbookFolder("b/01 Alpha", "x.epub")];

        var result = BookPlanner.Plan(_root, items, "Author", "Series", null);

        Assert.Empty(result.Errors);
        Assert.NotEqual(result.Books[0].OutputFolder, result.Books[1].OutputFolder);
    }
}
