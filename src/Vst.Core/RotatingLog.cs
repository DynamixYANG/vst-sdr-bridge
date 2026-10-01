using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;

namespace Vst.Core;

// Single writer, bounded memory, size AND UTC-day rotation, count/age retention.
// Disk failure is visible telemetry, never a reason to restart the hardware.
public sealed class RotatingLog : IDisposable
{
    private readonly Channel<object> queue=Channel.CreateBounded<object>(new BoundedChannelOptions(2048)
        {SingleReader=true,FullMode=BoundedChannelFullMode.Wait});
    private readonly Task writer;
    private readonly string directory, session;
    private readonly long limit;
    private readonly int files, days;
    private long dropped;
    private string error="";
    private readonly ConcurrentQueue<string> recent=new();
    public long Dropped => Interlocked.Read(ref dropped);
    public string Error => Volatile.Read(ref error);
    public string[] Recent => recent.ToArray();
    public string DirectoryPath => directory;
    public RotatingLog(string directory,string session,long limit=10*1048576,int files=10,int days=14)
    {
        this.directory=Path.GetFullPath(directory); this.session=session;
        this.limit=limit; this.files=files; this.days=days;
        Directory.CreateDirectory(this.directory);
        writer=Task.Run(WriteLoop);
    }
    public void Event(string level,string name,object? data=null)
    {
        var now=DateTimeOffset.UtcNow;
        recent.Enqueue($"{now.ToLocalTime():HH:mm:ss}  {level,-5}  {name}  {JsonDefaults.Serialize(data)}");
        while(recent.Count>60) recent.TryDequeue(out _);
        Enqueue(new {time=now,session_id=session,level,@event=name,data});
    }
    public void Metric(HubSnapshot snapshot) => Enqueue(new {time=DateTimeOffset.UtcNow,session_id=session,level="METRIC",@event="pipeline",data=snapshot});
    private void Enqueue(object item) {if(!queue.Writer.TryWrite(item)) Interlocked.Increment(ref dropped);}
    private async Task WriteLoop()
    {
        FileStream? stream=null; string current=""; DateOnly day=default; int sequence=0;
        try
        {
            await foreach(var item in queue.Reader.ReadAllAsync())
            {
                try
                {
                    var bytes=Encoding.UTF8.GetBytes(JsonDefaults.Serialize(item)+"\n");
                    var today=DateOnly.FromDateTime(DateTime.UtcNow);
                    if(stream==null || stream.Length+bytes.Length>limit || today!=day)
                    {
                        stream?.Dispose(); day=today;
                        current=Path.Combine(directory,$"hub-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{session}-{sequence++:D4}.jsonl");
                        stream=new FileStream(current,FileMode.CreateNew,FileAccess.Write,FileShare.Read|FileShare.Delete,65536,FileOptions.SequentialScan);
                        Retain(current);
                    }
                    await stream.WriteAsync(bytes);
                    await stream.FlushAsync();
                    Volatile.Write(ref error,"");
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
                {
                    Volatile.Write(ref error,ex.Message); Interlocked.Increment(ref dropped);
                    stream?.Dispose(); stream=null;
                    await Task.Delay(1000);
                }
            }
        }
        finally {stream?.Dispose();}
    }
    private void Retain(string current)
    {
        var all=new DirectoryInfo(directory).GetFiles("hub-*.jsonl").OrderByDescending(f=>f.LastWriteTimeUtc).ToArray();
        for(int i=0;i<all.Length;i++)
        {
            if(all[i].FullName==current) continue;
            if(i>=files || all[i].LastWriteTimeUtc<DateTime.UtcNow.AddDays(-days)) all[i].Delete();
        }
    }
    public void Dispose() {queue.Writer.TryComplete(); writer.GetAwaiter().GetResult();}
}
