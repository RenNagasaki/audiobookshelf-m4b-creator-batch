using AudiobookshelfM4bCreator;

namespace AudiobookshelfM4bCreator.Tests;

public sealed class BookPlannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("m4b-planner-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string MakeFolder(string name, string? narratorFile = null)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        if (narratorFile is not null)
            File.WriteAllText(Path.Combine(dir, BookPlanner.NarratorFileName), narratorFile);
        return dir;
    }

    [Theory]
    [InlineData("04 Kathy Reichs - Durch Mark und Bein", 4)]
    [InlineData("12 - Title", 12)]
    [InlineData("001_Title", 1)]
    [InlineData("Kathy Reichs - Title (Tempe Brennan 4)", null)]
    [InlineData("1984 - Title", null)]   // 4 digits look like a year, not a volume
    [InlineData("00 Title", null)]
    [InlineData("3CD Title", null)]
    public void ParseVolumeNumber(string folderName, int? expected) =>
        Assert.Equal(expected, BookPlanner.ParseVolumeNumber(folderName));

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
        string[] folders =
        [
            MakeFolder("01 Kathy Reichs - Tote lügen nicht (Temperance Brennan 01)", "Ranja Bonalana\n"),
            MakeFolder("04 Kathy Reichs - Durch Mark und Bein (Temperance Brennan 04)", "Katharina Spiering"),
            MakeFolder("13 Kathy Reichs - Blut vergisst nicht (Temperance Brennan 13)"),
        ];

        var result = BookPlanner.Plan(_root, folders, "Kathy Reichs", "Temperance Brennan", "Hansi Jochmann");

        Assert.Empty(result.Errors);
        Assert.Collection(result.Books,
            b => { Assert.Equal(1, b.Number); Assert.Equal("Tote lügen nicht", b.AudiobookName); Assert.Equal("Ranja Bonalana", b.Narrators); },
            b => { Assert.Equal(4, b.Number); Assert.Equal("Durch Mark und Bein", b.AudiobookName); Assert.Equal("Katharina Spiering", b.Narrators); },
            b => { Assert.Equal(13, b.Number); Assert.Equal("Blut vergisst nicht", b.AudiobookName); Assert.Equal("Hansi Jochmann", b.Narrators); });

        var first = result.Books[0];
        Assert.Equal("Tote lügen nicht: Temperance Brennan 1", first.Title);
        Assert.Equal(Path.Combine(_root, "Kathy Reichs", "Temperance Brennan", "Vol. 001 - Tote lügen nicht {Ranja Bonalana}"), first.OutputFolder);
        Assert.Equal(Path.Combine(first.OutputFolder, "Tote lügen nicht {Ranja Bonalana}.m4b"), first.OutputFile);
    }

    [Fact]
    public void Plan_WithoutFolderNumbers_NumbersByPosition()
    {
        string[] folders = [MakeFolder("Book Alpha"), MakeFolder("Book Beta")];

        var result = BookPlanner.Plan(_root, folders, "Author", "Series", "Narrator");

        Assert.Empty(result.Errors);
        Assert.Equal([1, 2], result.Books.Select(b => b.Number));
    }

    [Fact]
    public void Plan_MixedNumbering_IsAnError()
    {
        string[] folders = [MakeFolder("01 Alpha"), MakeFolder("Beta")];

        var result = BookPlanner.Plan(_root, folders, "Author", "Series", "Narrator");

        Assert.Single(result.Errors);
        Assert.Contains("\"Beta\"", result.Errors[0]);
    }

    [Fact]
    public void Plan_MissingNarrator_IsAnError()
    {
        string[] folders = [MakeFolder("01 Alpha", "Somebody"), MakeFolder("02 Beta")];

        var result = BookPlanner.Plan(_root, folders, "Author", "Series", defaultNarrators: null);

        Assert.Single(result.Errors);
        Assert.Contains("02 Beta", result.Errors[0]);
    }

    [Fact]
    public void Plan_EmptyNarratorFile_FallsBackToDefault()
    {
        string[] folders = [MakeFolder("01 Alpha", "  \n")];

        var result = BookPlanner.Plan(_root, folders, "Author", "Series", "Default");

        Assert.Equal("Default", Assert.Single(result.Books).Narrators);
    }

    [Fact]
    public void Plan_SameVolumeDifferentNarrators_CoexistButSameNarratorCollides()
    {
        // Same folder name (from two download locations) -> same title and volume.
        string[] ok = [MakeFolder("a/01 Alpha", "One"), MakeFolder("b/01 Alpha", "Two")];
        Assert.Empty(BookPlanner.Plan(_root, ok, "Author", "Series", null).Errors);

        string[] clash = [MakeFolder("c/01 Alpha", "One"), MakeFolder("d/01 Alpha", "One")];
        var result = BookPlanner.Plan(_root, clash, "Author", "Series", null);
        Assert.Contains(result.Errors, e => e.Contains("same file"));
    }
}
