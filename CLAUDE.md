# audiobookshelf-m4b-creator-batch

CLI (.NET 10) that turns a folder of MP3 audiobook folders (one series) into M4B files laid out for Audiobookshelf, and sorts ebooks of the series into the same layout. User-facing docs: `README.md`.

## Build & test
- `node .claude/verify.js` — build + tests. Prefers the user-local SDK in `~/.dotnet` (the global `dotnet` on this machine only has SDK 9, which cannot target net10.0).
- Manual: `PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" dotnet test`

## Structure
- `audiobookshelf-m4b-creator-batch/Program.cs` — top-level program: prompts, parallel loop dispatching per `BookKind`, ffmpeg/ffprobe handling (download, concat, chapters). Untested I/O glue.
- `SourceScanner.cs` — finds the books: subfolder with MP3s → `Audiobook`, subfolder with only ebook files or loose ebook file in the parent → `Ebook`, else ignored. Also `FindCoverImage`, `MoveToDone` (folder or file). `SourceItem.IsFile` is captured at construction (the path vanishes after the move).
- `EbookImporter.cs` — copies ebook files unchanged + writes `metadata.opf` (Calibre-style, unprefixed elements; Audiobookshelf reads title/author/`calibre:series`/`series_index` from it) + `cover.<ext>`; rolls back its own files on failure.
- `BookPlanner.cs` — pure planning per run: volume number as `decimal` (leading 1–3 digits of the name, or right after the shared prefix; 0 allowed; half volumes `15.5`/`15,5`/`15 5 -`; else sort position; mixed = error; folder `Vol. 015.5`, title/metadata `15.5`, M4B track = integer part), narrators (audiobooks only: `narrator.txt` per folder > default prompt), title and output paths, collision check. Returns `PlanResult(Books, Errors)`; Program aborts on any error before converting.
- `NameCleaner.cs` — folder name → clean title (strips author, series, brackets, episode labels, bitrate/source tags, shared prefix/suffix, leading digits, trailing genre label like `- Roman`/`- Thriller`).
- `NaturalStringComparer.cs` — numeric-aware sort used for folders and MP3s.
- `audiobookshelf-m4b-creator-batch.Tests/` — xUnit tests for all classes above except Program (references the exe project).

## Behavior worth knowing
- Output: `<parent>/Audiobook/<Author>/<Series>/Vol. NNN - <Title> {<Narrators>}/<Title> {<Narrators>}.m4b`. Narrator in the path lets two releases of a series coexist in Audiobookshelf.
- Ebook output: `<parent>/Book/<Author>/<Series>/Vol. NNN - <Title>/<Title>.<ext>` — separate root because ebooks live in their own Audiobookshelf library (user decision 2026-11-03); no narrator part. Roots: `BookPlanner.AudiobookOutputFolder`/`EbookOutputFolder` via `OutputRoot()`. Narrator prompt and ffmpeg only when the run has audiobooks.
- Scan skips `Done/`, `Audiobook/`, `Book/`, `Vol. NNN - …` folders and the legacy `<Author>/` folder (runs before this layout wrote there; M4Bs there are NOT detected as "already exists").
- Existing output M4B → volume skipped (never overwritten). Failed ffmpeg run deletes the partial file. Success moves the source folder to `Done/`.
- ffmpeg work files (concat list, chapter metadata) go to a per-book temp dir, never into the source folder — it is moved to `Done/` afterwards (writing them there crashed the run once).
- Every book is wrapped in try/catch: one failing book counts as failed but never aborts the parallel loop.
- Metadata: title/album = `<Title>: <Series> <N>`, artist/album_artist = author, composer = narrators (Audiobookshelf reads narrator from composer), track = N.

## Release
- Pushing to master (on GitLab; the mirror job forwards it to GitHub) runs `.github/workflows/release.yml`: it releases only if the `<Version>` in the main .csproj has no git tag yet. Bump the version for a new release.

## Git-Remotes
- `origin` → `https://gitlab.echotools.cloud/private/audiobookshelf-m4b-creator-batch.git` (Hauptremote, hierhin pushen).
- `github` → `https://github.com/RenNagasaki/audiobookshelf-m4b-creator-batch.git`: Nur-Lese-Spiegel, den der CI-Job `mirror-to-github` (`.gitlab-ci.yml`) bei jedem Push auf den Default-Branch oder einem Tag additiv befüllt (nie `--mirror`, sonst gehen GitHub-Release-Tags verloren). Nicht direkt nach GitHub pushen: der Job pusht mit `--force` und überschreibt das.
