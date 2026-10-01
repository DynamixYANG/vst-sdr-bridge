using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Vst.Core;

namespace Vst.Hub;

internal static class SelfTests
{
    public static int Run(string root)
    {
        var checks=new List<string>();
        void Check(bool value,string name) {if(!value) throw new InvalidOperationException(name); checks.Add(name);}
        var testRoot=Path.Combine(root,"selftest-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(testRoot);
        try
        {
            var c=ControlServer.ParseConfiguration("CONFIG2 reference_level_dbm=-30,center_hz=2400000000",new());
            Check(c.ReferenceDbm==-30 && c.CenterHz==2.4e9 && c.RateHz==120e6,"partial configuration preserves omitted fields");
            var rateOk=ControlServer.ParseConfiguration("CONFIG2 rate_hz=10000000",new());
            rateOk.Validate();
            Check(rateOk.RateHz==10e6,"variable sample rate 10 MS/s accepted");
            Check(Math.Abs(HubOptions.RawIqGbps(120e6)-3.84)<1e-9,"raw IQ Gbps formula at 120 MS/s");
            foreach(var command in new[]{"CONFIG2 reference_level_dbm=nan","CONFIG2 preamp_mode=on","CONFIG2 rate_hz=5e5","CONFIG2 rate_hz=200e6","CONFIG2 center_hz=1,center_hz=2"})
            {
                bool rejected=false;
                try {ControlServer.ParseConfiguration(command,new()).Validate();} catch(ArgumentException) {rejected=true;}
                Check(rejected,"reject "+command);
            }
            var ringOpts=new HubOptions{RingMiB=512}; ringOpts.Validate();
            Check(ringOpts.RingMiB==512,"editable shared memory 512 MiB accepted");
            bool ringRejected=false;
            try {new HubOptions{RingMiB=100}.Validate();} catch(ArgumentException) {ringRejected=true;}
            Check(ringRejected,"reject non-multiple shared memory size");
            TxTests(checks);
            using(var log=new RotatingLog(Path.Combine(testRoot,"rotation"),"test",2048,3,1))
                for(int i=0;i<120;i++) log.Event("INFO","test",new{index=i,payload=new string('x',180)});
            var logs=Directory.GetFiles(Path.Combine(testRoot,"rotation"),"*.jsonl");
            Check(logs.Length==3,"size rotation count retention");
            Check(logs.All(p=>new FileInfo(p).Length<=2048),"rotation file bounds");
            foreach(var file in logs) foreach(var line in File.ReadLines(file)) using(JsonDocument.Parse(line)) { }
            checks.Add("all retained log records parse as JSON");
            var portProbe=new TcpListener(IPAddress.Loopback,0); portProbe.Start(); int port=((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
            var options=new HubOptions {RingName=@"Local\vst_hub_test_"+Guid.NewGuid().ToString("N"),ControlPort=port};
            using(var engine=new HubEngine(options,Path.Combine(testRoot,"engine"),o=>new FakeRx(o.BlockSamples)))
            {
                engine.Start(); SpinWait.SpinUntil(()=>engine.Snapshot.Status==EngineState.RUNNING,10000);
                Check(engine.Snapshot.Status==EngineState.RUNNING,"fake driver initialization");
                var epoch=engine.Snapshot.Ring.Epoch;
                Check(engine.Command("CONFIG2 reference_level_dbm=nan").Result.StartsWith("ERR "),"invalid transaction rejected");
                Check(engine.Snapshot.Ring.Epoch==epoch,"invalid transaction does not interrupt stream");
                Check(engine.Command("CONFIG2 center_hz=123000000").Result.StartsWith("ERR "),"injected hardware error propagated");
                Check(engine.Snapshot.Status==EngineState.RUNNING && engine.Snapshot.AppliedCenterHz==1e9 && engine.Snapshot.Ring.Epoch>epoch,"hardware rollback restarts old configuration");
                Check(engine.Command("CONFIG2 rate_hz=40000000").Result.StartsWith("OK ") && Math.Abs(engine.Snapshot.AppliedRateHz-40e6)<1,"variable sample rate applied via control");
                Check(engine.Command("CONFIG2 reference_level_dbm=-30").Result.StartsWith("OK ") && engine.Snapshot.PreampActual=="on","reference configuration readback");
                SpinWait.SpinUntil(()=>engine.Snapshot.Ring.IdleDiscardSamples>0,5000);
                Check(engine.Snapshot.Ring.Drops==0 && engine.Snapshot.Ring.IdleDiscardSamples>0,"waiting receiver is separate from active loss");
                using(var socket=new TcpClient("127.0.0.1",port))
                {
                    using var writer=new StreamWriter(socket.GetStream()){AutoFlush=true}; writer.WriteLine("{\"version\":1,\"id\":\"a\",\"method\":\"capabilities\"}");
                    using var reader=new StreamReader(socket.GetStream()); using var reply=JsonDocument.Parse(reader.ReadLine()!);
                    Check(reply.RootElement.GetProperty("ok").GetBoolean() && !reply.RootElement.GetProperty("result").GetProperty("tx").GetBoolean(),"fake driver exposes TX only after a native device session exists");
                }
                Check(engine.Command("STOP").Result.StartsWith("OK ") && !engine.Snapshot.Ring.Active,"stop makes SHM inactive");
                Check(engine.Command("CONFIG2 rate_hz=20000000,center_hz=900000000").Result.StartsWith("OK "),"stopped RX accepts pending configuration");
                Check(engine.Snapshot.AppliedRateHz==40e6,"pending configuration preserves hardware readback until START");
                Check(engine.Command("START").Result.StartsWith("OK ") && engine.Snapshot.Ring.Active,"restart acquisition");
                Check(engine.Snapshot.AppliedRateHz==20e6 && engine.Snapshot.AppliedCenterHz==900e6,"START applies pending RX settings");
            }
            AppFiles.Atomic(Path.Combine(root,"selftest-result.json"),JsonDefaults.Serialize(new{status="PASS",checks})); return 0;
        }
        catch(Exception ex) {AppFiles.Atomic(Path.Combine(root,"selftest-result.json"),JsonDefaults.Serialize(new{status="FAIL",checks,error=ex.ToString()}));return 1;}
    }
    private static unsafe void TxTests(List<string> checks)
    {
        using var queue=new TxSampleQueue(16);
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Exception? failure=null;
        var producer=new Thread(()=>
        {
            try
            {
                for(int n=1;n<=32;n++)
                {
                    var data=new Span<int>((void*)queue.AcquireWrite(cancel.Token),TxSampleQueue.BlockSamples);
                    data.Fill(n);queue.Publish();
                }
            }
            catch(Exception ex){failure=ex;}
        });
        producer.Start();
        try
        {
            for(int n=1;n<=32;n++)
            {
                var data=new ReadOnlySpan<int>((void*)queue.AcquireRead(cancel.Token),TxSampleQueue.BlockSamples);
                for(int i=0;i<data.Length;i++) if(data[i]!=n) throw new InvalidOperationException("TX queue overwrote or reordered samples");
                queue.Release();
            }
        }
        finally {cancel.Cancel();producer.Join();}
        if(failure!=null)throw failure;
        checks.Add("TX bounded queue: all 33,554,432 samples ordered across eight wraps");
        if(queue.Occupancy!=0||queue.Produced!=queue.Consumed)throw new InvalidOperationException("TX queue accounting mismatch");
        checks.Add("TX queue occupancy returns to zero");
        using var full=new TxSampleQueue(16);
        for(int i=0;i<4;i++){full.AcquireWrite(CancellationToken.None);full.Publish();}
        using var blocked=new CancellationTokenSource(30);
        bool canceled=false;
        try{full.AcquireWrite(blocked.Token);}catch(OperationCanceledException){canceled=true;}
        if(!canceled||full.Occupancy!=full.Capacity)throw new InvalidOperationException("TX backpressure failed");
        checks.Add("TX full queue blocks and cancels without overwriting");
        bool invalid=false;
        try{new TxConfiguration{RateHz=122880000,WaveformPath="test"}.Validate();}catch(ArgumentException){invalid=true;}
        if(!invalid)throw new InvalidOperationException("TX accepted mismatched rate");
        checks.Add("TX rejects 122.88 MS/s instead of silently changing playback speed");
        new TxConfiguration{Source="live_ring"}.Validate();
        checks.Add("TX live_ring accepts empty waveform_path");
        bool liveBad=false;
        try{new TxConfiguration{Source="file",WaveformPath=""}.Validate();}catch(ArgumentException){liveBad=true;}
        if(!liveBad)throw new InvalidOperationException("file TX must require waveform_path");
        checks.Add("TX file mode still requires waveform_path");
        using(var ring=new SharedTxRing(@"Local\vst_tx_selftest_"+Guid.NewGuid().ToString("N"),16,true))
        {
            if(ring.CapacitySamples!=16UL*1048576UL/4UL) throw new InvalidOperationException("TX SHM capacity");
            ring.PublishApplied(2.5e9,120e6,-30,false,"PREFILLING");
        }
        checks.Add("TX SharedTxRing create/dispose");
    }
    private sealed unsafe class FakeRx(int samples) : IRxHardware
    {
        private readonly nint buffer=(nint)NativeMemory.AllocZeroed((nuint)samples*4);
        public int BlockSamples=>samples;
        public ulong FifoRemaining=>0;
        public ulong FifoCapacity=>67108864;
        public bool Overflow=>false;
        public Frontend Configure(RxConfiguration configuration)
        {
            if(configuration.CenterHz==123e6) throw new NiException("injected configure",-999,"expected self-test error");
            return new(configuration,configuration.ReferenceDbm<=-30?2502:2500,false,84e6);
        }
        public nint Read() {Thread.Sleep(8); return buffer;}
        public void Dispose() => NativeMemory.Free((void*)buffer);
    }
}
