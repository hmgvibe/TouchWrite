// PromoEncoder <framesDir> <fps> <audio.wav> <out.mp4> [videoBitrateMbps]
// Frames are 00000.jpg, 00001.jpg, ... all the same size. Audio: 16-bit PCM WAV.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;

if (args.Length < 4)
{
    Console.WriteLine("usage: PromoEncoder <framesDir> <fps> <audio.wav> <out.mp4> [videoMbps]");
    return 1;
}

string framesDir = Path.GetFullPath(args[0]);
uint fps = uint.Parse(args[1]);
string wavPath = Path.GetFullPath(args[2]);
string outPath = Path.GetFullPath(args[3]);
uint mbps = args.Length > 4 ? uint.Parse(args[4]) : 20;

var frames = Directory.GetFiles(framesDir, "*.jpg").OrderBy(f => f, StringComparer.Ordinal).ToArray();
if (frames.Length == 0) { Console.WriteLine("no frames"); return 1; }

// ---- probe size
uint width, height;
{
    var f = await StorageFile.GetFileFromPathAsync(frames[0]);
    using var s = await f.OpenReadAsync();
    var dec = await BitmapDecoder.CreateAsync(s);
    width = dec.PixelWidth; height = dec.PixelHeight;
}

// ---- audio
var wav = Wav.Read(wavPath);
Console.WriteLine($"{frames.Length} frames {width}x{height} @ {fps} fps, audio {wav.SampleRate} Hz x{wav.Channels}, {wav.Data.Length / (wav.Channels * 2) / (double)wav.SampleRate:0.00}s");

TimeSpan frameDur = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / fps);
TimeSpan videoLength = TimeSpan.FromTicks(frameDur.Ticks * frames.Length);

// ---- decode frames ahead of the encoder on several threads
var decoded = new ConcurrentDictionary<int, byte[]>();
int nextToDecode = -1;
const int Lookahead = 48;
var gate = new SemaphoreSlim(Lookahead);
var decoders = Enumerable.Range(0, Math.Max(2, Environment.ProcessorCount / 2)).Select(_ => Task.Run(async () =>
{
    while (true)
    {
        await gate.WaitAsync();
        int i = Interlocked.Increment(ref nextToDecode);
        if (i >= frames.Length) { gate.Release(); return; }
        var file = await StorageFile.GetFileFromPathAsync(frames[i]);
        using var stream = await file.OpenReadAsync();
        var dec = await BitmapDecoder.CreateAsync(stream);
        var px = await dec.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var top = px.DetachPixelData();
        // Uncompressed RGB in Media Foundation is bottom-up: store the rows in reverse order.
        int stride = (int)dec.PixelWidth * 4, rows = (int)dec.PixelHeight;
        var flipped = new byte[top.Length];
        for (int y = 0; y < rows; y++) System.Buffer.BlockCopy(top, y * stride, flipped, (rows - 1 - y) * stride, stride);
        decoded[i] = flipped;
    }
})).ToArray();

// ---- media stream source
var vProps = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, width, height);
vProps.FrameRate.Numerator = fps; vProps.FrameRate.Denominator = 1;
var vDesc = new VideoStreamDescriptor(vProps);
var aProps = AudioEncodingProperties.CreatePcm((uint)wav.SampleRate, (uint)wav.Channels, 16);
var aDesc = new AudioStreamDescriptor(aProps);
var mss = new MediaStreamSource(vDesc, aDesc) { BufferTime = TimeSpan.Zero, Duration = videoLength };

int videoIndex = 0;
long audioByteOffset = 0;
int bytesPerAudioFrame = wav.Channels * 2;
int audioChunkFrames = wav.SampleRate / 50; // 20 ms
long audioBytesTotal = Math.Min(wav.Data.Length, (long)(videoLength.TotalSeconds * wav.SampleRate) * bytesPerAudioFrame);
var sw = Stopwatch.StartNew();

mss.SampleRequested += (s, e) =>
{
    var req = e.Request;
    if (req.StreamDescriptor == vDesc)
    {
        if (videoIndex >= frames.Length) { req.Sample = null; return; }
        var deferral = req.GetDeferral();
        int i = videoIndex++;
        _ = Task.Run(async () =>
        {
            byte[]? px;
            while (!decoded.TryRemove(i, out px)) await Task.Delay(2);
            gate.Release();
            var sample = MediaStreamSample.CreateFromBuffer(px.AsBuffer(), TimeSpan.FromTicks(frameDur.Ticks * i));
            sample.Duration = frameDur;
            sample.KeyFrame = true;
            req.Sample = sample;
            if (i % 120 == 0) Console.WriteLine($"  frame {i}/{frames.Length}  {sw.Elapsed.TotalSeconds:0}s");
            deferral.Complete();
        });
    }
    else
    {
        if (audioByteOffset >= audioBytesTotal) { req.Sample = null; return; }
        int len = (int)Math.Min(audioChunkFrames * bytesPerAudioFrame, audioBytesTotal - audioByteOffset);
        var chunk = new byte[len];
        System.Buffer.BlockCopy(wav.Data, (int)audioByteOffset, chunk, 0, len);
        var ts = TimeSpan.FromTicks(audioByteOffset / bytesPerAudioFrame * TimeSpan.TicksPerSecond / wav.SampleRate);
        var sample = MediaStreamSample.CreateFromBuffer(chunk.AsBuffer(), ts);
        sample.Duration = TimeSpan.FromTicks((long)len / bytesPerAudioFrame * TimeSpan.TicksPerSecond / wav.SampleRate);
        audioByteOffset += len;
        req.Sample = sample;
    }
};

// ---- encode
var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
profile.Video.Width = width;
profile.Video.Height = height;
profile.Video.Bitrate = mbps * 1_000_000;
profile.Video.FrameRate.Numerator = fps;
profile.Video.FrameRate.Denominator = 1;
profile.Video.PixelAspectRatio.Numerator = 1;
profile.Video.PixelAspectRatio.Denominator = 1;
profile.Audio = AudioEncodingProperties.CreateAac((uint)wav.SampleRate, (uint)wav.Channels, 192_000);

if (File.Exists(outPath)) File.Delete(outPath);
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outPath)!);
var outFile = await folder.CreateFileAsync(Path.GetFileName(outPath), CreationCollisionOption.ReplaceExisting);
using (var outStream = await outFile.OpenAsync(FileAccessMode.ReadWrite))
{
    var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true, AlwaysReencode = true };
    var prep = await transcoder.PrepareMediaStreamSourceTranscodeAsync(mss, outStream, profile);
    if (!prep.CanTranscode) { Console.WriteLine("cannot transcode: " + prep.FailureReason); return 2; }
    await prep.TranscodeAsync();
}
Console.WriteLine($"done in {sw.Elapsed.TotalSeconds:0.0}s -> {outPath} ({new FileInfo(outPath).Length / 1048576.0:0.0} MB)");
return 0;

internal sealed class Wav
{
    public int SampleRate;
    public int Channels;
    public byte[] Data = Array.Empty<byte>();

    public static Wav Read(string path)
    {
        using var br = new BinaryReader(File.OpenRead(path));
        if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("not RIFF");
        br.ReadInt32();
        if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("not WAVE");
        var w = new Wav();
        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            string id = new string(br.ReadChars(4));
            int size = br.ReadInt32();
            if (id == "fmt ")
            {
                short fmt = br.ReadInt16();
                w.Channels = br.ReadInt16();
                w.SampleRate = br.ReadInt32();
                br.ReadInt32(); br.ReadInt16();
                short bits = br.ReadInt16();
                if (fmt != 1 || bits != 16) throw new InvalidDataException("need 16-bit PCM");
                br.BaseStream.Seek(size - 16, SeekOrigin.Current);
            }
            else if (id == "data") { w.Data = br.ReadBytes(size); break; }
            else br.BaseStream.Seek(size + (size & 1), SeekOrigin.Current);
        }
        return w;
    }
}
