using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using AudiobookshelfM4bCreator;

Console.OutputEncoding = Encoding.UTF8;

string parentFolder = GetParentFolder(args);

Console.Write("Author: ");
string author = Console.ReadLine()?.Trim() ?? "";
if (string.IsNullOrWhiteSpace(author))
{
    Console.Error.WriteLine("Author is required.");
    return 1;
}

Console.Write("Series name: ");
string seriesName = Console.ReadLine()?.Trim() ?? "";
if (string.IsNullOrWhiteSpace(seriesName))
{
    Console.Error.WriteLine("Series name is required.");
    return 1;
}

var scan = SourceScanner.Scan(parentFolder, author);
foreach (string name in scan.Ignored)
    Console.WriteLine($"Ignored (no MP3 or ebook files): {name}");

if (scan.Items.Count == 0)
{
    Console.Error.WriteLine("No audiobook folders or ebooks found.");
    return 1;
}

bool hasAudiobooks = scan.Items.Any(i => i.Kind == BookKind.Audiobook);
string? defaultNarrators = null;
if (hasAudiobooks)
{
    Console.Write($"Default narrator(s) (separate with , or ; — leave empty if every folder has a {BookPlanner.NarratorFileName}): ");
    defaultNarrators = BookPlanner.NormalizeNarrators(Console.ReadLine() ?? "");
    if (defaultNarrators.Length == 0) defaultNarrators = null;
}

Console.WriteLine();
Console.WriteLine($"  Author:    {author}");
if (hasAudiobooks)
    Console.WriteLine($"  Narrator:  {defaultNarrators ?? $"(from {BookPlanner.NarratorFileName})"}");
Console.WriteLine($"  Series:    {seriesName}");
Console.WriteLine();

var plan = BookPlanner.Plan(parentFolder, scan.Items, author, seriesName, defaultNarrators);
if (plan.Errors.Count > 0)
{
    foreach (string error in plan.Errors)
        Console.Error.WriteLine($"ERROR: {error}");
    return 1;
}

Console.WriteLine($"Found {plan.Books.Count} book(s):");
foreach (var book in plan.Books)
{
    string detail = book.Kind == BookKind.Audiobook
        ? $"{{{book.Narrators}}}"
        : $"[ebook: {string.Join(", ", book.EbookCopies.Select(c => Path.GetExtension(c.Target).TrimStart('.')))}]";
    Console.WriteLine($"  Vol. {BookPlanner.FormatVolumeFolder(book.Number)}  {book.Name}  {detail}   <- {Path.GetFileName(book.Source.Path)}");
}
Console.WriteLine();

string ffmpegPath = "", ffprobePath = "";
if (hasAudiobooks)
{
    ffmpegPath = await ResolveFfmpegAsync();
    ffprobePath = ResolveFfprobePath(ffmpegPath);
}

int successful = 0, skipped = 0, failed = 0;
int maxParallel = Math.Max(1, Environment.ProcessorCount / 2);
Console.WriteLine($"Processing with {maxParallel} parallel worker(s)...");
Console.WriteLine();

await Parallel.ForEachAsync(plan.Books, new ParallelOptions { MaxDegreeOfParallelism = maxParallel }, async (book, ct) =>
{
    string number = BookPlanner.FormatVolume(book.Number);
    string sourceName = Path.GetFileName(book.Source.Path)!;

    Console.WriteLine($"[Vol. {number}] Processing: {sourceName}");

    // Never overwrite an existing book (e.g. the same volume from another release).
    if (book.OutputFiles.Any(File.Exists))
    {
        Console.WriteLine($"  [{number}] Skipped — already exists: {book.OutputFile}");
        Interlocked.Increment(ref skipped);
        return;
    }

    bool ok = book.Kind == BookKind.Ebook
        ? ImportEbook(book)
        : await ConvertAudiobookAsync(book);
    if (!ok)
    {
        Interlocked.Increment(ref failed);
        return;
    }

    Console.WriteLine($"  [{number}] Done -> {Path.GetFileName(book.OutputFile)}");
    Interlocked.Increment(ref successful);

    // Move the source to Done/. A failure here keeps the finished output.
    try
    {
        SourceScanner.MoveToDone(parentFolder, book.Source);
        Console.WriteLine($"  [{number}] Moved source to Done/{sourceName}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  [{number}] WARNING — output created, but moving the source to Done/ failed: {ex.Message}");
    }
});

Console.WriteLine($"Done. {successful} created, {skipped} skipped, {failed} failed (of {plan.Books.Count}).");
return failed == 0 ? 0 : 1;

bool ImportEbook(BookPlan book)
{
    try
    {
        EbookImporter.Import(book, author, seriesName);
        return true;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  [{BookPlanner.FormatVolume(book.Number)}] FAILED — {ex.Message}");
        return false;
    }
}

async Task<bool> ConvertAudiobookAsync(BookPlan book)
{
    string number = BookPlanner.FormatVolume(book.Number);
    string[] mp3Files = CollectMp3Files(book.Source.Path);
    Console.WriteLine($"  [{number}] {mp3Files.Length} MP3 file(s)");

    string? coverImage = SourceScanner.FindCoverImage(book.Source.Path);
    if (coverImage is not null)
        Console.WriteLine($"  [{number}] Cover: {Path.GetFileName(coverImage)}");

    // Work files live in the temp folder, not in the source folder: the source folder
    // is moved to Done/ afterwards, and the files must not travel along or be deleted
    // from a path that no longer exists.
    string workDir = Directory.CreateTempSubdirectory("m4b-creator-").FullName;
    string concatListFile = Path.Combine(workDir, "filelist.txt");
    string metadataFile = Path.Combine(workDir, "metadata.txt");
    bool converted = false;
    try
    {
        Directory.CreateDirectory(book.OutputFolder);
        WriteConcatList(concatListFile, mp3Files);

        Console.WriteLine($"  [{number}] Reading chapter durations...");
        var durations = await GetMp3DurationsAsync(ffprobePath, mp3Files);
        WriteChapterMetadata(metadataFile, mp3Files, durations);
        Console.WriteLine($"  [{number}] {durations.Count} chapter(s), converting...");

        converted = await ConvertToM4bAsync(ffmpegPath, concatListFile, metadataFile, book.OutputFile, book.Title, author, book.Narrators!, (int)book.Number, coverImage);
        if (!converted)
            Console.Error.WriteLine($"  [{number}] FAILED — ffmpeg returned an error.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  [{number}] FAILED — {ex.Message}");
    }
    finally
    {
        Directory.Delete(workDir, recursive: true);
    }

    // A partial file would make the next run skip this volume as "already exists".
    if (!converted)
        File.Delete(book.OutputFile);
    return converted;
}

// ---------------------------------------------------------------------------

static string GetParentFolder(string[] args)
{
    if (args.Length > 0 && Directory.Exists(args[0]))
        return Path.GetFullPath(args[0]);

    Console.Write("Enter the parent folder path containing all audiobook folders: ");
    string? input = Console.ReadLine()?.Trim().Trim('"');

    if (string.IsNullOrWhiteSpace(input) || !Directory.Exists(input))
    {
        Console.Error.WriteLine("Invalid or non-existent folder path.");
        Environment.Exit(1);
    }

    return Path.GetFullPath(input);
}

static async Task<string> ResolveFfmpegAsync()
{
    // 1. Check if ffmpeg is on PATH
    if (TryRunFfmpeg("ffmpeg"))
    {
        Console.WriteLine("Using ffmpeg from PATH.");
        return "ffmpeg";
    }

    // 2. Check local tools folder
    string toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
    string localFfmpeg = Path.Combine(toolsDir, "ffmpeg.exe");

    if (File.Exists(localFfmpeg) && TryRunFfmpeg(localFfmpeg))
    {
        Console.WriteLine($"Using ffmpeg from {toolsDir}");
        return localFfmpeg;
    }

    // 3. Download ffmpeg
    Console.WriteLine("ffmpeg not found. Downloading ffmpeg...");
    await DownloadFfmpegAsync(toolsDir);

    if (!File.Exists(localFfmpeg))
    {
        Console.Error.WriteLine("Failed to obtain ffmpeg. Please install it manually.");
        Environment.Exit(1);
    }

    Console.WriteLine("ffmpeg downloaded successfully.");
    Console.WriteLine();
    return localFfmpeg;
}

static bool TryRunFfmpeg(string path)
{
    try
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            Arguments = "-version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        process?.WaitForExit();
        return process?.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

static async Task DownloadFfmpegAsync(string toolsDir)
{
    const string downloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    string tempZip = Path.Combine(Path.GetTempPath(), "ffmpeg-download.zip");

    try
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(10);

        Console.WriteLine($"Downloading from {downloadUrl}");
        Console.WriteLine("This may take a few minutes...");

        using (var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            long? totalBytes = response.Content.Headers.ContentLength;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[81920];
            long downloaded = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloaded += bytesRead;

                if (totalBytes.HasValue)
                {
                    int percent = (int)(downloaded * 100 / totalBytes.Value);
                    Console.Write($"\r  {percent}% ({downloaded / 1024 / 1024} MB / {totalBytes.Value / 1024 / 1024} MB)");
                }
            }

            Console.WriteLine();
        }

        Console.WriteLine("Extracting ffmpeg.exe...");
        Directory.CreateDirectory(toolsDir);

        using var zip = ZipFile.OpenRead(tempZip);

        // The zip contains a top-level folder like "ffmpeg-7.1-essentials_build/bin/ffmpeg.exe"
        string[] targets = ["ffmpeg.exe", "ffprobe.exe"];
        foreach (var entry in zip.Entries)
        {
            string fileName = Path.GetFileName(entry.FullName);
            if (targets.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                string destPath = Path.Combine(toolsDir, fileName);
                entry.ExtractToFile(destPath, overwrite: true);
                Console.WriteLine($"  Extracted {fileName}");
            }
        }
    }
    finally
    {
        if (File.Exists(tempZip))
            File.Delete(tempZip);
    }
}

static string ResolveFfprobePath(string ffmpegPath)
{
    string candidate = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? "", "ffprobe.exe");
    if (File.Exists(candidate)) return candidate;

    candidate = ffmpegPath.Replace("ffmpeg", "ffprobe");
    if (File.Exists(candidate)) return candidate;

    return ffmpegPath == "ffmpeg" ? "ffprobe" : candidate;
}

static string[] CollectMp3Files(string folder)
{
    var result = new List<string>();
    CollectMp3FilesRecursive(folder, result);
    return result.ToArray();
}

static void CollectMp3FilesRecursive(string folder, List<string> result)
{
    var comparer = NaturalStringComparer.Instance;

    // Add MP3s from current folder, naturally sorted
    result.AddRange(
        Directory.GetFiles(folder, "*.mp3").OrderBy(f => f, comparer)
    );

    // Recurse into subfolders, naturally sorted
    foreach (string sub in Directory.GetDirectories(folder).OrderBy(d => d, comparer))
    {
        CollectMp3FilesRecursive(sub, result);
    }
}

static void WriteConcatList(string listFile, string[] mp3Files)
{
    using var writer = new StreamWriter(listFile, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    foreach (string file in mp3Files)
        writer.WriteLine($"file '{file.Replace("'", "'\\''")}'");
}

static async Task<List<double>> GetMp3DurationsAsync(string ffprobePath, string[] mp3Files)
{
    var durations = new List<double>();
    foreach (string file in mp3Files)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string arg in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", file })
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        string output = (await process.StandardOutput.ReadToEndAsync()).Trim();
        await process.WaitForExitAsync();

        if (double.TryParse(output, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double duration))
            durations.Add(duration);
        else
            durations.Add(0);
    }
    return durations;
}

static void WriteChapterMetadata(string metadataFile, string[] mp3Files, List<double> durations)
{
    using var writer = new StreamWriter(metadataFile, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.WriteLine(";FFMETADATA1");

    long startMs = 0;
    for (int i = 0; i < mp3Files.Length; i++)
    {
        long endMs = startMs + (long)(durations[i] * 1000);
        string chapterTitle = Path.GetFileNameWithoutExtension(mp3Files[i]);

        writer.WriteLine();
        writer.WriteLine("[CHAPTER]");
        writer.WriteLine("TIMEBASE=1/1000");
        writer.WriteLine($"START={startMs}");
        writer.WriteLine($"END={endMs}");
        writer.WriteLine($"title={chapterTitle}");

        startMs = endMs;
    }
}

static async Task<bool> ConvertToM4bAsync(
    string ffmpegPath, string concatListFile, string metadataFile, string outputFile, string title,
    string author, string narrators, int trackNumber, string? coverImage)
{
    var args = new List<string>
    {
        "-f", "concat",
        "-safe", "0",
        "-i", concatListFile,
        "-i", metadataFile
    };

    int metadataInputIndex = 1;
    int coverInputIndex = 2;

    if (coverImage is not null)
    {
        args.AddRange(["-i", coverImage]);
    }

    args.AddRange(["-map", "0:a"]);

    if (coverImage is not null)
    {
        args.AddRange([
            "-map", $"{coverInputIndex}:v",
            "-c:v", "copy",
            "-disposition:v", "attached_pic"
        ]);
    }

    args.AddRange([
        "-map_metadata", $"{metadataInputIndex}",
        "-c:a", "aac",
        "-b:a", "128k",
        "-movflags", "+faststart",
        "-metadata", $"title={title}",
        "-metadata", $"album={title}",
        "-metadata", $"artist={author}",
        "-metadata", $"album_artist={author}",
        "-metadata", $"composer={narrators}",
        "-metadata", $"track={trackNumber}",
        "-y",
        outputFile
    ]);

    using var process = new Process();
    process.StartInfo = new ProcessStartInfo
    {
        FileName = ffmpegPath,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    foreach (string arg in args)
        process.StartInfo.ArgumentList.Add(arg);

    process.Start();

    string stderr = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    if (process.ExitCode != 0)
    {
        Console.Error.WriteLine(stderr);
        return false;
    }

    return true;
}
