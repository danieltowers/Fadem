using System.IO;
using System.Text.Json;

namespace Faderio.Core.Persistence;

/// <summary>Atomic JSON file write: write to a temp file, then rename over the target. A write
/// that fails leaves the target as it was and no temp file behind.</summary>
public static class AtomicJson
{
    public static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, value, options);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            throw;
        }
    }
}
