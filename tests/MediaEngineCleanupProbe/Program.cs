using System.Diagnostics;
using FengBroPlayer.Services;

if (args.Length == 0)
    throw new ArgumentException("Pass a local media path.");

var mediaPath = Path.GetFullPath(args[0]);
var switchCount = args.Length > 1 ? int.Parse(args[1]) : 100;
if (!File.Exists(mediaPath))
    throw new FileNotFoundException("Probe media was not found.", mediaPath);

// Warm up LibVLC so process-wide native initialization is not counted as a leak.
using (var warmup = new MediaEngine())
{
}
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

var process = Process.GetCurrentProcess();
process.Refresh();
var baselineHandles = process.HandleCount;
var baselinePrivateBytes = process.PrivateMemorySize64;

var engine = new MediaEngine();
for (var index = 0; index < switchCount; index++)
{
    if (!engine.Play(mediaPath))
        throw new InvalidOperationException($"Media switch {index + 1} failed.");
    Thread.Sleep(5);
}

var disposeTimer = Stopwatch.StartNew();
engine.Dispose();
disposeTimer.Stop();
if (disposeTimer.ElapsedMilliseconds > 4000)
    throw new InvalidOperationException(
        $"Dispose hung for {disposeTimer.ElapsedMilliseconds}ms; native Stop/release must not block shutdown.");

process.Refresh();
var handlesAfterDispose = process.HandleCount;
var privateBytesAfterDispose = process.PrivateMemorySize64;

var finalizerTimer = Stopwatch.StartNew();
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
finalizerTimer.Stop();
process.Refresh();

Console.WriteLine(
    "switches={0} baseline-handles={1} after-dispose-handles={2} after-gc-handles={3} " +
    "private-growth-mb={4:N1} finalizer-ms={5}",
    switchCount,
    baselineHandles,
    handlesAfterDispose,
    process.HandleCount,
    (privateBytesAfterDispose - baselinePrivateBytes) / 1024d / 1024d,
    finalizerTimer.ElapsedMilliseconds);

var undisposedHandles = handlesAfterDispose - process.HandleCount;
if (undisposedHandles > 8)
    throw new InvalidOperationException(
        $"Dispose left {undisposedHandles} handles for GC/finalizers; repeated media switches defer native cleanup until exit.");
