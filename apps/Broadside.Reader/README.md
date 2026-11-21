# SneakyPdf for Windows

A stand-alone WPF application using the Broadside viewer control and managed
software rasterizer. It supports:

- Local PDF files through a file picker, drag-and-drop, or a command-line path.
- Continuous, single-page, two-page and horizontal page layouts, fit modes and zoom.
- A right-side scrollbar with a draggable thumb, page steps and synchronized scroll position.
- Text selection and copy, background search, outlines, page labels and thumbnails.
- AcroForm filling, Save and Save As, and an unsaved-change prompt.
- Password-protected documents, smart dark pages and document diagnostics.
- Recent files, saved window preferences and the control's UI Automation support.

One document is open per window. Run another instance for another document.
External HTTP and HTTPS links require confirmation. PDF scripts are never
executed. Printing, OCR, XFA rendering, signature-validation UI, annotation tools,
an installer and file-association registration are not included.

## Run from source

On Windows with the .NET SDK selected by the repository's `global.json`:

```powershell
dotnet run --project apps\Broadside.Reader -- "C:\Documents\example.pdf"
```

Omit the PDF path to show the welcome screen. Keyboard shortcuts include Ctrl+O,
Ctrl+S, Ctrl+Shift+S, Ctrl+F and Ctrl+C. Enter in the page box navigates to that
page. Enter in the search box finds text; subsequent Enter presses move to the
next match, and Shift+Enter moves to the previous match.

## Portable distribution

```powershell
.\tools\Publish-SneakyPdf.ps1
```

The script publishes a self-contained Windows x64 app and creates
`artifacts\sneakypdf\SneakyPdf-win-x64.zip`. Extract the archive and run
`win-x64\SneakyPdf.exe`; keep all files together. The destination machine
does not need a separately installed .NET runtime. Use `-Runtime win-arm64` to
publish for Windows on ARM. The build is not trimmed or Native AOT, and the
executable is not code-signed.

For compatibility, settings remain in `%LOCALAPPDATA%\Broadside\Reader\settings.json`. They
include recent file paths but never passwords. Clear recent files from the File
menu. `BROADSIDE_READER_SETTINGS` can select a different settings file, including
for isolated tests.

## Saving

Form changes affect the open document until saved. Saving commits the active
field edit and writes an incremental update, retaining the source bytes and
existing encryption. It uses an atomic temporary-file replacement and prevents
background document reads during the save. A detected external file change
refuses an in-place save; use Save As to keep both versions.

Opening makes a private disk snapshot in the Windows temporary directory so
lazy reads remain stable if another application changes the original file. This
costs a disk copy and temporary space approximately the size of the PDF, rather
than loading the entire PDF into RAM. The snapshot is deleted automatically
when the document closes or its process exits.

Incremental saving does not guarantee that a document's signatures remain valid:
form edits can violate certification or field-lock restrictions. Validate
important signed documents with a signature-aware tool.

## Tests

```powershell
dotnet test --project tests\Broadside.Reader.Tests
dotnet format tests\Broadside.Reader.Tests --verify-no-changes
dotnet format apps\Broadside.Reader --verify-no-changes
```

UI Automation tests require an interactive Windows desktop. To test the portable
app rather than its development build:

```powershell
$env:BROADSIDE_READER_EXE = "$PWD\artifacts\sneakypdf\win-x64\SneakyPdf.exe"
dotnet test --project tests\Broadside.Reader.Tests
```

These tests launch their own processes with temporary working directories and
isolated settings. They do not change your reader preferences.
