using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

Console.OutputEncoding = Encoding.UTF8;

string parentFolder = GetParentFolder(args);

Console.Write("Author: ");
string author = Console.ReadLine()?.Trim() ?? "";
if (string.IsNullOrWhiteSpace(author))
{
    Console.Error.WriteLine("Author is required.");
    return 1;
}

Console.Write("Narrator(s) (separate multiple with , or ;): ");
string narratorInput = Console.ReadLine()?.Trim() ?? "";
if (string.IsNullOrWhiteSpace(narratorInput))
{
    Console.Error.WriteLine("At least one narrator is required.");
    return 1;
}
string narrators = string.Join(", ", Regex.Split(narratorInput, @"\s*[,;]\s*").Where(n => n.Length > 0));

Console.Write("Series name: ");
string seriesName = Console.ReadLine()?.Trim() ?? "";
if (string.IsNullOrWhiteSpace(seriesName))
{
    Console.Error.WriteLine("Series name is required.");
    return 1;
}

Console.WriteLine();
Console.WriteLine($"  Author:    {author}");
Console.WriteLine($"  Narrator:  {narrators}");
Console.WriteLine($"  Series:    {seriesName}");
Console.WriteLine();

string[] subfolders = Directory.GetDirectories(parentFolder)
    .Where(d => !Regex.IsMatch(Path.GetFileName(d)!, @"^Vol\.\s\d{3}\s-\s"))
    .Where(d => !string.Equals(Path.GetFileName(d), "Done", StringComparison.OrdinalIgnoreCase))
    .OrderBy(d => d, new NaturalStringComparer())
    .ToArray();

if (subfolders.Length == 0)
{
    Console.Error.WriteLine("No subfolders found.");
    return 1;
}

Console.WriteLine($"Found {subfolders.Length} audiobook folder(s):");
foreach (string folder in subfolders)
    Console.WriteLine($"  - {Path.GetFileName(folder)}");
Console.WriteLine();

// Detect shared prefix/suffix across all folder names
string[] folderNames = subfolders.Select(d => Path.GetFileName(d)!).ToArray();
string commonPrefix = FindCommonPrefix(folderNames);
string commonSuffix = FindCommonSuffix(folderNames);
if (!string.IsNullOrWhiteSpace(commonPrefix))
    Console.WriteLine($"Common prefix detected (will be removed): \"{commonPrefix}\"");
if (!string.IsNullOrWhiteSpace(commonSuffix))
    Console.WriteLine($"Common suffix detected (will be removed): \"{commonSuffix}\"");

string ffmpegPath = await ResolveFfmpegAsync();

int successful = 0;
int maxParallel = Math.Max(1, Environment.ProcessorCount / 2);
Console.WriteLine($"Processing with {maxParallel} parallel worker(s)...");
Console.WriteLine();

string ffprobePath = ResolveFfprobePath(ffmpegPath);

await Parallel.ForEachAsync(
    Enumerable.Range(0, subfolders.Length),
    new ParallelOptions { MaxDegreeOfParallelism = maxParallel },
    async (i, ct) =>
{
    int number = i + 1;
    string folder = subfolders[i];
    string folderName = Path.GetFileName(folder)!;
    string audiobookName = CleanAudiobookName(folderName, seriesName, author, commonPrefix, commonSuffix);
    string displayName = $"{audiobookName} {{{narrators}}}";
    string title = $"{audiobookName}: {seriesName} {number}";
    string outputFolder = Path.Combine(parentFolder, author, seriesName, $"Vol. {number:D3} - {displayName}");
    Directory.CreateDirectory(outputFolder);
    string outputFile = Path.Combine(outputFolder, $"{displayName}.m4b");

    Console.WriteLine($"[{number}/{subfolders.Length}] Processing: {folderName}");

    string[] mp3Files = CollectMp3Files(folder);

    if (mp3Files.Length == 0)
    {
        Console.WriteLine($"  [{number}] Skipped — no MP3 files found.");
        return;
    }

    Console.WriteLine($"  [{number}] {mp3Files.Length} MP3 file(s)");

    string? coverImage = FindCoverImage(folder);
    if (coverImage is not null)
        Console.WriteLine($"  [{number}] Cover: {Path.GetFileName(coverImage)}");

    string concatListFile = Path.Combine(folder, "_filelist.txt");
    string metadataFile = Path.Combine(folder, "_metadata.txt");
    try
    {
        WriteConcatList(concatListFile, mp3Files);

        Console.WriteLine($"  [{number}] Reading chapter durations...");
        var durations = await GetMp3DurationsAsync(ffprobePath, mp3Files);
        WriteChapterMetadata(metadataFile, mp3Files, durations);
        Console.WriteLine($"  [{number}] {durations.Count} chapter(s), converting...");

        bool ok = await ConvertToM4bAsync(ffmpegPath, concatListFile, metadataFile, outputFile, title, author, narrators, number, coverImage);
        if (ok)
        {
            Console.WriteLine($"  [{number}] Done -> {Path.GetFileName(outputFile)}");
            Interlocked.Increment(ref successful);

            // Move original folder to "Done" subfolder
            string doneDir = Path.Combine(parentFolder, "Done");
            Directory.CreateDirectory(doneDir);
            string dest = Path.Combine(doneDir, folderName);
            Directory.Move(folder, dest);
            Console.WriteLine($"  [{number}] Moved source to Done/{folderName}");
        }
        else
        {
            Console.Error.WriteLine($"  [{number}] FAILED — ffmpeg returned an error.");
        }
    }
    finally
    {
        File.Delete(concatListFile);
        File.Delete(metadataFile);
    }
});

Console.WriteLine($"Done. {successful}/{subfolders.Length} audiobook(s) created.");
return successful == subfolders.Length ? 0 : 1;

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
    var comparer = new NaturalStringComparer();

    // Get MP3s from the root folder
    var rootMp3s = Directory.GetFiles(folder, "*.mp3")
        .OrderBy(f => f, comparer)
        .ToList();

    // Get MP3s from subfolders (CD1, CD2, Disc 1, etc.), sorted by subfolder name then filename
    var subfolderMp3s = Directory.GetDirectories(folder)
        .OrderBy(d => d, comparer)
        .SelectMany(d => Directory.GetFiles(d, "*.mp3").OrderBy(f => f, comparer))
        .ToList();

    // If there are MP3s in both root and subfolders, root first then subfolders
    // If only in subfolders, just use those
    if (rootMp3s.Count > 0 && subfolderMp3s.Count > 0)
        rootMp3s.AddRange(subfolderMp3s);
    else if (subfolderMp3s.Count > 0)
        rootMp3s = subfolderMp3s;

    return rootMp3s.ToArray();
}

static string? FindCoverImage(string folder)
{
    string[] imageExtensions = ["*.jpg", "*.jpeg", "*.png"];

    // Check root folder first
    foreach (string ext in imageExtensions)
    {
        string? image = Directory.GetFiles(folder, ext).FirstOrDefault();
        if (image is not null)
            return image;
    }

    // Check subfolders
    foreach (string sub in Directory.GetDirectories(folder))
    {
        foreach (string ext in imageExtensions)
        {
            string? image = Directory.GetFiles(sub, ext).FirstOrDefault();
            if (image is not null)
                return image;
        }
    }

    return null;
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

static string FindCommonPrefix(string[] names)
{
    if (names.Length < 2) return "";
    string first = names[0];
    int len = 0;
    for (int i = 0; i < first.Length; i++)
    {
        char c = char.ToUpperInvariant(first[i]);
        if (names.All(n => i < n.Length && char.ToUpperInvariant(n[i]) == c))
            len = i + 1;
        else
            break;
    }
    // Trim to last word boundary to avoid cutting mid-word
    string prefix = first[..len];
    int lastSpace = prefix.LastIndexOfAny([' ', '-', '_', '.']);
    return lastSpace > 0 ? prefix[..lastSpace] : "";
}

static string FindCommonSuffix(string[] names)
{
    if (names.Length < 2) return "";
    string first = names[0];
    int len = 0;
    for (int i = 1; i <= first.Length; i++)
    {
        char c = char.ToUpperInvariant(first[^i]);
        if (names.All(n => i <= n.Length && char.ToUpperInvariant(n[^i]) == c))
            len = i;
        else
            break;
    }
    // Trim to last word boundary to avoid cutting mid-word
    string suffix = first[^len..];
    int firstSpace = suffix.IndexOfAny([' ', '-', '_', '.']);
    return firstSpace >= 0 ? suffix[(firstSpace + 1)..] : "";
}

static string CleanAudiobookName(string folderName, string seriesName, string author,
    string commonPrefix, string commonSuffix)
{
    string cleaned = folderName;

    // Remove common prefix/suffix shared by all folders
    if (!string.IsNullOrWhiteSpace(commonPrefix))
        cleaned = Regex.Replace(cleaned, "^" + Regex.Escape(commonPrefix), "", RegexOptions.IgnoreCase).Trim();
    if (!string.IsNullOrWhiteSpace(commonSuffix))
        cleaned = Regex.Replace(cleaned, Regex.Escape(commonSuffix) + "$", "", RegexOptions.IgnoreCase).Trim();

    // Remove series name and author
    cleaned = Regex.Replace(cleaned, Regex.Escape(seriesName), "", RegexOptions.IgnoreCase).Trim();
    cleaned = Regex.Replace(cleaned, Regex.Escape(author), "", RegexOptions.IgnoreCase).Trim();

    // Remove anything in square brackets or parentheses like [1980], (2013), [FLAC], etc.
    cleaned = Regex.Replace(cleaned, @"\[.*?\]", "").Trim();
    cleaned = Regex.Replace(cleaned, @"\(.*?\)", "").Trim();

    // Remove episode/Folge labels (German and English) with their number
    cleaned = Regex.Replace(cleaned, @"\b(Folge|Episode|Ep\.?|Part|Teil|Chapter|Kapitel|Nr\.?|No\.?|Volume|Vol\.?|Band|Heft)\s*\d+\b", "", RegexOptions.IgnoreCase).Trim();

    // Remove bitrate patterns like "320 kbit", "128kbps", "192 kbit/s"
    cleaned = Regex.Replace(cleaned, @"\b\d+\s*kbit(/s)?\b", "", RegexOptions.IgnoreCase).Trim();
    cleaned = Regex.Replace(cleaned, @"\b\d+\s*kbps\b", "", RegexOptions.IgnoreCase).Trim();

    // Remove common source/quality tags
    cleaned = Regex.Replace(cleaned, @"\b(TbT|Webrip|WEB-DL|WEB|WEBRip|MP3|FLAC|AAC|OGG|CBR|VBR|Lossless|Lossy|HQ|LQ|Remastered|Repack|Proper)\b", "", RegexOptions.IgnoreCase).Trim();

    // Strip leftover leading/trailing dashes, whitespace, and common separators
    cleaned = Regex.Replace(cleaned, @"^[\s\-–—_.,;:]+", "");
    cleaned = Regex.Replace(cleaned, @"[\s\-–—_.,;:]+$", "");

    // Strip leading digits and separators so the name starts with A-Z
    cleaned = Regex.Replace(cleaned, @"^[\d\s\-–—_.,;:]+", "");

    // If nothing meaningful remains, fall back to original
    if (string.IsNullOrWhiteSpace(cleaned) || !Regex.IsMatch(cleaned, @"^[A-Za-z]"))
        return folderName;

    return cleaned;
}

class NaturalStringComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (x == y) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int ix = 0, iy = 0;

        while (ix < x.Length && iy < y.Length)
        {
            bool xDigit = char.IsDigit(x[ix]);
            bool yDigit = char.IsDigit(y[iy]);

            if (xDigit && yDigit)
            {
                // Compare numeric segments by value
                int xStart = ix, yStart = iy;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;

                var xNum = x.AsSpan(xStart, ix - xStart);
                var yNum = y.AsSpan(yStart, iy - yStart);

                // Skip leading zeros for comparison
                var xTrimmed = xNum.TrimStart('0');
                var yTrimmed = yNum.TrimStart('0');

                if (xTrimmed.Length != yTrimmed.Length)
                    return xTrimmed.Length.CompareTo(yTrimmed.Length);

                int numCmp = xTrimmed.CompareTo(yTrimmed, StringComparison.Ordinal);
                if (numCmp != 0) return numCmp;
            }
            else
            {
                int cmp = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                if (cmp != 0) return cmp;
                ix++;
                iy++;
            }
        }

        return x.Length.CompareTo(y.Length);
    }
}
