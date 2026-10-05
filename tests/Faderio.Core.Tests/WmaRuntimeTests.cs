using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>
/// The Windows Media Format runtime, the WMA decoder in both hosts (issue 12), measured on the
/// matrix's WMA fixtures, and the index that makes its seeks exact (<see cref="WmaIndex"/>).
/// Issue 12's first question was whether its sample stamps are exact after a seek. They are
/// not, and these pin why the reader does not trust them:
/// <list type="bullet">
/// <item>Stamps are whole milliseconds, 44 or 48 frames.</item>
/// <item>A seek lands up to 2 ms after where its first stamp says, on Windows' files; ffmpeg's
/// stamp the file's first sample about one block late, and land within 2 ms of that.</item>
/// <item>What a seek delivers after its first sample is cut at the boundaries of a decode from
/// the start, each stamped within a millisecond of that decode's stamp: the index's premise.</item>
/// </list>
/// The runtime seeks in milliseconds anywhere in a file: 1 to 17 ms 26 minutes in, measured on
/// files from both encoders (2026-10-05, 25 MB each); the 10-minute fixture pins a deep seek.
/// </summary>
public sealed class WmaRuntimeTests
{
    /// <summary>WMA 9 Standard from both encoders, then WMA 9 Lossless from Windows': encoder,
    /// rate, channels and the depth the runtime decodes to.</summary>
    public static IEnumerable<object[]> Cells() =>
        (from encoder in new[] { "windows", "ffmpeg" }
         from rate in new[] { 44100, 48000 }
         from channels in new[] { 1, 2 }
         where !(encoder == "windows" && rate == 48000 && channels == 1)
         select new object[] { encoder, rate, channels, 16 })
        .Concat(LosslessCells());

    public static IEnumerable<object[]> LosslessCells() =>
        from cell in new[] { (44100, 16), (44100, 24), (48000, 24), (96000, 24) }
        select new object[] { "windowslossless", cell.Item1, 2, cell.Item2 };

    /// <summary>WMA 9 Standard from Windows' encoder.</summary>
    public static IEnumerable<object[]> WindowsCells() => Cells().Where(c => (string)c[0] == "windows");

    private static string Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        Assert.True(File.Exists(path), $"fixture {name} is missing; run Fixtures/generate.sh");
        return path;
    }

    private static string Noise(string encoder, int rate, int channels, int bits = 16) =>
        Fixture(encoder == "windowslossless"
            ? $"noise-{encoder}-{rate}-{bits}-{channels}.wma"
            : $"noise-{encoder}-{rate}-{channels}.wma");

    private const long Millisecond = 10_000;

    private static long Frames(long stamp, int rate) => (long)Math.Round(stamp * (double)rate / 10_000_000);

    /// <summary>One sample the runtime delivered: its stamp, the frame it starts at counted from
    /// the start, and its left channel.</summary>
    private sealed record Sample(long Stamp, long Start, float[] Left);

    private static IEnumerable<Sample> Samples(WmSyncReader reader, long start = 0)
    {
        var buffer = new byte[0];
        var align = reader.Format.BlockAlign;
        while (reader.Next(ref buffer, out var stamp, out var bytes))
        {
            var left = new float[bytes / align];
            for (var f = 0; f < left.Length; f++)
            {
                var at = f * align;
                left[f] = reader.Format.BitsPerSample == 16
                    ? BitConverter.ToInt16(buffer, at) / 32768f
                    : (buffer[at] | buffer[at + 1] << 8 | (sbyte)buffer[at + 2] << 16) / 8388608f;
            }
            yield return new Sample(stamp, start, left);
            start += left.Length;
        }
    }

    private static List<Sample> Sequential(string path)
    {
        using var file = File.OpenRead(path);
        using var reader = WmSyncReader.Open(file, Path.GetFileName(path));
        return Samples(reader).ToList();
    }

    /// <summary>25 targets spread through the noise, past the decode's first two samples, which a
    /// seek reaches by decoding from the start. The last is in the last block of the 2 s of
    /// noise, or of the decode where ffmpeg's is shorter, with a block to read after it: never
    /// in the codec tail Windows' files decode past the source, which locates nowhere.</summary>
    private static IEnumerable<long> Targets(int rate, long frames)
    {
        var last = Math.Min(frames, 2L * rate) - 1100;
        return Enumerable.Range(0, 25).Select(i => (long)(rate / 5 + i * ((last - rate / 5) / 24.0)));
    }

    [Theory, MemberData(nameof(Cells))]
    public void TheRuntime_DecodesToPcmAtTheFilesOwnRateAndDepth(string encoder, int rate, int channels, int bits)
    {
        // Its default is not always the file's own: 24-bit Lossless decoded to 16-bit, and
        // 96 kHz to 48 kHz, until the reader asked for what the stream was encoded from.
        using var file = File.OpenRead(Noise(encoder, rate, channels, bits));
        using var reader = WmSyncReader.Open(file, "noise");

        Assert.Equal(encoder == "windowslossless" ? 0x163 : 0x161, reader.CodecTag);
        Assert.Equal(new WmPcmFormat(rate, channels, bits), reader.Format);
    }

    [Theory, MemberData(nameof(LosslessCells))]
    public void Lossless_DecodesToTheSourceExactly(string encoder, int rate, int channels, int bits)
    {
        // From sample 0, no codec tail, no sample lost, to the depth's last bit: the source is the
        // 24-bit FLAC of the same noise, which a 16-bit cell matches to its own last bit.
        var source = TestAudio.LeftChannel(TrackSources.Open(Fixture($"noise-{rate}-24-{channels}.flac")));
        var decoded = TestAudio.LeftChannel(TrackSources.Open(Noise(encoder, rate, channels, bits)));

        Assert.Equal(source.Length, decoded.Length);
        var step = bits == 16 ? 1.0 / 32768 : 1.0 / 8388608;
        Assert.All(Enumerable.Range(0, source.Length), i => Assert.InRange(Math.Abs(source[i] - decoded[i]), 0, step));
    }

    [Theory, MemberData(nameof(Cells))]
    public void Stamps_AreWholeMilliseconds(string encoder, int rate, int channels, int bits)
    {
        Assert.All(Sequential(Noise(encoder, rate, channels, bits)), s => Assert.Equal(0, s.Stamp % Millisecond));
    }

    [Theory, MemberData(nameof(WindowsCells))]
    public void ASeek_OnWindowsEncodedWma_LandsUpTo2msAfterItsStamp_SoStampsAreNotTheSample(string encoder, int rate, int channels, int bits)
    {
        var path = Noise(encoder, rate, channels);
        var sequential = Sequential(path);
        var reference = sequential.SelectMany(s => s.Left).ToArray();
        var errors = new List<long>();
        foreach (var target in Targets(rate, reference.Length))
        {
            using var file = File.OpenRead(path);
            using var reader = WmSyncReader.Open(file, "noise");
            reader.SetRange(target * 10_000_000 / rate);
            var first = Samples(reader).First();
            var (landed, similarity) = TestAudio.Locate(reference, first.Left.AsSpan(0, Math.Min(512, first.Left.Length)),
                (int)target - rate / 10, (int)target + rate / 10);
            Assert.True(similarity > 0.99, $"the first sample after a seek to {target} matches nowhere near it ({similarity:F3})");
            errors.Add(landed - Frames(first.Stamp, rate));
        }

        var report = string.Join(" ", errors);
        Assert.True(errors.All(e => e >= 0 && e <= 2 * rate / 1000), $"frames landed after the stamp: {report}");
        Assert.True(errors.Any(e => e != 0), $"every seek landed on its stamp, so stamps may now be exact: {report}");
    }

    [Theory, MemberData(nameof(Cells))]
    public void ASeek_DeliversADecodeFromTheStart_WhoseWholeSamplesAreStampedWithin1msOfIt(string encoder, int rate, int channels, int bits)
    {
        // The index's premise. After the first sample, cut where the runtime landed, comes the
        // decode from the start sample for sample, except that a seek can split one of its
        // samples in two: the halves' stamps are far from any of its stamps, so the index never
        // takes one for a sample it knows.
        var path = Noise(encoder, rate, channels, bits);
        var sequential = Sequential(path);
        var reference = sequential.SelectMany(s => s.Left).ToArray();
        var byStart = sequential.ToDictionary(s => s.Start);
        var split = 0;
        foreach (var target in Targets(rate, reference.Length))
        {
            using var file = File.OpenRead(path);
            using var reader = WmSyncReader.Open(file, "noise");
            reader.SetRange(target * 10_000_000 / rate);
            // Placed by the second sample: the first can be a few frames, too few to locate, and
            // Lossless can land up to 0.4 s after the time asked for.
            var delivered = Samples(reader).Take(11).ToList();
            if (delivered.Count < 2)
            {
                continue; // a target in the last sample
            }
            var second = delivered[1];
            // Within 0.4 s after the target at worst (Lossless); a block of 512 still locates.
            var (landed, _) = TestAudio.Locate(reference, second.Left.AsSpan(0, Math.Min(512, second.Left.Length)),
                (int)target - rate / 10, (int)target + rate / 2);
            var start = landed;
            foreach (var after in delivered.Skip(1).Select(d => { var placed = d with { Start = start }; start += d.Left.Length; return placed; }))
            {
                Assert.Equal(reference.AsSpan((int)after.Start, after.Left.Length).ToArray(), after.Left);
                if (byStart.TryGetValue(after.Start, out var same) && same.Left.Length == after.Left.Length)
                {
                    Assert.InRange(after.Stamp - same.Stamp, -Millisecond, Millisecond);
                }
                else
                {
                    split++;
                    Assert.All(sequential, s => Assert.True(Math.Abs(s.Stamp - after.Stamp) > 3 * Millisecond,
                        $"a split sample at {after.Start} is stamped near the whole one at {s.Start}"));
                }
            }
        }
        Assert.True(split < 25, $"{split} of 250 samples after a seek were split");
    }

    [Theory, MemberData(nameof(Cells))]
    public void TheReader_SeeksToTheSample(string encoder, int rate, int channels, int bits)
    {
        // The matrix proves 1 ms through the normaliser; in the file's own frames the index
        // places every seek exactly, deep or near the start.
        var path = Noise(encoder, rate, channels, bits);
        var reference = TestAudio.LeftChannel(TrackSources.Open(path));
        foreach (var target in Targets(rate, reference.Length).Prepend(rate / 50))
        {
            using var source = TrackSources.Open(path);
            source.Position = target;
            Assert.Equal(target, source.Position);
            // In the first two samples, decoding from the start is the quick way; past them, the
            // index places it.
            Assert.True((target < rate / 5 ? 1 : 0) == ((WmaTrackReader)source).SeeksFromTheStart,
                $"a seek to {target} decoded from the start: {((WmaTrackReader)source).SeeksFromTheStart}");
            var block = new float[1024 * channels];
            Assert.Equal(1024, source.Read(block, 0, 1024));
            var left = Enumerable.Range(0, 1024).Select(f => block[f * channels]).ToArray();
            Assert.Equal(reference.AsSpan((int)target, 1024).ToArray(), left);
        }
    }

    [Fact]
    public void TheReader_SeekingBackAndForth_ReadsWhatADecodeFromTheStartReads()
    {
        var path = Noise("windows", 44100, 2);
        var reference = TestAudio.LeftChannel(TrackSources.Open(path));
        using var source = TrackSources.Open(path);
        var block = new float[2048 * 2];
        foreach (var target in new long[] { 60000, 3000, 85000, 85000, 20000, 0, 89000 })
        {
            source.Position = target;
            var n = source.Read(block, 0, 1000);
            Assert.Equal(Math.Min(1000, reference.Length - target), n);
            Assert.Equal(reference.AsSpan((int)target, n).ToArray(), Enumerable.Range(0, n).Select(f => block[f * 2]).ToArray());
            Assert.Equal(target + n, source.Position);
        }
    }

    private static string Copy(string fixture)
    {
        var path = Path.Combine(TestTemp.Root, $"wma-{Guid.NewGuid():N}.wma");
        File.Copy(Fixture(fixture), path);
        return path;
    }

    private static WmaIndex IndexOf(WmaIndexStore store, string path)
    {
        using var file = File.OpenRead(path);
        return store.For(path, file, CancellationToken.None);
    }

    [Fact]
    public void TheIndex_IsKeptOnDisk_AndReadBackForTheSameAudio()
    {
        var folder = Path.Combine(TestTemp.Root, "wma-index-" + Guid.NewGuid().ToString("N"));
        var path = Copy("noise-windows-44100-2.wma");
        var first = new WmaIndexStore { Folder = folder };
        var built = IndexOf(first, path);
        Assert.Equal(1, first.Built);
        Assert.Single(Directory.GetFiles(folder, "*.wmaindex"));

        // A second store, a library opened again, reads it rather than decoding.
        var second = new WmaIndexStore { Folder = folder };
        var read = IndexOf(second, path);
        Assert.Equal(0, second.Built);
        Assert.Equal(built.TotalFrames, read.TotalFrames);
        Assert.Equal(built.Starts, read.Starts);
        Assert.Equal(built.Stamps, read.Stamps);

        // Other audio under the same name is decoded again.
        File.Copy(Fixture("noise-windows-48000-2.wma"), path, overwrite: true);
        var third = new WmaIndexStore { Folder = folder };
        Assert.NotEqual(built.TotalFrames, IndexOf(third, path).TotalFrames);
        Assert.Equal(1, third.Built);
    }

    [Fact]
    public void ATagEdit_KeepsTheIndex()
    {
        // MusicBee rewrites tags often. They live in the ASF header, so the audio, and the
        // last mebibyte the index is keyed by, stay the same while the size and time change.
        var folder = Path.Combine(TestTemp.Root, "wma-index-" + Guid.NewGuid().ToString("N"));
        var path = Copy("long-ffmpeg-44100-1.wma");
        var built = IndexOf(new WmaIndexStore { Folder = folder }, path);
        var size = new FileInfo(path).Length;

        using (var tagged = TagLib.File.Create(path))
        {
            tagged.Tag.Title = "A title long enough to move the audio along the file " + new string('x', 300);
            tagged.Save();
        }

        Assert.NotEqual(size, new FileInfo(path).Length);
        var again = new WmaIndexStore { Folder = folder };
        Assert.Equal(built.TotalFrames, IndexOf(again, path).TotalFrames);
        Assert.Equal(0, again.Built);
    }

    [Fact]
    public void ASeekIntoTheLastMinuteOfALongWma_IsQuick_AndOnTheSample()
    {
        // 10 minutes, the longest fixture the repo carries (2 MB). The runtime's seek does not
        // walk the file: 1 to 17 ms 26 minutes in was measured on 25 MB files, and the index
        // places it without decoding from the start.
        var path = Fixture("long-ffmpeg-44100-1.wma");
        using var source = (WmaTrackReader)TrackSources.Open(path);
        var target = source.TotalFrames - 30L * 44100;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        source.Position = target;
        var block = new float[1024];
        Assert.Equal(1024, source.Read(block, 0, 1024));
        clock.Stop();

        Assert.Equal(0, source.SeeksFromTheStart);
        Assert.True(clock.ElapsedMilliseconds < 250, $"the seek took {clock.ElapsedMilliseconds} ms; the open budget is 500");
        // On the sample, located in a decode from the start as the matrix locates a landing. At
        // this bitrate the codec fills bands with noise from a generator whose state is the
        // whole decode before, so after a seek the samples are close to a decode from the start
        // but not equal to it; where they sit is still exact.
        using var sequential = TrackSources.Open(path);
        var skip = new float[44100];
        for (var left = target - 22050; left > 0;)
        {
            left -= sequential.Read(skip, 0, (int)Math.Min(skip.Length, left));
        }
        var around = new float[44100];
        Assert.Equal(around.Length, sequential.Read(around, 0, around.Length));
        var (landed, similarity) = TestAudio.Locate(around, block);
        Assert.True(similarity > 0.9, $"the block read after the seek matches nowhere near it ({similarity:F3})");
        Assert.Equal(22050, landed);
    }

    [Fact]
    public void AReaderOpenedOnOneThread_SeeksAndReadsOnAnother_EvenAnStaThread()
    {
        // A boundary's track is opened by a worker and read by the render thread; a seek can
        // come from the UI thread, which is STA. The runtime's objects are called through their
        // raw vtables, never through a wrapper that would marshal them between apartments.
        var path = Noise("windows", 44100, 2);
        var reference = TestAudio.LeftChannel(TrackSources.Open(path));
        using var source = TrackSources.Open(path);
        Exception? failed = null;
        var block = new float[2048];
        var thread = new Thread(() =>
        {
            try
            {
                source.Position = 60000;
                source.Read(block, 0, 1024);
            }
            catch (Exception e)
            {
                failed = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failed);
        Assert.Equal(reference.AsSpan(60000, 1024).ToArray(), Enumerable.Range(0, 1024).Select(f => block[f * 2]).ToArray());
    }

    [Fact]
    public void TheProbe_RejectsWmaPro_ByItsCodec()
    {
        var path = Fixture("tone-windowspro-44100-2.wma");

        var rejection = AudioProbe.Probe(path, out var result);

        Assert.Null(result);
        Assert.NotNull(rejection);
        Assert.Equal(RejectedProperty.Codec, rejection!.Property);
        Assert.Equal("WMA 9 Pro is not a promised codec; only WMA 9 Standard and WMA 9 Lossless are.", rejection.Detail);
    }

    [Theory]
    [InlineData(48000, 16, 2, RejectedProperty.BitDepth)]
    [InlineData(96000, 16, 2, RejectedProperty.BitDepth)]
    [InlineData(32000, 24, 2, RejectedProperty.SampleRate)]
    [InlineData(44100, 16, 1, RejectedProperty.Channels)]
    public void LosslessOutsideItsFourCells_IsRejected(int rate, int bits, int channels, RejectedProperty property)
    {
        var shape = new AudioShape(AudioFormat.Wma, rate, bits, channels, SupportedAudio.WmaLossless);
        Assert.Equal(property, SupportedAudio.Check(shape));
    }

    [Theory]
    [InlineData(32000)]
    [InlineData(96000)]
    public void UnpromisedRates_AreRejected(int rate)
    {
        var shape = new AudioShape(AudioFormat.Wma, rate, null, 2, SupportedAudio.WmaStandard);
        Assert.Equal(RejectedProperty.SampleRate, SupportedAudio.Check(shape));
    }

    [Fact]
    public void AFileThatIsNotWma_IsRejectedAsUnreadable()
    {
        var path = Path.Combine(TestTemp.Root, $"not-{Guid.NewGuid():N}.wma");
        File.WriteAllBytes(path, new byte[4096]);

        var rejection = AudioProbe.Probe(path, out _);

        Assert.Equal(RejectedProperty.File, rejection!.Property);
    }
}
