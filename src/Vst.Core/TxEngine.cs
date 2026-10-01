using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Vst.Core;

internal sealed unsafe class TxEngine : IDisposable
{
    private readonly NiDeviceSession device;
    private readonly RotatingLog log;
    private CancellationTokenSource? stop;
    private Thread? worker;
    private double requestedRateHz;
    private TxSnapshot snapshot = new(){Status="STOPPED"};
    public TxSnapshot Snapshot => Volatile.Read(ref snapshot);
    public bool IsAlive => worker?.IsAlive == true;
    public double RequestedRateHz => Volatile.Read(ref requestedRateHz);
    public TxEngine(NiDeviceSession device, RotatingLog log) { this.device = device; this.log = log; }
    public void Start(TxConfiguration config)
    {
        config.Validate();
        if (IsAlive) throw new InvalidOperationException("TX already running. Stop TX before reconfiguration.");
        Volatile.Write(ref requestedRateHz,config.RateHz);
        stop?.Dispose(); stop = new();
        Volatile.Write(ref snapshot, new TxSnapshot
        {
            Status = "CONFIGURING",
            Waveform = config.IsLiveRing ? "(live_ring)" : Path.GetFileName(config.WaveformPath),
            SourceMode = config.IsLiveRing
                ? "Live SHM ring -> bounded queue -> DMA"
                : "Host memory replay -> bounded queue -> DMA"
        });
        worker = new Thread(() => Run(config, stop.Token))
        {
            IsBackground = true,
            Name = "VST TX DMA",
            Priority = ThreadPriority.AboveNormal
        };
        worker.Start();
    }

    private void Run(TxConfiguration c, CancellationToken token)
    {
        NiTxHardware? hardware = null; TxSampleQueue? queue = null; Thread? producer = null;
        SharedTxRing? live = null;
        using var sourceStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Exception? producerError = null;
        var timer = Stopwatch.StartNew(); string error = ""; string state = "CONFIGURING"; bool clientIdle=false;
        bool ClientNotSending() => live != null && (!live.TryReadProducerHeartbeat(out _, out var age, out _) || age > 500);
        ulong submitted = 0, processed = 0, previousSubmitted = 0, previousProduced = 0, previousProcessed = 0;
        uint lastRaw = 0; double lastMetric = 0, maxWrite = 0; string hash = "";
        void Update(bool measure = false)
        {
            double now = timer.Elapsed.TotalSeconds, interval = now - lastMetric;
            var old = Snapshot; uint raw = hardware?.ReadU32("samples processed") ?? 0;
            if (state is "STREAMING" or "STOPPING") { processed += unchecked(raw - lastRaw); lastRaw = raw; }
            var s = new TxSnapshot
            {
                Status = state, Error = error,
                Waveform = c.IsLiveRing ? "(live_ring)" : Path.GetFileName(c.WaveformPath),
                WaveformSha256 = hash,
                SourceMode = c.IsLiveRing ? "Live SHM ring -> bounded queue -> DMA" : "Host memory replay -> bounded queue -> DMA",
                AppliedRateHz = hardware?.Rate ?? 0, AppliedCenterHz = hardware?.Center ?? 0, AppliedPeakDbm = hardware?.Peak ?? 0,
                RfEnabled = hardware?.RfEnabled ?? false, ElapsedS = now,
                ProducedSamples = queue?.Produced ?? old.ProducedSamples,
                SubmittedSamples = submitted, ProcessedSamples = processed,
                SourceMsps = measure && interval > 0 ? ((queue?.Produced ?? 0) - previousProduced) / interval / 1e6 : old.SourceMsps,
                DmaMsps = measure && interval > 0 ? (submitted - previousSubmitted) / interval / 1e6 : old.DmaMsps,
                ProcessedMsps = measure && interval > 0 ? (processed - previousProcessed) / interval / 1e6 : old.ProcessedMsps,
                QueueSamples = queue?.Occupancy ?? 0, QueueCapacity = queue?.Capacity ?? 0,
                HostFifoSamples = hardware == null ? 0 : hardware.Capacity - hardware.Free,
                HostFifoCapacity = hardware?.Capacity ?? 0,
                FpgaFifoSamples = hardware?.ReadU32("dma fullness") ?? 0,
                FpgaMinSamples = hardware?.ReadU32("dma min elements") ?? 0,
                Underflows = hardware?.ReadU32("underflows") ?? 0,
                Primed = hardware?.ReadBool("primed") ?? false, FpgaState = hardware?.ReadState() ?? 0,
                MaxWriteMs = maxWrite, SourceWaits = queue?.Waits ?? 0
            };
            if (measure) { lastMetric = now; previousProduced = s.ProducedSamples; previousSubmitted = submitted; previousProcessed = processed; }
            Volatile.Write(ref snapshot, s);
            try { live?.PublishApplied(s.AppliedCenterHz, s.AppliedRateHz, s.AppliedPeakDbm, s.RfEnabled, state); } catch { }
        }

        try
        {
            log.Event("INFO","tx.initializing",new {stage="Allocating bounded source queue",c.QueueMiB,c.Source});
            queue = new TxSampleQueue(c.QueueMiB);
            if (c.IsLiveRing)
            {
                // Do not expose an attachable ring while RFSG/DMA configuration
                // can still block longer than a client's writeStream timeout.
                // Configure first; GNU Radio can then start filling prefetch.
                log.Event("INFO","tx.initializing",new {stage="Configuring TX DMA FIFO and RFSG",c.FifoMiB,c.CenterHz,c.RateHz,c.PeakDbm});
                hardware = new NiTxHardware(device, c.FifoMiB);
                hardware.Configure(c.CenterHz, c.RateHz, c.PeakDbm);
                log.Event("INFO","tx.initializing",new {stage="Opening live IQ shared memory and waiting for client",c.RingName,c.RingMiB});
                live = new SharedTxRing(c.RingName, c.RingMiB, create: true);
                state = "PREFILLING"; Update();
                // Wait briefly for a live producer to appear and push prefill.
                var waitDeadline = Stopwatch.StartNew();
                // Heartbeat only: GNU Radio delivers IQ from work() after start() returns.
                while (!token.IsCancellationRequested)
                {
                    if (live.TryReadProducerHeartbeat(out _, out var age, out _) && age < 2000) break;
                    if (waitDeadline.Elapsed > TimeSpan.FromSeconds(15))
                        throw new TimeoutException("TX live_ring: no producer heartbeat within 15 s. Start vst_tx_sink before or right after TXSTART.");
                    token.WaitHandle.WaitOne(20);
                }
                producer = new Thread(() =>
                {
                    try
                    {
                        using var idleWait = new HighResolutionWait();
                        var scratch = (nint)NativeMemory.AlignedAlloc((nuint)TxSampleQueue.BlockSamples * 4, 64);
                        try
                        {
                            while (!sourceStop.IsCancellationRequested)
                            {
                                nint destination = queue.AcquireWrite(sourceStop.Token);
                                int got = 0;
                                var blockDeadline = Stopwatch.StartNew();
                                // Prefill may wait for GNU Radio work() to start; after STREAMING keep a tight starve bound.
                                int starveMs = Volatile.Read(ref snapshot).Status == "STREAMING" ? 500 : 30000;
                                while (got < TxSampleQueue.BlockSamples)
                                {
                                    sourceStop.Token.ThrowIfCancellationRequested();
                                    int n = live.TryCopy(scratch + got * 4, TxSampleQueue.BlockSamples - got);
                                    if (n == 0)
                                    {
                                        if (blockDeadline.ElapsedMilliseconds > starveMs)
                                            throw new TimeoutException($"TX live_ring starved for {starveMs} ms while filling a DMA block.");
                                        idleWait.Wait();
                                        continue;
                                    }
                                    got += n;
                                    blockDeadline.Restart();
                                }
                                Buffer.MemoryCopy((void*)scratch, (void*)destination, TxSampleQueue.BlockSamples * 4L, TxSampleQueue.BlockSamples * 4L);
                                queue.Publish();
                            }
                        }
                        finally { NativeMemory.AlignedFree((void*)scratch); }
                    }
                    catch (OperationCanceledException) when (sourceStop.IsCancellationRequested) { }
                    catch (Exception ex) { Volatile.Write(ref producerError, ex); }
                })
                { IsBackground = true, Name = "VST TX live ring source", Priority = ThreadPriority.AboveNormal };
            }
            else
            {
                log.Event("INFO","tx.initializing",new {stage="Validating and loading waveform",c.WaveformPath,c.RateHz});
                byte[] waveform = WaveformFile.Load(c.WaveformPath, c.RateHz, out hash);
                producer = new Thread(() =>
                {
                    try
                    {
                        int offset = 0;
                        fixed (byte* source = waveform)
                            while (!sourceStop.IsCancellationRequested)
                            {
                                byte* destination = (byte*)queue.AcquireWrite(sourceStop.Token); int copied = 0;
                                while (copied < TxSampleQueue.BlockSamples * 4)
                                {
                                    int n = Math.Min(waveform.Length - offset, TxSampleQueue.BlockSamples * 4 - copied);
                                    Buffer.MemoryCopy(source + offset, destination + copied, n, n);
                                    copied += n; offset = (offset + n) % waveform.Length;
                                }
                                queue.Publish();
                            }
                    }
                    catch (OperationCanceledException) when (sourceStop.IsCancellationRequested) { }
                    catch (Exception ex) { Volatile.Write(ref producerError, ex); }
                })
                { IsBackground = true, Name = "VST TX memory source", Priority = ThreadPriority.AboveNormal };
            }

            producer.Start();
            if(hardware==null)
            {
                log.Event("INFO","tx.initializing",new {stage="Configuring TX DMA FIFO and RFSG",c.FifoMiB,c.CenterHz,c.RateHz,c.PeakDbm});
                hardware = new NiTxHardware(device, c.FifoMiB);
                hardware.Configure(c.CenterHz, c.RateHz, c.PeakDbm);
            }
            state = "PREFILLING"; Update();
            void Transfer(int timeoutMs=500)
            {
                if (Volatile.Read(ref producerError) is { } failure) throw failure;
                nint data = queue.AcquireRead(token, timeoutMs); long start = Stopwatch.GetTimestamp();
                hardware.Write(data, TxSampleQueue.BlockSamples);
                maxWrite = Math.Max(maxWrite, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                queue.Release(); submitted += TxSampleQueue.BlockSamples;
            }
            // GNU Radio activates sinks before scheduling their first work() call.
            // Startup is not an on-air starvation: allow one bounded 15 s prefill window.
            var prefillDeadline = Stopwatch.StartNew();
            int prefillBlocks = c.IsLiveRing ? Math.Max(c.PrefillBlocks, Math.Min(48, c.FifoMiB / 4 - 1)) : c.PrefillBlocks;
            for (int i = 0; i < prefillBlocks; i++)
            {
                token.ThrowIfCancellationRequested();
                int remaining = 15000 - (int)prefillDeadline.ElapsedMilliseconds;
                if (remaining <= 0) throw new TimeoutException("TX startup prefill did not complete within 15 s.");
                Transfer(remaining);
            }
            token.ThrowIfCancellationRequested();
            log.Event("INFO","tx.prefill_complete",new {blocks=prefillBlocks,submitted_samples=submitted});
            hardware.Initiate(c.RfEnabled);
            lastRaw = 0; state = "STREAMING"; Update(true);
            log.Event("INFO", "tx.started", new { configuration = c, sha256 = hash, readback = Snapshot });
            while (!token.IsCancellationRequested)
            {
                if(c.IsLiveRing && ClientNotSending())
                {
                    clientIdle=true;
                    log.Event("INFO","tx.client_idle","Client is not sending IQ; RF output disabled");
                    break;
                }
                Transfer();
                if (timer.Elapsed.TotalSeconds - lastMetric >= .5)
                {
                    hardware.RefreshFree(); Update(true);
                    if (Snapshot.Underflows != 0 || hardware.ReadBool("underflow"))
                        throw new InvalidOperationException("FPGA TX underflow: output stopped; explicit restart required.");
                    if (!Snapshot.Primed || Snapshot.FpgaState != 1)
                        throw new InvalidOperationException("TX FPGA left the active/primed state.");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (c.IsLiveRing && ClientNotSending() &&
            (ex is TimeoutException || ex.Message.StartsWith("FPGA TX underflow") || ex.Message.StartsWith("TX FPGA left")))
        {
            clientIdle=true;
            log.Event("INFO","tx.client_idle",new {message="Client is not sending IQ; RF output disabled",cause=ex.Message});
        }
        catch (Exception ex) { error = ex.Message; log.Event("ERROR", "tx.failed", ex.ToString()); }
        finally
        {
            sourceStop.Cancel(); producer?.Join();
            if (hardware != null)
            {
                try { state = "STOPPING"; hardware.RefreshFree(); Update(true); }
                catch (Exception ex) { if (error.Length == 0) error = ex.Message; }
                try { hardware.Dispose(); }
                catch (Exception ex) { error += " TX shutdown: " + ex.Message; log.Event("ERROR", "tx.shutdown_failed", ex.ToString()); }
            }
            queue?.Dispose();
            try { live?.Dispose(); } catch { }
            Volatile.Write(ref snapshot, Snapshot with
            {
                Status = error.Length != 0 ? "FAULT" : clientIdle ? "WAITING_CLIENT" : "STOPPED",
                Error = error,
                RfEnabled = false,
                SourceMsps = 0, DmaMsps = 0, ProcessedMsps = 0,
                QueueSamples = 0, HostFifoSamples = 0
            });
            log.Event(error.Length == 0 ? "INFO" : "ERROR", "tx.stopped", Snapshot);
        }
    }

    public void Stop(bool clientStopped=false)
    {
        stop?.Cancel(); worker?.Join();
        if(clientStopped && Snapshot.Status != "FAULT")
        {
            Volatile.Write(ref snapshot,Snapshot with {Status="WAITING_CLIENT",Error="",RfEnabled=false});
            log.Event("INFO","tx.client_idle","Client stopped sending IQ; RF output disabled");
        }
    }
    public void Dispose() { Stop(); stop?.Dispose(); }
}
