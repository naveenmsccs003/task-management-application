using System.Text.RegularExpressions;
using TaskManagement.Application.Attachments;

namespace TaskManagement.Infrastructure.Files;

/// <summary>
/// Private storage on a local disk or mounted volume (A10): files are written to a temporary name and moved into place,
/// so a reader never sees a half-written file. Keys must look like <c>2026/10/&lt;32 hex&gt;</c>, so a path can never
/// leave the root folder (security.md S12).
/// </summary>
public sealed partial class LocalFileStorage : IFileStorage
{
    private const int BufferSize = 81_920;
    private readonly string _root;

    public LocalFileStorage(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partial = path + ".partial";
        try
        {
            await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(file, BufferSize, cancellationToken);
                await file.FlushAsync(cancellationToken);
            }

            File.Move(partial, path);
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The stored file is missing.", key);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        File.Delete(PathFor(key));
        return Task.CompletedTask;
    }

    /// <summary>For the readiness check: the folder exists and a file can be written and removed.</summary>
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var probe = Path.Combine(_root, $".probe-{Guid.NewGuid():N}");
        await File.WriteAllBytesAsync(probe, [1], cancellationToken);
        File.Delete(probe);
    }

    private string PathFor(string key)
    {
        if (!KeyPattern().IsMatch(key))
        {
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }

        return Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
    }

    [GeneratedRegex("^[0-9]{4}/[0-9]{2}/[0-9a-f]{32}$")]
    private static partial Regex KeyPattern();
}
