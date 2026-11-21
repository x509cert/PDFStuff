using System.IO;
using Broadside.Security;
using Broadside.Viewer;

namespace Broadside.Reader;

internal sealed class DocumentFile : IAsyncDisposable
{
    private readonly FileStream _source;
    private FileStamp _stamp;

    private DocumentFile(string path, FileStream source, DocumentSession session, FileStamp stamp)
    {
        Path = path;
        _source = source;
        Session = session;
        _stamp = stamp;
    }

    public string Path { get; private set; }

    public DocumentSession Session { get; }

    public bool IsDirty { get; private set; }

    public void MarkChanged() => IsDirty = true;

    public static Task<DocumentFile> OpenAsync(string path, PdfPassword? password = null) => Task.Run(() =>
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        using var original = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        FileStamp stamp = FileStamp.Read(fullPath);
        string snapshot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Broadside.Reader-" + Guid.NewGuid().ToString("N") + ".pdf");
        // A private disk snapshot permits safe replacement and protects lazy reads from external edits without buffering the PDF in RAM.
        var source = new FileStream(snapshot, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 64 * 1024,
            FileOptions.DeleteOnClose | FileOptions.RandomAccess);
        try
        {
            original.CopyTo(source);
            source.Position = 0;
            var options = new ViewerOptions()
                .WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps());
            options.Credentials = password;
            DocumentSession session = DocumentSession.Open(source, options);
            return new DocumentFile(fullPath, source, session, stamp);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    });

    public async Task SaveAsync(string destination)
    {
        string fullPath = System.IO.Path.GetFullPath(destination);
        bool replacingSource = string.Equals(fullPath, Path, StringComparison.OrdinalIgnoreCase);
        await Task.Run(() =>
        {
            using IDisposable edit = Session.BeginEdit();
            if (replacingSource && FileStamp.Read(fullPath) != _stamp)
            {
                throw new IOException("The file changed on disk. Use Save As to keep both versions.");
            }

            // Keep the edit lease on this worker until the asynchronous, atomic save has finished.
            Session.Document.SaveAsync(fullPath, new PdfSaveOptions().WithIncrementalUpdate(true))
                .GetAwaiter().GetResult();
        });
        Path = fullPath;
        _stamp = FileStamp.Read(fullPath);
        IsDirty = false;
    }

    public async ValueTask DisposeAsync()
    {
        await Session.DisposeAsync();
        await _source.DisposeAsync();
    }

    private readonly record struct FileStamp(long Length, DateTime Modified)
    {
        public static FileStamp Read(string path)
        {
            var info = new FileInfo(path);
            return new FileStamp(info.Length, info.LastWriteTimeUtc);
        }
    }
}
