namespace Fadem.Core.Tests;

/// <summary>
/// The replacing move behind every atomic write. Windows refuses to replace a file another
/// handle has open, in any sharing mode. A reader that holds it a moment (a test polling the
/// playlist, an antivirus scan, another player) once cost a playlist write: the write after a
/// drop failed with access denied, and nothing wrote again until the queue next changed.
/// </summary>
public sealed class MoveReplacingTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestTemp.Root, "move-replacing", Guid.NewGuid().ToString("N"));

    public MoveReplacingTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private (string Path, string Next) Files()
    {
        var path = Path.Combine(_folder, "Untitled.m3u8");
        File.WriteAllText(path, "three rows");
        var next = Path.Combine(_folder, "next.tmp");
        File.WriteAllText(next, "four rows");
        return (path, next);
    }

    [Fact]
    public void AFileAReaderHoldsAMoment_IsReplacedOnceItLetsGo()
    {
        var (path, next) = Files();
        var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); // as File.ReadAllLines opens it
        var release = Task.Run(async () =>
        {
            await Task.Delay(50);
            reader.Dispose();
        });

        FileSystemCompat.MoveReplacing(next, path);

        release.Wait();
        Assert.Equal("four rows", File.ReadAllText(path));
        Assert.False(File.Exists(next));
    }

    [Fact]
    public void AFileAReaderNeverLetsGo_IsStillAFailure_AndTheNewFileIsKept()
    {
        var (path, next) = Files();

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() =>
            {
                try
                {
                    FileSystemCompat.MoveReplacing(next, path);
                }
                catch (UnauthorizedAccessException e)
                {
                    throw new IOException(e.Message, e); // either is a store write failure
                }
            });
        }

        Assert.Equal("three rows", File.ReadAllText(path));
        Assert.Equal("four rows", File.ReadAllText(next));
    }
}
