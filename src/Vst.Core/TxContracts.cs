namespace Vst.Core;

public sealed record TxConfiguration
{
    /// <summary>"file" = CS16 waveform replay (default). "live_ring" = SHM producer (vst_tx_sink / Soapy TX).</summary>
    public string Source { get; init; } = "file";
    public double CenterHz { get; init; } = 2.45e9;
    public double RateHz { get; init; } = 120e6;
    public double PeakDbm { get; init; } = -30;
    public bool RfEnabled { get; init; }
    public string WaveformPath { get; init; } = "";
    public string RingName { get; init; } = SharedTxRing.DefaultName;
    public int QueueMiB { get; init; } = 128;
    public int FifoMiB { get; init; } = 256;
    public int PrefillBlocks { get; init; } = 16;
    public int RingMiB { get; init; } = 128;
    public bool IsLiveRing => string.Equals(Source, "live_ring", StringComparison.OrdinalIgnoreCase);
    public void Validate()
    {
        if (!double.IsFinite(CenterHz) || CenterHz < 65e6 || CenterHz > 6e9) throw new ArgumentException("TX frequency: 65 MHz to 6 GHz.");
        if (RateHz != 120e6) throw new ArgumentException("This TX release supports 120 MS/s only.");
        if (!double.IsFinite(PeakDbm) || PeakDbm < -50 || PeakDbm > 0) throw new ArgumentException("TX peak level: -50 to 0 dBm.");
        if (QueueMiB is < 16 or > 256 || QueueMiB % 4 != 0 || FifoMiB is < 64 or > 512 || PrefillBlocks is < 4 or > 32 || PrefillBlocks * 4 >= FifoMiB)
            throw new ArgumentException("Invalid TX queue, DMA FIFO, or prefill size.");
        if (Source != "file" && !IsLiveRing) throw new ArgumentException("TX source must be file or live_ring.");
        if (IsLiveRing)
        {
            if (RingMiB is < 16 or > 256 || RingMiB % 4 != 0) throw new ArgumentException("TX live ring must be 16�?56 MiB in steps of 4.");
            if (string.IsNullOrWhiteSpace(RingName)) throw new ArgumentException("live_ring requires ring_name.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(WaveformPath)) throw new ArgumentException("Select TDMS I/Q or CS16 with matching JSON metadata.");
        }
    }
}

public sealed record TxSnapshot
{
    public string Status { get; init; } = "DISABLED";
    public string Error { get; init; } = "";
    public string Waveform { get; init; } = "";
    public string WaveformSha256 { get; init; } = "";
    public string SourceMode { get; init; } = "Host memory replay -> bounded queue -> DMA";
    public double AppliedRateHz { get; init; }
    public double AppliedCenterHz { get; init; }
    public double AppliedPeakDbm { get; init; }
    public bool RfEnabled { get; init; }
    public double ElapsedS { get; init; }
    public ulong ProducedSamples { get; init; }
    public ulong SubmittedSamples { get; init; }
    public ulong ProcessedSamples { get; init; }
    public double SourceMsps { get; init; }
    public double DmaMsps { get; init; }
    public double ProcessedMsps { get; init; }
    public ulong QueueSamples { get; init; }
    public ulong QueueCapacity { get; init; }
    public ulong HostFifoSamples { get; init; }
    public ulong HostFifoCapacity { get; init; }
    public uint FpgaFifoSamples { get; init; }
    public uint FpgaMinSamples { get; init; }
    public uint Underflows { get; init; }
    public bool Primed { get; init; }
    public ushort FpgaState { get; init; }
    public double MaxWriteMs { get; init; }
    public ulong SourceWaits { get; init; }
    public double QueuePercent => QueueCapacity == 0 ? 0 : 100d * QueueSamples / QueueCapacity;
    public double HostFifoPercent => HostFifoCapacity == 0 ? 0 : 100d * HostFifoSamples / HostFifoCapacity;
}
