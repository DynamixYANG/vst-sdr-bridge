using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vst.Core;

public enum EngineState { INITIALIZING, RUNNING, TUNING, RECOVERING, ERROR, STOPPED }
public sealed record RxConfiguration(double CenterHz = 1e9, double RateHz = 120e6, double ReferenceDbm = 0, string PreampMode = "auto")
{
    // Matches ContinuousDma / NI Streaming for VST: IQ rate 1..120 MS/s.
    // NI-RFSA coerces to a device-legal value; Hub publishes the read-back rate.
    public const double MinRateHz = 1e6;
    public const double MaxRateHz = 120e6;
    public void Validate()
    {
        if (!double.IsFinite(CenterHz) || CenterHz < 65e6 || CenterHz > 6e9)
            throw new ArgumentException("Center frequency must be between 65 MHz and 6 GHz.");
        if (!double.IsFinite(RateHz) || RateHz < MinRateHz || RateHz > MaxRateHz)
            throw new ArgumentException("Sample rate must be between 1 MS/s and 120 MS/s (NI Streaming for VST / ContinuousDma limits).");
        if (!double.IsFinite(ReferenceDbm) || ReferenceDbm < -50 || ReferenceDbm > 30)
            throw new ArgumentException("Reference level must be between -50 and +30 dBm.");
        if (PreampMode != "auto") throw new ArgumentException("5644R supports Auto preamp only. Use reference level to configure the front end.");
    }
}

public sealed record Frontend(RxConfiguration Configuration, int PreampActual, bool PreampPresent, double BandwidthHz)
{
    public string ActualMode => PreampActual switch {2500 => "off", 2502 => "on", 2503 => "auto", _ => "unknown"};
}

// Driver sessions and stream direction are separate extension seams. A future
// TX implementation must negotiate the shared RFSA/RFSG FPGA session here;
// it must not create a competing device session behind the RX engine.
public sealed record DeviceCapabilities(bool Rx, bool Tx, bool FullDuplex, string[] PreampModes);
public interface IRxHardware : IDisposable
{
    Frontend Configure(RxConfiguration configuration);
    nint Read(); // Pointer is owned by hardware until the next Read/Dispose.
    int BlockSamples { get; }
    ulong FifoRemaining { get; }
    ulong FifoCapacity { get; }
    bool Overflow { get; }
}
public interface ITxHardware : IDisposable
{
    void Configure(double centerHz, double sampleRate, double outputLevelDbm);
    void Write(nint interleavedIq, int complexSamples);
    void Stop();
}

public sealed record HubOptions
{
    public int SchemaVersion { get; init; } = 1;
    public string Resource { get; init; } = "RIO0";
    // VST Hub and the Soapy consumer use this single canonical endpoint.
    public string RingName { get; init; } = @"Local\vst_live_v2";
    public int ControlPort { get; init; } = 19788;
    public int RingMiB { get; init; } = 256;
    public int FifoMiB { get; init; } = 256;
    public int BlockSamples { get; init; } = 1048576;
    public int LogFileMiB { get; init; } = 10;
    public int LogFiles { get; init; } = 10;
    public int LogRetentionDays { get; init; } = 14;
    public string GnuRadioPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"radioconda\Scripts\gnuradio-companion.exe");
    public string GqrxPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"radioconda\Library\bin\gqrx.exe");
    public string GnuRadioArguments { get; init; } = "";
    public string GqrxArguments { get; init; } = "";
    public string BitfilePath { get; init; } = @"C:\Users\Public\Documents\National Instruments\FPGA Extensions Bitfiles\NI PXIe-5644R\NI Streaming for VST.lvbitx";
    public RxConfiguration Rx { get; init; } = new();
    public TxConfiguration Tx { get; init; } = new();
    public void Validate()
    {
        Rx.Validate();
        if (SchemaVersion != 1 || ControlPort is < 1024 or > 65535 ||
            RingMiB is < 64 or > 1024 || RingMiB % 64 != 0 ||
            FifoMiB is < 64 or > 1024 || BlockSamples != 1048576 ||
            LogFileMiB is < 1 or > 100 || LogFiles is < 2 or > 100 || LogRetentionDays is < 1 or > 365)
            throw new ArgumentException("Unsupported configuration. Shared memory must be 64–1024 MiB in steps of 64; DMA blocks require 1,048,576 complex samples.");
        if (string.IsNullOrWhiteSpace(Resource) || Resource.Any(char.IsWhiteSpace)) throw new ArgumentException("Invalid NI resource name.");
    }

    /// <summary>
    /// Raw IQ (int16 I + int16 Q = 4 bytes/complex) DMA/SHM bit rate in gigabits per second.
    /// Formula: Gbps = RateHz × 4 bytes × 8 bits / 1e9 = RateHz × 32e-9.
    /// CF32 delivery to GQRX is twice that (8 bytes/complex → RateHz × 64e-9).
    /// </summary>
    public static double RawIqGbps(double rateHz) => rateHz * 32e-9;
    public static double Cf32Gbps(double rateHz) => rateHz * 64e-9;
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }, WriteIndented = false
    };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static string Number(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}

public sealed class NiException(string operation, int code, string detail) : Exception($"{operation}: NI {code} (0x{unchecked((uint)code):X8}) {detail}")
{
    public int Code { get; } = code;
}

public sealed record HubSnapshot
{
    public int SchemaVersion { get; init; } = 2;
    public TxSnapshot Tx { get; init; } = new();
    public string SessionId { get; init; } = "";
    public int Pid { get; init; } = Environment.ProcessId;
    public double UpdatedUnix { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000d;
    public double ElapsedS { get; init; }
    public EngineState Status { get; init; } = EngineState.INITIALIZING;
    public bool Done { get; init; }
    public string Transport { get; init; } = "native-fpga-dma-memory";
    public string? Error { get; init; }
    public string LastControlError { get; init; } = "";
    public int LastNiError { get; init; }
    public ulong RequestId { get; init; }
    public ulong ControlErrorCount { get; init; }
    public ulong ConfigCount { get; init; }
    public ulong OverflowCount { get; init; }
    public ulong Recoveries { get; init; }
    public double LastTuneS { get; init; }
    public double MaxPublishGapS { get; init; }
    public double AppliedCenterHz { get; init; }
    public double AppliedRateHz { get; init; } = 120e6;
    public double AppliedRefDbm { get; init; }
    public string PreampMode { get; init; } = "auto";
    public string PreampActual { get; init; } = "unknown";
    public bool PreampPresent { get; init; }
    public double EffectiveBandwidthHz { get; init; }
    public double DmaMsps { get; init; }
    public double ShmMsps { get; init; }
    public double DeliveredMsps { get; init; }
    public double CpuCores { get; init; }
    public ulong FifoRemaining { get; init; }
    public ulong FifoCapacity { get; init; }
    public double FifoPercent => FifoCapacity == 0 ? 0 : 100d * FifoRemaining/FifoCapacity;
    public string ClientState { get; init; } = "Waiting for client";
    public long LogDropped { get; init; }
    public string LogError { get; init; } = "";
    public RingSnapshot Ring { get; init; } = new();
    public DeviceCapabilities Capabilities { get; init; } = new(true, false, false, ["auto"]);
}
