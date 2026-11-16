using System.Globalization;

namespace SistecHub.Modulos.AcessoRemoto;

internal static class AcessoRemotoFileSystem
{
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

            if (trimmed.Length <= 7)
                return desktop;

            var rel = trimmed[8..].TrimStart('\\', '/');
            return Path.Combine(desktop, rel);
        }

        return trimmed;
    }

    public static (List<AcessoRemotoFileEntry> Entries, List<string> Drives, string? ErrorMessage) ListDirectory(string? dirPath)
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
            return (driveEntries, drives, null);
        }

        try
        {
            var cleanPath = Path.GetFullPath(ResolvePath(dirPath));
            var dirInfo = new DirectoryInfo(cleanPath);
            if (!dirInfo.Exists)
                return (new List<AcessoRemotoFileEntry>(), drives, $"Diretório não encontrado: {cleanPath}");

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

            return (result, drives, null);
        }
        catch (Exception ex)
        {
            return (new List<AcessoRemotoFileEntry>(), drives, $"falha ao ler diretório: {ex.Message}");
        }
    }

    public static (byte[] Chunk, long TotalSize, bool IsLast) ReadFileChunk(string filePath, long offset, int chunkSize)
    {
        filePath = ResolvePath(filePath);
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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

    public static void WriteFileChunk(string filePath, long offset, byte[] chunk)
    {
        filePath = ResolvePath(filePath);
        var parentDir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(parentDir))
            Directory.CreateDirectory(parentDir);

        var mode = offset == 0 ? FileMode.Create : FileMode.OpenOrCreate;
        using var fs = new FileStream(filePath, mode, FileAccess.Write, FileShare.Read);
        fs.Seek(offset, SeekOrigin.Begin);
        fs.Write(chunk, 0, chunk.Length);
    }

    public static void DeleteFileOrDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        path = ResolvePath(path);
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

