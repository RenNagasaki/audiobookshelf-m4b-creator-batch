# Audiobookshelf M4B Creator (Batch)

A command-line tool that batch-converts folders of MP3 audiobooks into M4B audiobook files with proper metadata and chapter markers. Designed to produce files that work seamlessly with [Audiobookshelf](https://github.com/advplyr/audiobookshelf).

## What it does

Given a parent folder containing multiple subfolders (one per audiobook), this tool:

- Combines all MP3 files in each subfolder into a single `.m4b` file
- Supports subfolders within audiobook folders (e.g. `CD1/`, `CD2/`, `Disc 1/`) — files are ordered by subfolder then by filename using natural sort
- Creates a chapter for each MP3 file (using the filename as the chapter title)
- Embeds cover art if an image (`.jpg`, `.jpeg`, `.png`) is found in the folder or its subfolders
- Writes metadata (title, album, author, narrator, track number) compatible with Audiobookshelf
- Outputs each M4B into a structured folder: `Author/Series/Vol. 001 - Audiobook Name {Narrators}/`
- Automatically cleans up audiobook names by removing series names, author names, episode labels, bitrate info, source tags, bracket/parenthesis content, and shared prefixes/suffixes
- Moves successfully converted source folders into a `Done/` subfolder
- Processes multiple audiobooks in parallel for faster conversion
- Downloads ffmpeg automatically if it's not already installed

## Prerequisites

- [.NET 10 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) (or SDK if building from source)
- **ffmpeg** — the tool will auto-download it on first run if not found on your PATH

## Usage

### Run from source

```bash
dotnet run --project audiobookshelf-m4b-creator-batch
```

Or pass the folder path directly as an argument:

```bash
dotnet run --project audiobookshelf-m4b-creator-batch -- "D:\Audiobooks\My Series"
```

### Interactive prompts

The tool will ask for the following information:

```
Enter the parent folder path containing all audiobook folders: D:\Audiobooks\My Series
Author: Author Name
Narrator(s) (separate multiple with , or ;): Narrator One, Narrator Two
Series name: My Series
```

### Expected folder structure

```
D:\Audiobooks\My Series\          <-- parent folder (you provide this path)
├── Book One\
│   ├── 01 - Chapter 1.mp3
│   ├── 02 - Chapter 2.mp3
│   └── cover.jpg
├── Book Two\
│   ├── CD1\
│   │   ├── 01 - Chapter 1.mp3
│   │   └── 02 - Chapter 2.mp3
│   ├── CD2\
│   │   ├── 01 - Chapter 3.mp3
│   │   └── 02 - Chapter 4.mp3
│   └── Images\
│       └── cover.jpg
└── ...
```

### Output

```
D:\Audiobooks\My Series\
├── Author Name\
│   └── My Series\
│       ├── Vol. 001 - Book One {Narrator One, Narrator Two}\
│       │   └── Book One {Narrator One, Narrator Two}.m4b
│       ├── Vol. 002 - Book Two {Narrator One, Narrator Two}\
│       │   └── Book Two {Narrator One, Narrator Two}.m4b
│       └── ...
├── Done\
│   ├── Book One\
│   └── Book Two\
└── ...
```

### Metadata written to each M4B

| Field          | Value                                      |
|----------------|--------------------------------------------|
| `title`        | `{Audiobook Name}: {Series Name} {Number}` |
| `album`        | `{Audiobook Name}: {Series Name} {Number}` |
| `artist`       | Author                                     |
| `album_artist` | Author                                     |
| `composer`     | Narrator(s) — used by Audiobookshelf       |
| `track`        | Position in series                         |

### Name cleaning

The tool automatically strips the following from folder names to produce clean audiobook titles:

- Series name and author name
- Episode labels: `Folge 1`, `Episode 3`, `Part 2`, `Kapitel 5`, etc. (German and English)
- Bracketed content: `[1980]`, `[FLAC]`
- Parenthesized content: `(2013)`, `(Remastered)`
- Bitrate info: `320 kbit`, `128kbps`
- Source tags: `Webrip`, `TbT`, `MP3`, `FLAC`, `CBR`, etc.
- Shared prefixes/suffixes common to all folders
- Leading numbers and separators

## Re-running

The tool skips its own output folders (`Vol. XXX - ...`) and the `Done/` folder when scanning, so it's safe to re-run without processing previously created files.

## License

See [LICENSE](LICENSE).
