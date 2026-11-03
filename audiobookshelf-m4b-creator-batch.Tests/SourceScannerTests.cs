using AudiobookshelfM4bCreator;

namespace AudiobookshelfM4bCreator.Tests;

public sealed class SourceScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("m4b-scanner-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Touch(string relativePath)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    [Fact]
    public void Scan_ClassifiesFoldersAndLooseEbooks()
    {
        Touch("02 Audio/CD1/01.mp3");
        Touch("02 Audio/book.epub");          // MP3s win: still an audiobook
        Touch("01 Ebook/sub/book.azw3");
        Touch("03 Loose.epub");
        Touch("notes.txt");                   // loose non-ebook file: not a book
        Touch("Empty/readme.txt");            // folder without books: ignored
        Touch("Done/old/old.mp3");
        Touch("Author/Series/x.m4b");
        Touch("Audiobook/Author/Series/Vol. 001 - X/x.mp3");
        Touch("Book/Author/Series/Vol. 001 - X/x.epub");
        Touch("Vol. 001 - Old/old.epub");
        Touch("Vol. 015.5 - Half/half.epub");

        var result = SourceScanner.Scan(_root, "Author");

        Assert.Collection(result.Items,
            i => { Assert.Equal("01 Ebook", i.Name); Assert.Equal(BookKind.Ebook, i.Kind); Assert.False(i.IsFile); },
            i => { Assert.Equal("02 Audio", i.Name); Assert.Equal(BookKind.Audiobook, i.Kind); },
            i => { Assert.Equal("03 Loose", i.Name); Assert.Equal(BookKind.Ebook, i.Kind); Assert.True(i.IsFile); });
        Assert.Equal(["Empty"], result.Ignored);
    }

    [Fact]
    public void MoveToDone_MovesFilesAndFolders()
    {
        Touch("01 Folder/book.epub");
        Touch("02 File.pdf");
        var folder = new SourceItem(Path.Combine(_root, "01 Folder"), BookKind.Ebook);
        var file = new SourceItem(Path.Combine(_root, "02 File.pdf"), BookKind.Ebook);

        SourceScanner.MoveToDone(_root, folder);
        SourceScanner.MoveToDone(_root, file);

        Assert.True(File.Exists(Path.Combine(_root, "Done", "01 Folder", "book.epub")));
        Assert.True(File.Exists(Path.Combine(_root, "Done", "02 File.pdf")));
        Assert.True(file.IsFile); // captured before the move
    }
}
