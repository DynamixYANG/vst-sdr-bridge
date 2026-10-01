using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Vst.Core;

// Versioned JSON requests coexist with the established Soapy CONFIG/CONFIG2
// protocol. Control is serialized; RX and TX data workers run independently.
public sealed class ControlServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop=new();
    private readonly SemaphoreSlim clients=new(16);
    private readonly HubEngine engine;
    private readonly Task accept;
    public ControlServer(HubEngine engine,int port)
    {
        this.engine=engine;
        listener=new TcpListener(IPAddress.Loopback,port);
        listener.Server.ExclusiveAddressUse=true;
        listener.Start(16); accept=Task.Run(AcceptLoop);
    }
    private async Task AcceptLoop()
    {
        try
        {
            while(!stop.IsCancellationRequested)
            {
                var client=await listener.AcceptTcpClientAsync(stop.Token);
                if(!clients.Wait(0)) {client.Dispose(); continue;}
                _=Handle(client);
            }
        }
        catch(OperationCanceledException) { }
        catch(SocketException) when(stop.IsCancellationRequested) { }
    }
    private async Task Handle(TcpClient client)
    {
        using(client)
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                var stream=client.GetStream();
                var buffer=new byte[4097]; int length=0;
                while(length<buffer.Length)
                {
                    int n=await stream.ReadAsync(buffer.AsMemory(length,1),timeout.Token);
                    if(n==0 || buffer[length]=='\n') break;
                    length++;
                }
                string reply;
                if(length>4096) reply="ERR request exceeds 4096 bytes";
                else reply=await Dispatch(Encoding.UTF8.GetString(buffer,0,length).Trim());
                await stream.WriteAsync(Encoding.UTF8.GetBytes(reply+"\n"),timeout.Token);
            }
            catch(Exception e) when(e is IOException or OperationCanceledException or SocketException) { }
            finally {clients.Release();}
        }
    }
    public async Task<string> Dispatch(string line)
    {
        if(line=="STATUS") return JsonDefaults.Serialize(engine.Snapshot);
        if(line=="CAPABILITIES") return JsonDefaults.Serialize(engine.Snapshot.Capabilities);
        if(line=="GET last_error") return "VALUE "+engine.Snapshot.LastControlError.Replace('\n',' ');
        if(line.StartsWith('{'))
        {
            string? id=null;
            try
            {
                using var document=JsonDocument.Parse(line);
                var request=document.RootElement;
                id=request.TryGetProperty("id",out var ident)?ident.ToString():null;
                if(request.GetProperty("version").GetInt32()!=1) throw new ArgumentException("Unsupported API version");
                var method=request.GetProperty("method").GetString();
                if(method=="status") return JsonDefaults.Serialize(new {version=1,id,ok=true,result=engine.Snapshot});
                if(method=="capabilities") return JsonDefaults.Serialize(new {version=1,id,ok=true,result=engine.Snapshot.Capabilities});
                if(method=="app.shutdown") line="SHUTDOWN";
                else if(method=="tx.stop") line="TXSTOP";
                else if(method=="tx.start") line="TXSTART "+request.GetProperty("params").GetRawText();
                else if(method is "rx.start" or "rx.stop") line=method=="rx.start"?"START":"STOP";
                else if(method=="rx.configure")
                {
                    var values=request.GetProperty("params").EnumerateObject().Select(x=>$"{x.Name}={x.Value}");
                    line="CONFIG2 "+string.Join(',',values);
                }
                else throw new ArgumentException("Unsupported method");
                var response=await engine.Command(line);
                return JsonDefaults.Serialize(new {version=1,id,ok=response.StartsWith("OK "),result=response,status=engine.Snapshot});
            }
            catch(Exception ex) when(ex is JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException)
            { return JsonDefaults.Serialize(new {version=1,id,ok=false,error=ex.Message}); }
        }
        return await engine.Command(line);
    }
    public static RxConfiguration ParseConfiguration(string line,RxConfiguration old)
    {
        var fields=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        double Number(string value) => double.Parse(value,CultureInfo.InvariantCulture);
        if(fields.Length==4 && fields[0]=="CONFIG") return old with {CenterHz=Number(fields[1]),RateHz=Number(fields[2]),ReferenceDbm=Number(fields[3])};
        if(fields.Length!=2 || fields[0]!="CONFIG2") throw new ArgumentException("Use CONFIG2 key=value,... or CONFIG center rate ref");
        var result=old; var keys=new HashSet<string>();
        foreach(var field in fields[1].Split(','))
        {
            var pair=field.Split('=',2);
            if(pair.Length!=2 || !keys.Add(pair[0])) throw new ArgumentException("Malformed or duplicate key");
            result=pair[0] switch
            {
                "center_hz" => result with {CenterHz=Number(pair[1])},
                "rate_hz" => result with {RateHz=Number(pair[1])},
                "reference_level_dbm" => result with {ReferenceDbm=Number(pair[1])},
                "preamp_mode" => result with {PreampMode=pair[1]},
                _ => throw new ArgumentException("Unknown setting: "+pair[0])
            };
        }
        return result;
    }
    public void Dispose()
    {
        stop.Cancel(); listener.Stop(); accept.GetAwaiter().GetResult();
        // In-flight client tasks observe cancellation; semaphore lives until GC.
    }
}
