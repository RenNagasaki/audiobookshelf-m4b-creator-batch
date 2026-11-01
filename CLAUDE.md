# audiobookshelf-m4b-creator-batch

CLI (.NET 10) that turns a folder of MP3 audiobook folders (one series) into M4B files laid out for Audiobookshelf. User-facing docs: `README.md`.

## Build & test
- `node .claude/verify.js` — build + tests. Prefers the user-local SDK in `~/.dotnet` (the global `dotnet` on this machine only has SDK 9, which cannot target net10.0).
- Manual: `PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" dotnet test`

## Structure
- `audiobookshelf-m4b-creator-batch/Program.cs` — top-level program: prompts, folder scan, parallel conversion, ffmpeg/ffprobe handling (download, concat, chapters, cover). Untested I/O glue.
- `BookPlanner.cs` — pure planning per run: volume number (leading 1–3 digits of folder name, else sort position; mixed = error), narrators (`narrator.txt` per folder > default prompt), title and output paths, collision check. Returns `PlanResult(Books, Errors)`; Program aborts on any error before converting.
- `NameCleaner.cs` — folder name → clean title (strips author, series, brackets, episode labels, bitrate/source tags, shared prefix/suffix, leading digits).
- `NaturalStringComparer.cs` — numeric-aware sort used for folders and MP3s.
- `audiobookshelf-m4b-creator-batch.Tests/` — xUnit tests for the three classes above (references the exe project).

## Behavior worth knowing
- Output: `<parent>/<Author>/<Series>/Vol. NNN - <Title> {<Narrators>}/<Title> {<Narrators>}.m4b`. Narrator in the path lets two releases of a series coexist in Audiobookshelf.
- Scan skips `Done/`, `Vol. NNN - …` folders and the `<Author>/` output folder.
- Existing output M4B → volume skipped (never overwritten). Failed ffmpeg run deletes the partial file. Success moves the source folder to `Done/`.
- ffmpeg work files (concat list, chapter metadata) go to a per-book temp dir, never into the source folder — it is moved to `Done/` afterwards (writing them there crashed the run once).
- Every book is wrapped in try/catch: one failing book counts as failed but never aborts the parallel loop.
- Metadata: title/album = `<Title>: <Series> <N>`, artist/album_artist = author, composer = narrators (Audiobookshelf reads narrator from composer), track = N.

## Release
- Pushing to master runs `.github/workflows/release.yml`: it releases only if the `<Version>` in the main .csproj has no git tag yet. Bump the version for a new release.
