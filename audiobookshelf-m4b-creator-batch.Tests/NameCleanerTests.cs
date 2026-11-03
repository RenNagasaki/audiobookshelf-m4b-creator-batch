using AudiobookshelfM4bCreator;

namespace AudiobookshelfM4bCreator.Tests;

public class NameCleanerTests
{
    [Theory]
    [InlineData("Series - Folge 3 - The Title [2015] (Remastered)", "The Title")]
    [InlineData("The Title 320 kbit Webrip MP3", "The Title")]
    [InlineData("14 Kathy Reichs - Fahr zur Hölle (Temperance Brennan 14)", "Fahr zur Hölle")]
    [InlineData("09 Kathy Reichs - Hals über Kopf (Temperance Brennan 09)", "Hals über Kopf")]
    [InlineData("01 Über den Wolken", "Über den Wolken")]
    [InlineData("02 - Knochenarbeit - Roman", "Knochenarbeit")]
    [InlineData("15 5 - Fährte des Todes - Thriller", "Fährte des Todes")]
    [InlineData("Der Roman", "Der Roman")]
    public void CleanAudiobookName_StripsNoise(string folder, string expected) =>
        Assert.Equal(expected, NameCleaner.CleanAudiobookName(folder, "Series", "Kathy Reichs", "", ""));

    [Fact]
    public void CleanAudiobookName_FallsBackToFolderNameWhenNothingRemains() =>
        Assert.Equal("Folge 3", NameCleaner.CleanAudiobookName("Folge 3", "Series", "Author", "", ""));

    [Fact]
    public void CommonPrefixAndSuffix_TrimToWordBoundary()
    {
        string[] names = ["Show - Alpha End", "Show - Beta End"];
        Assert.Equal("Show -", NameCleaner.FindCommonPrefix(names));
        Assert.Equal("End", NameCleaner.FindCommonSuffix(names));
    }

    [Fact]
    public void CommonPrefix_NeedsAtLeastTwoNames() =>
        Assert.Equal("", NameCleaner.FindCommonPrefix(["Only One"]));
}

public class NaturalStringComparerTests
{
    [Fact]
    public void SortsNumbersByValue()
    {
        string[] input = ["10 b", "2 a", "01 c", "1 d"];
        string[] sorted = input.OrderBy(s => s, NaturalStringComparer.Instance).ToArray();
        Assert.Equal(["01 c", "1 d", "2 a", "10 b"], sorted);
    }

    [Fact]
    public void IsCaseInsensitive() =>
        Assert.Equal(0, NaturalStringComparer.Instance.Compare("abc", "ABC"));
}
