using System.Globalization;
using Vst.Core;

namespace Vst.Hub;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture=CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture=CultureInfo.GetCultureInfo("en-US");
        ApplicationConfiguration.Initialize();
        string? Value(string key) {int i=Array.IndexOf(args,key); return i>=0&&i+1<args.Length?args[i+1]:null;}
        var files=new AppFiles(Value("--data-dir"));
        try
        {
            if(args.Contains("--self-test")) return SelfTests.Run(files.Root);
            var options=files.Load();
            if(args.Contains("--launch-gqrx")) {files.LaunchGqrx(options); return 0;}
            using var instance=new Mutex(false,@"Local\VSTHub.app."+options.Resource);
            bool owns;
            try {owns=instance.WaitOne(0);} catch(AbandonedMutexException) {owns=true;}
            if(!owns) throw new InvalidOperationException("VST Hub is already running. Use the existing monitor window.");
            if(args.Contains("--headless"))
            {
                using var engine=new HubEngine(options,files.LogDirectory); engine.Start();
                var seconds=double.Parse(Value("--seconds")??"0",CultureInfo.InvariantCulture);
                var start=DateTime.UtcNow;
                using var done=new ManualResetEventSlim();
                Console.CancelKeyPress+=(_,e)=>{e.Cancel=true; done.Set();};
                while(!done.Wait(500))
                {
                    try {AppFiles.Atomic(files.StatusPath,JsonDefaults.Serialize(engine.Snapshot));} catch(IOException) { }
                    if(engine.ShutdownRequested || (seconds>0 && (DateTime.UtcNow-start).TotalSeconds>=seconds)) break;
                }
                return engine.Snapshot.Status==EngineState.ERROR?1:0;
            }
            Application.Run(new MonitorForm(files,options,Value("--render-check")));
            return 0;
        }
        catch(Exception ex)
        {
            AppFiles.Atomic(Path.Combine(files.Root,"startup-error.json"),JsonDefaults.Serialize(new{time=DateTimeOffset.Now,error=ex.ToString()}));
            if(!args.Contains("--headless")) MessageBox.Show(ex.Message+"\n\n"+files.ConfigPath,"VST Hub",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }
}
