using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Vst.Core;

public sealed class HubEngine : IDisposable
{
    private sealed record Request(string Line,TaskCompletionSource<string> Completion,long Deadline);
    private readonly HubOptions options;
    private readonly Func<HubOptions,IRxHardware>? factory;
    private TxEngine? tx;
    private TxConfiguration liveDefaults;
    private readonly ConcurrentQueue<Request> requests=new();
    private readonly AutoResetEvent wake=new(false);
    private readonly CancellationTokenSource stop=new();
    private Thread? worker;
    private HubSnapshot snapshot=new();
    public HubSnapshot Snapshot => Volatile.Read(ref snapshot) with {Tx=tx?.Snapshot??new()};
    public bool IsAlive => worker?.IsAlive==true;
    private volatile bool shutdownRequested;
    public bool ShutdownRequested => shutdownRequested;
    public RotatingLog Log { get; }
    private readonly string session=Guid.NewGuid().ToString("N")[..12];
    public HubEngine(HubOptions options,string logDirectory,Func<HubOptions,IRxHardware>? factory=null)
    {
        options.Validate(); this.options=options; this.factory=factory; liveDefaults=options.Tx;
        Log=new RotatingLog(logDirectory,session,options.LogFileMiB*1048576L,options.LogFiles,options.LogRetentionDays);
    }
    public void Start()
    {
        if(IsAlive) throw new InvalidOperationException("Engine already started");
        worker=new Thread(Run){Name="VST RX device owner",IsBackground=true,Priority=ThreadPriority.AboveNormal};
        Log.Event("INFO","bridge.worker_start","Starting shared-device owner and RX worker");
        worker.Start();
    }
    public Task<string> Command(string line)
    {
        if(stop.IsCancellationRequested) return Task.FromResult("ERR application is shutting down");
        if(worker!=null && !worker.IsAlive) return Task.FromResult("ERR engine unavailable; restart RX");
        if(requests.Count>=16) return Task.FromResult("ERR control queue full");
        var completion=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        requests.Enqueue(new Request(line,completion,Environment.TickCount64+15000)); wake.Set();
        return completion.Task;
    }
    private void Run()
    {
        using var owner=new Mutex(false,options.RingName+".producer");
        bool owns=false; SharedIqRing? ring=null; IRxHardware? hardware=null; ControlServer? server=null;
        NiDeviceSession? device=null;
        var timer=Stopwatch.StartNew(); var state=EngineState.INITIALIZING; var config=options.Rx;
        Frontend? front=null; RxConfiguration? pendingRx=null; string? error=null; string controlError=""; int niError=0;
        ulong requestId=0,configCount=0,controlErrors=0,overflow=0,recoveries=0;
        double lastTune=0,lastPublish=0,maxGap=0,lastMetric=0,previousMetric=0;
        ulong previousWrite=0,previousDelivered=0;
        double previousCpu=0; using var process=Process.GetCurrentProcess();
        void Telemetry(bool metric=false)
        {
            var sh=ring?.Snapshot()??new RingSnapshot();
            var now=timer.Elapsed.TotalSeconds; double interval=now-previousMetric;
            double dma=Snapshot.DmaMsps, delivered=Snapshot.DeliveredMsps,cpu=Snapshot.CpuCores;
            if(metric && interval>0)
            {
                dma=(sh.WriteIdx-previousWrite)/interval/1e6;
                delivered=(sh.DeliveredSamples-previousDelivered)/interval/1e6;
                var cpuSeconds=process.TotalProcessorTime.TotalSeconds; cpu=(cpuSeconds-previousCpu)/interval;
                previousWrite=sh.WriteIdx; previousDelivered=sh.DeliveredSamples; previousMetric=now; previousCpu=cpuSeconds;
            }
            var s=new HubSnapshot{SessionId=session,ElapsedS=now,Status=state,Done=state is EngineState.STOPPED or EngineState.ERROR,
                Error=error,LastControlError=controlError,LastNiError=niError,RequestId=requestId,ConfigCount=configCount,
                ControlErrorCount=controlErrors,OverflowCount=overflow,Recoveries=recoveries,LastTuneS=lastTune,MaxPublishGapS=maxGap,
                AppliedCenterHz=config.CenterHz,AppliedRateHz=config.RateHz,AppliedRefDbm=config.ReferenceDbm,
                PreampActual=front?.ActualMode??"unknown",PreampPresent=front?.PreampPresent??false,EffectiveBandwidthHz=front?.BandwidthHz??0,
                FifoRemaining=hardware?.FifoRemaining??0,FifoCapacity=hardware?.FifoCapacity??0,
                DmaMsps=dma,ShmMsps=dma,DeliveredMsps=delivered,CpuCores=cpu,Ring=sh,LogDropped=Log.Dropped,LogError=Log.Error,
                ClientState=sh.ConsumerActive?(sh.ConsumerHeartbeatAgeMs<2000?"Receiving":"Client stalled"):"Waiting for client",
                Tx=tx?.Snapshot??new(),Capabilities=new(true,device!=null,device!=null,["auto"])};
            ring?.Telemetry(s); Volatile.Write(ref snapshot,s);
            if(metric) Log.Metric(s);
        }
        void Publish()
        {
            ring!.Publish(hardware!.Read(),hardware.BlockSamples);
            var now=timer.Elapsed.TotalSeconds;
            if(lastPublish>0) maxGap=Math.Max(maxGap,now-lastPublish);
            lastPublish=now;
        }
        void Configure(RxConfiguration requested)
        {
            // Rate/bitfile changes are rejected by the caller while TX is alive.
            // Center/reference may retune with TX running (independent TX/RX settings).
            requested.Validate(); state=EngineState.TUNING; ring!.Pause(); Telemetry();
            var t=Stopwatch.StartNew();
            Log.Event("INFO","rx.configuring",requested);
            var readback=hardware!.Configure(requested);
            hardware.Read(); hardware.Read();
            if(hardware.Overflow) throw new NiException("Configure overflow",-1,"FPGA overflow while settling");
            ring.Configure(readback); lastPublish=0; Publish();
            config=readback.Configuration; front=readback; lastTune=t.Elapsed.TotalSeconds; configCount++;
            state=EngineState.RUNNING; error=null; Telemetry();
        }
        void Open()
        {
            state=EngineState.INITIALIZING; error=null; Telemetry();
            if(factory!=null) hardware=factory(options);
            else
            {
                if(device==null) {device=new NiDeviceSession(options,message=>Log.Event("INFO","device.initializing",message));tx=new TxEngine(device,Log);}
                Log.Event("INFO","rx.initializing",new {stage="Allocating RX DMA FIFO",options.FifoMiB,options.BlockSamples});
                hardware=new NiRxHardware(options,device);
            }
            Configure(config);
            Log.Event("INFO","rx.started",new{config,hardware.FifoCapacity});
        }
        try
        {
            try {owns=owner.WaitOne(0);} catch(AbandonedMutexException) {owns=true;}
            if(!owns) throw new InvalidOperationException("Another bridge owns this device. Stop it, then retry or restart Hub.");
            Log.Event("INFO","control.initializing",new {address="127.0.0.1",options.ControlPort});
            server=new ControlServer(this,options.ControlPort); // reserve port before touching hardware
            Log.Event("INFO","rx.shared_memory",new {options.RingName,options.RingMiB});
            ring=new SharedIqRing(options.RingName,options.RingMiB);
            try {Open();} catch(Exception ex) {state=EngineState.ERROR; error=ex.Message; hardware?.Dispose(); hardware=null; Log.Event("ERROR","rx.start_failed",error); Telemetry();}
            while(!stop.IsCancellationRequested)
            {
                if(requests.TryDequeue(out var request))
                {
                    var started=Stopwatch.StartNew(); string reply;
                    requestId++; niError=0;
                    try
                    {
                        if(Environment.TickCount64>request.Deadline) throw new TimeoutException("Request expired before execution; not applied");
                        if(request.Line=="SHUTDOWN") {shutdownRequested=true; reply="OK application shutting down";}
                        else if(request.Line=="TXIDLE")
                        {
                            tx?.Stop(clientStopped:true);reply="OK TX client idle";
                        }
                        else if(request.Line=="TXSTOP")
                        {
                            tx?.Stop();reply="OK TX stopped";
                        }
                        else if(request.Line.StartsWith("TXDEFAULTS "))
                        {
                            var requested=JsonSerializer.Deserialize<TxConfiguration>(request.Line[11..],JsonDefaults.Options)??throw new ArgumentException("TX defaults required");
                            (requested with {Source="live_ring"}).Validate();
                            liveDefaults=requested;reply="OK TX buffer defaults saved for next client start";
                        }
                        else if(request.Line.StartsWith("TXSTART "))
                        {
                            if(tx==null) throw new InvalidOperationException("Shared device session is not ready. Hub must finish opening RFSA/RFSG/FPGA before TXSTART (independent of RX streaming).");
                            var requested=JsonSerializer.Deserialize<TxConfiguration>(request.Line[8..],JsonDefaults.Options)??throw new ArgumentException("TX configuration required");
                            if(requested.IsLiveRing)
                            {
                                using var json=JsonDocument.Parse(request.Line[8..]);
                                requested=requested with {
                                    QueueMiB=json.RootElement.TryGetProperty("queue_mi_b",out _)?requested.QueueMiB:liveDefaults.QueueMiB,
                                    FifoMiB=json.RootElement.TryGetProperty("fifo_mi_b",out _)?requested.FifoMiB:liveDefaults.FifoMiB,
                                    PrefillBlocks=json.RootElement.TryGetProperty("prefill_blocks",out _)?requested.PrefillBlocks:liveDefaults.PrefillBlocks
                                };
                            }
                            tx.Start(requested);reply="OK TX startup requested; inspect tx.status for completion";
                        }
                        else if(request.Line=="STOP")
                        {
                            ring.Pause(); hardware?.Dispose(); hardware=null; state=EngineState.STOPPED; error=null; lastPublish=0;
                            reply="OK RX stopped";
                        }
                        else if(request.Line=="START")
                        {
                            // RX start/stop is independent of TX streaming on the shared session.
                            if(hardware==null) {if(pendingRx!=null)config=pendingRx;Open();pendingRx=null;} reply="OK RX running";
                        }
                        else
                        {
                            var requested=ControlServer.ParseConfiguration(request.Line,pendingRx??config); requested.Validate();
                            if(tx?.IsAlive==true && Math.Abs(requested.RateHz-config.RateHz)>1e-6) throw new InvalidOperationException("Stop TX before changing RX sample rate (shared streaming bitfile).");
                            if(hardware==null)
                            {
                                pendingRx=requested;reply="OK RX configuration saved for next START; hardware readback unchanged";
                            }
                            else
                            {
                            if(state!=EngineState.RUNNING) throw new InvalidOperationException("RX is not running");
                            if(requested!=config)
                            {
                                if(tx?.IsAlive==true && Math.Abs(requested.RateHz-config.RateHz)>1e-6) throw new InvalidOperationException("Stop TX before changing RX sample rate (shared streaming bitfile). Center/ref may change independently.");
                                var previous=config;
                                try {Configure(requested);}
                                catch(Exception ex)
                                {
                                    niError=ex is NiException ni?ni.Code:0;
                                    try {Configure(previous); Log.Event("WARN","rx.rollback",new{previous,cause=ex.Message});}
                                    catch(Exception rollback)
                                    {
                                        ring.Pause(); hardware.Dispose(); hardware=null; state=EngineState.ERROR;
                                        error="Configuration and rollback failed: "+rollback.Message;
                                        throw new InvalidOperationException(error,ex);
                                    }
                                    throw;
                                }
                            }
                            var epoch=ring.Snapshot().Epoch;
                            reply=request.Line.StartsWith("CONFIG2 ")
                                ? $"OK center_hz={JsonDefaults.Number(config.CenterHz)},rate_hz={JsonDefaults.Number(config.RateHz)},reference_level_dbm={JsonDefaults.Number(config.ReferenceDbm)},preamp_mode=auto,preamp_actual={front!.ActualMode},preamp_actual_code={front.PreampActual},preamp_present={front.PreampPresent},effective_bandwidth_hz={JsonDefaults.Number(front.BandwidthHz)},epoch={epoch},request_id={requestId}"
                                : $"OK {JsonDefaults.Number(config.CenterHz)} {JsonDefaults.Number(config.RateHz)} {JsonDefaults.Number(config.ReferenceDbm)} {epoch}";
                            }
                        }
                        controlError=""; niError=0;
                    }
                    catch(Exception ex)
                    {
                        if(state is EngineState.INITIALIZING or EngineState.TUNING)
                        {ring.Pause(); hardware?.Dispose(); hardware=null; state=EngineState.ERROR; error=ex.Message;}
                        controlErrors++; controlError=ex.Message; niError=ex is NiException ni?ni.Code:niError;
                        reply="ERR "+ex.Message.Replace('\n',' ').Replace('\r',' ');
                    }
                    Telemetry(); Log.Event(reply.StartsWith("OK")?"INFO":"WARN","control.completed",new{request_id=requestId,command=request.Line,reply,elapsed_ms=started.Elapsed.TotalMilliseconds,epoch=Snapshot.Ring.Epoch,ni_error=niError});
                    request.Completion.TrySetResult(reply);
                }
                if(hardware!=null && state==EngineState.RUNNING)
                {
                    try
                    {
                        Publish();
                        if(timer.Elapsed.TotalSeconds-lastMetric>=.5)
                        {
                            if(hardware.Overflow) {overflow++; throw new NiException("FPGA overflow",-1,"FIFO did not keep up");}
                            lastMetric=timer.Elapsed.TotalSeconds; Telemetry(true);
                        }
                    }
                    catch(Exception ex)
                    {
                        ring.Pause(); error=ex.Message; state=EngineState.RECOVERING; Telemetry(); Log.Event("ERROR","rx.read_failed",error);
                        hardware.Dispose(); hardware=null;
                        for(int attempt=1;attempt<=3 && !stop.IsCancellationRequested;attempt++)
                        {
                            recoveries++;
                            try { if(stop.Token.WaitHandle.WaitOne(250*attempt)) break; Open(); break; }
                            catch(Exception recovery) {hardware?.Dispose(); hardware=null; error=recovery.Message; Log.Event("ERROR","rx.recovery_failed",new{attempt,error});}
                        }
                        if(hardware==null) {state=EngineState.ERROR; Telemetry();}
                    }
                }
                else
                {
                    wake.WaitOne(100);
                    if(timer.Elapsed.TotalSeconds-lastMetric>=.5) {lastMetric=timer.Elapsed.TotalSeconds; Telemetry(true);}
                }
            }
        }
        catch(Exception ex) {state=EngineState.ERROR; error=ex.Message; Log.Event("ERROR","engine.failed",error); Telemetry();}
        finally
        {
            tx?.Dispose();ring?.Pause(); hardware?.Dispose(); hardware=null;device?.Dispose();device=null;
            if(state!=EngineState.ERROR) state=EngineState.STOPPED;
            Telemetry(true); server?.Dispose(); ring?.Dispose();
            while(requests.TryDequeue(out var request)) request.Completion.TrySetResult("ERR engine stopped");
            if(owns) owner.ReleaseMutex(); Log.Event("INFO","engine.closed");
        }
    }
    public void Dispose()
    {
        stop.Cancel(); wake.Set(); worker?.Join(); Log.Dispose(); wake.Dispose(); stop.Dispose();
    }
}
