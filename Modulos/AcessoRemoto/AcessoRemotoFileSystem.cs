using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;

namespace SistecHub.Modulos.AcessoRemoto;

internal static class AcessoRemotoFileSystem
{
    private sealed class ActiveUploadStream : IDisposable
    {
        public readonly object SyncRoot = new();
        public FileStream? Stream;

        public ActiveUploadStream(FileStream stream)
        {
            Stream = stream;
        }

        public void Dispose()
        {
            lock (SyncRoot)
            {
                try { Stream?.Dispose(); } catch { }
                Stream = null;
            }
        }
    }

    private static readonly ConcurrentDictionary<string, ActiveUploadStream> ActiveUploadStreams =
        new(StringComparer.OrdinalIgnoreCase);

    public static List<string> GetDrives()
    {
        var drives = new List<string>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (!string.IsNullOrWhiteSpace(d.Name))
                    drives.Add(d.Name);
            }
        }
        catch
        {
            // Fallback abaixo
        }

        if (drives.Count == 0)
            drives.Add(@"C:\");

        return drives;
    }

    public static string ResolvePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return @"C:\";

        var trimmed = rawPath.Trim();
        if (trimmed.StartsWith("DESKTOP\\", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("DESKTOP/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "DESKTOP", StringComparison.OrdinalIgnoreCase))
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
                desktop = @"C:\Users\Public\Desktop";

            var desktopRoot = Path.GetFullPath(desktop);
            if (trimmed.Length <= 7)
                return desktopRoot;

            var rel = trimmed[8..].Replace('/', '\\').TrimStart('\\');
            if (string.IsNullOrWhiteSpace(rel))
                return desktopRoot;

            var combined = Path.GetFullPath(Path.Combine(desktopRoot, rel));
            var desktopRootWithSep = desktopRoot.EndsWith(Path.DirectorySeparatorChar)
                ? desktopRoot
                : desktopRoot + Path.DirectorySeparatorChar;

            if (!combined.StartsWith(desktopRootWithSep, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(combined, desktopRoot, StringComparison.OrdinalIgnoreCase))
            {
                var safeName = Path.GetFileName(rel);
                return string.IsNullOrWhiteSpace(safeName) ? desktopRoot : Path.Combine(desktopRoot, safeName);
            }

            return combined;
        }

        return trimmed;
    }

    public static (List<AcessoRemotoFileEntry> Entries, List<string> Drives, string ResolvedPath, string? ErrorMessage) ListDirectory(string? dirPath)
    {
        var drives = GetDrives();

        if (string.IsNullOrWhiteSpace(dirPath) || string.Equals(dirPath.Trim(), "DRIVES", StringComparison.OrdinalIgnoreCase))
        {
            var driveEntries = drives
                .Select(d => new AcessoRemotoFileEntry
                {
                    Name = d,
                    IsDir = true,
                    Size = 0,
                    ModTime = "-",
                })
                .ToList();
            return (driveEntries, drives, "DRIVES", null);
        }

        string cleanPath;
        try
        {
            cleanPath = Path.GetFullPath(ResolvePath(dirPath));
        }
        catch
        {
            cleanPath = ResolvePath(dirPath);
        }

        try
        {
            var dirInfo = new DirectoryInfo(cleanPath);
            if (!dirInfo.Exists)
                return (new List<AcessoRemotoFileEntry>(), drives, cleanPath, $"Diretório não encontrado: {cleanPath}");

            var dirs = new List<AcessoRemotoFileEntry>();
            var files = new List<AcessoRemotoFileEntry>();

            foreach (var entry in dirInfo.EnumerateFileSystemInfos())
            {
                try
                {
                    bool isDir = (entry.Attributes & FileAttributes.Directory) != 0;
                    long size = 0;
                    if (!isDir && entry is FileInfo fi)
                        size = fi.Length;

                    var item = new AcessoRemotoFileEntry
                    {
                        Name = entry.Name,
                        IsDir = isDir,
                        Size = size,
                        ModTime = entry.LastWriteTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                    };

                    if (isDir)
                        dirs.Add(item);
                    else
                        files.Add(item);
                }
                catch
                {
                    // Ignora entradas inacessíveis individualmente
                }
            }

            dirs.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            files.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            var result = new List<AcessoRemotoFileEntry>(dirs.Count + files.Count);
            result.AddRange(dirs);
            result.AddRange(files);

            return (result, drives, cleanPath, null);
        }
        catch (Exception ex)
        {
            return (new List<AcessoRemotoFileEntry>(), drives, cleanPath, $"falha ao ler diretório: {ex.Message}");
        }
    }

    public static (byte[] Chunk, long TotalSize, bool IsLast) ReadFileChunk(string filePath, long offset, int chunkSize)
    {
        filePath = ResolvePath(filePath);
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 64 * 1024, FileOptions.SequentialScan);
        long totalSize = fs.Length;

        if (offset >= totalSize)
            return (Array.Empty<byte>(), totalSize, true);

        fs.Seek(offset, SeekOrigin.Begin);
        var buf = new byte[chunkSize];
        int read = fs.Read(buf, 0, chunkSize);
        if (read < buf.Length)
            Array.Resize(ref buf, read);

        bool isLast = (offset + read) >= totalSize;
        return (buf, totalSize, isLast);
    }

    public static void WriteFileChunk(string filePath, long offset, byte[] chunk, bool isLast = false)
    {
        var resolvedPath = Path.GetFullPath(ResolvePath(filePath));
        var parentDir = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(parentDir))
            Directory.CreateDirectory(parentDir);

        // Caminho rápido para arquivos enviados em chunk único (<= 256 KB)
        if (offset == 0 && isLast)
        {
            CloseActiveUpload(resolvedPath);
            using var singleFs = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 64 * 1024);
            if (chunk.Length > 0)
                singleFs.Write(chunk, 0, chunk.Length);
            return;
        }

        if (offset == 0)
        {
            CloseActiveUpload(resolvedPath);
            var fs = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 64 * 1024);
            var active = new ActiveUploadStream(fs);
            try
            {
                if (chunk.Length > 0)
                    fs.Write(chunk, 0, chunk.Length);
                ActiveUploadStreams[resolvedPath] = active;
            }
            catch
            {
                active.Dispose();
                throw;
            }
            return;
        }

        if (!ActiveUploadStreams.TryGetValue(resolvedPath, out var existingEntry))
        {
            var fs = new FileStream(resolvedPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, bufferSize: 64 * 1024);
            var newEntry = new ActiveUploadStream(fs);
            existingEntry = ActiveUploadStreams.GetOrAdd(resolvedPath, newEntry);
            if (!ReferenceEquals(existingEntry, newEntry))
                newEntry.Dispose();
        }

        try
        {
            lock (existingEntry.SyncRoot)
            {
                var s = existingEntry.Stream;
                if (s == null)
                {
                    s = new FileStream(resolvedPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, bufferSize: 64 * 1024);
                    existingEntry.Stream = s;
                }

                if (s.Position != offset)
                    s.Seek(offset, SeekOrigin.Begin);

                if (chunk.Length > 0)
                    s.Write(chunk, 0, chunk.Length);
            }
        }
        catch
        {
            CloseActiveUpload(resolvedPath);
            throw;
        }

        if (isLast)
        {
            CloseActiveUpload(resolvedPath);
        }
    }

    public static void CloseActiveUpload(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        string resolvedPath;
        try
        {
            resolvedPath = Path.GetFullPath(ResolvePath(filePath));
        }
        catch
        {
            resolvedPath = ResolvePath(filePath);
        }

        if (ActiveUploadStreams.TryRemove(resolvedPath, out var active))
        {
            active.Dispose();
        }
    }

    public static void CloseAllActiveUploads()
    {
        foreach (var key in ActiveUploadStreams.Keys)
        {
            if (ActiveUploadStreams.TryRemove(key, out var active))
            {
                active.Dispose();
                try
                {
                    var fileName = Path.GetFileName(key);
                    if (fileName.StartsWith("sistec_extract_", StringComparison.OrdinalIgnoreCase) &&
                        fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(key))
                    {
                        File.Delete(key);
                    }
                }
                catch
                {
                }
            }
        }
    }

    public static void ExtractZipSafe(string zipFilePath, string destinationDir)
    {
        var destFullPath = Path.GetFullPath(ResolvePath(destinationDir));
        Directory.CreateDirectory(destFullPath);

        var destRootWithSep = destFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? destFullPath
            : destFullPath + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipFilePath);
        foreach (var entry in archive.Entries)
        {
            var rawRelPath = entry.FullName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);

            if (string.IsNullOrWhiteSpace(rawRelPath))
                continue;

            bool isDirectoryEntry = string.IsNullOrEmpty(entry.Name) ||
                                    entry.FullName.EndsWith('/') ||
                                    entry.FullName.EndsWith('\\');

            var safeRelPath = AcessoRemotoClipboard.SanitizeRelativePath(rawRelPath, entry.Name);
            if (string.IsNullOrWhiteSpace(safeRelPath))
                continue;

            string targetPath;
            try
            {
                targetPath = Path.GetFullPath(Path.Combine(destFullPath, safeRelPath));
            }
            catch
            {
                continue;
            }

            if (!targetPath.StartsWith(destRootWithSep, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(targetPath, destFullPath, StringComparison.OrdinalIgnoreCase))
            {
                // Bloqueia tentativa de Path Traversal (ZipSlip)
                continue;
            }

            if (isDirectoryEntry)
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            var parentDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(parentDir))
                Directory.CreateDirectory(parentDir);

            CloseActiveUpload(targetPath);
            using (var entryStream = entry.Open())
            using (var outStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 64 * 1024))
            {
                entryStream.CopyTo(outStream);
            }

            try
            {
                File.SetLastWriteTimeUtc(targetPath, entry.LastWriteTime.UtcDateTime);
            }
            catch
            {
                // Ignora caso não seja possível restaurar o timestamp
            }
        }
    }

    public static void DeleteFileOrDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        path = ResolvePath(path);
        CloseActiveUpload(path);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }

    public static void CreateDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        path = ResolvePath(path);
        Directory.CreateDirectory(path);
    }
}

