using System.IO;
using System.Text.Json;

namespace Faderio.Core.Persistence;

/// <summary>Atomic JSON file write: write to a temp file, then rename over the target.</summary>
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
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, value, options);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
