using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Vst.Core;

static string Cmd(string line)
{
    using var c = new TcpClient("127.0.0.1", 19788);
    using var s = c.GetStream();
    s.Write(Encoding.UTF8.GetBytes(line + "\n"));
    using var r = new StreamReader(s, Encoding.UTF8);
    return r.ReadLine() ?? "";
}

double seconds = args.Length > 0 && !args[0].StartsWith("-") ? double.Parse(args[0]) : 20;
bool rf = args.Contains("--rf-enabled");
Console.WriteLine(JsonSerializer.Serialize(new { seconds, rf_enabled = rf, note = "native CS16 live_ring feeder (wavetable)" }));

Console.WriteLine(Cmd("TXSTOP"));
var cfg = new TxConfiguration
{
    Source = "live_ring", CenterHz = 2.5e9, RateHz = 120e6, PeakDbm = -30, RfEnabled = rf,
    RingName = SharedTxRing.DefaultName, RingMiB = 64, QueueMiB = 64, FifoMiB = 256, PrefillBlocks = 16
};
cfg.Validate();
var start = Cmd("TXSTART " + JsonDefaults.Serialize(cfg));
Console.WriteLine(start);
if (!start.StartsWith("OK")) return 2;

SharedTxRing? ring = null;
var openDeadline = Stopwatch.StartNew();
while (openDeadline.Elapsed < TimeSpan.FromSeconds(15))
{
    try { ring = new SharedTxRing(SharedTxRing.DefaultName, 64, create: false); break; }
    catch { Thread.Sleep(20); }
}
if (ring == null) { Console.Error.WriteLine("ring open failed"); return 3; }

using (ring)
{
    ring.ClaimProducer();
    // 1 MHz tone @ 120 MS/s => period 120 samples. Tile into 1 Mi-sample DMA-sized block.
    const int period = 120;
    const int block = 120 * 8192; // 983040, multiple of 1 MHz period @ 120 MS/s
    var tile = new short[period * 2];
    for (int n = 0; n < period; n++)
    {
        double ph = 2 * Math.PI * n / period;
        tile[2 * n] = (short)(Math.Cos(ph) * 0.8 * 32767);
        tile[2 * n + 1] = (short)(Math.Sin(ph) * 0.8 * 32767);
    }
    var buf = new short[block * 2];
    for (int off = 0; off < block; off += period)
        Buffer.BlockCopy(tile, 0, buf, off * 4, period * 4);

    var t0 = Stopwatch.StartNew();
    ulong accepted = 0;
    var last = Stopwatch.StartNew();
    string lastStatus = "";
    while (t0.Elapsed.TotalSeconds < seconds)
    {
        int offset = 0;
        while (offset < block)
        {
            unsafe
            {
                fixed (short* p = &buf[offset * 2])
                {
                    int wrote;
                    var spin = Stopwatch.StartNew();
                    while ((wrote = ring.TryWrite((nint)p, block - offset)) == 0)
                    {
                        if (spin.ElapsedMilliseconds > 5000) throw new TimeoutException("ring full >5s");
                        Thread.Sleep(0);
                    }
                    offset += wrote; accepted += (ulong)wrote;
                }
            }
        }
        if (last.ElapsedMilliseconds >= 1000)
        {
            var st = JsonDocument.Parse(Cmd("STATUS")).RootElement.GetProperty("tx");
            lastStatus = st.GetProperty("status").GetString() ?? "";
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                elapsed_s = Math.Round(t0.Elapsed.TotalSeconds, 2),
                feed_msps = accepted / t0.Elapsed.TotalSeconds / 1e6,
                hub = lastStatus,
                processed_msps = st.GetProperty("processed_msps").GetDouble(),
                underflows = st.GetProperty("underflows").GetUInt32(),
                rf_enabled = st.GetProperty("rf_enabled").GetBoolean(),
                queue = st.GetProperty("queue_samples").GetUInt64()
            }));
            last.Restart();
            if (lastStatus == "FAULT")
            {
                Console.Error.WriteLine(st.ToString());
                Cmd("TXSTOP");
                return 1;
            }
        }
    }
    var final = JsonDocument.Parse(Cmd("STATUS")).RootElement.GetProperty("tx");
    Console.WriteLine("FINAL " + final);
}
Cmd("TXSTOP");
var after = JsonDocument.Parse(Cmd("STATUS")).RootElement.GetProperty("tx");
Console.WriteLine(JsonSerializer.Serialize(new { after_status = after.GetProperty("status").GetString(), rf = after.GetProperty("rf_enabled").GetBoolean() }));
return 0;
