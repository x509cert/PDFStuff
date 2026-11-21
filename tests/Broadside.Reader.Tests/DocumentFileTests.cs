using Broadside.Forms;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Reader.Tests;

public sealed class DocumentFileTests
{
    [Fact]
    public async Task Saving_to_the_open_path_and_then_another_path_preserves_edits_and_original_bytes()
    {
        using var directory = new TestDirectory();
        string path = directory.Copy("acroform-fields.pdf");
        byte[] original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        await using (DocumentFile file = await DocumentFile.OpenAsync(path))
        {
            ChangeName(file, "First edit");
            await file.SaveAsync(path);
            Assert.False(file.IsDirty);
            byte[] saved = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(saved.AsSpan(0, original.Length).SequenceEqual(original));
            Assert.Equal("First edit", ReadName(path));

            ChangeName(file, "Second edit");
            string copy = directory.PathOf("copy.pdf");
            await file.SaveAsync(copy);
            Assert.Equal(copy, file.Path);
            Assert.False(file.IsDirty);
            Assert.Equal("Second edit", ReadName(copy));
            Assert.Equal("First edit", ReadName(path));

            ChangeName(file, "Third edit");
            await file.SaveAsync(copy);
            Assert.Equal("Third edit", ReadName(copy));
        }

        using FileStream exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task A_failed_save_keeps_the_original_and_the_unsaved_state()
    {
        using var directory = new TestDirectory();
        string path = directory.Copy("acroform-fields.pdf");
        byte[] original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        await using DocumentFile file = await DocumentFile.OpenAsync(path);
        ChangeName(file, "Keep this edit");
        string destination = directory.PathOf("missing", "copy.pdf");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => file.SaveAsync(destination));
        Assert.True(file.IsDirty);
        Assert.Equal(path, file.Path);
        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(destination));
        Assert.Equal("Keep this edit", ((PdfTextField)file.Session.Document.AcroForm!.FindField("name")!).Value);
        await file.SaveAsync(path);
        Assert.Equal("Keep this edit", ReadName(path));
    }

    [Fact]
    public async Task A_replaced_source_is_not_overwritten_and_the_edit_can_be_saved_elsewhere()
    {
        using var directory = new TestDirectory();
        string path = directory.Copy("acroform-fields.pdf");
        await using DocumentFile file = await DocumentFile.OpenAsync(path);
        ChangeName(file, "Unsaved version");
        string replacement = directory.Copy("empty-page.pdf");
        File.Move(replacement, path, overwrite: true);
        byte[] external = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        IOException exception = await Assert.ThrowsAsync<IOException>(() => file.SaveAsync(path));
        Assert.Contains("Save As", exception.Message, StringComparison.Ordinal);
        Assert.True(file.IsDirty);
        Assert.Equal(external, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        string destination = directory.PathOf("recovered.pdf");
        await file.SaveAsync(destination);
        Assert.Equal("Unsaved version", ReadName(destination));
    }

    [Fact]
    public async Task Password_failures_release_the_source_and_a_correct_password_opens_it()
    {
        using var directory = new TestDirectory();
        string path = directory.Copy("encrypted-user-password.pdf");
        PdfPasswordException required = await Assert.ThrowsAsync<PdfPasswordException>(() => DocumentFile.OpenAsync(path));
        Assert.Equal(PdfPasswordFailure.Required, required.Failure);
        PdfPasswordException incorrect = await Assert.ThrowsAsync<PdfPasswordException>(() => DocumentFile.OpenAsync(path, new PdfPassword("wrong")));
        Assert.Equal(PdfPasswordFailure.Incorrect, incorrect.Failure);
        await using (DocumentFile file = await DocumentFile.OpenAsync(path, new PdfPassword("owner")))
        {
            Assert.True(file.Session.Document.IsEncrypted);
            await file.SaveAsync(directory.PathOf("encrypted-copy.pdf"));
        }

        using FileStream exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using PdfDocument copy = await PdfDocument.OpenAsync(directory.PathOf("encrypted-copy.pdf"), new PdfOptions().WithPassword("owner"), TestContext.Current.CancellationToken);
        Assert.True(copy.IsEncrypted);
    }

    private static void ChangeName(DocumentFile file, string value)
    {
        using IDisposable edit = file.Session.BeginEdit();
        ((PdfTextField)file.Session.Document.AcroForm!.FindField("name")!).SetValue(value);
        file.MarkChanged();
    }

    private static string? ReadName(string path)
    {
        using PdfDocument document = PdfDocument.Open(path);
        return ((PdfTextField)document.AcroForm!.FindField("name")!).Value;
    }
}

internal sealed class TestDirectory : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "Broadside.Reader.Tests-" + Guid.NewGuid().ToString("N"));

    public TestDirectory() => Directory.CreateDirectory(_path);

    public string PathOf(params string[] parts) => Path.Combine([_path, .. parts]);

    public string Copy(string file)
    {
        string destination = PathOf(file);
        File.Copy(Corpus.Path(file), destination);
        return destination;
    }

    public void Dispose()
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(_path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }
}
