using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Vst.Core;

/// <summary>Strict native IQ loader. Playback has no Python or disk-I/O dependency.</summary>
public static class WaveformFile
{
    public static byte[] Load(string path, double rate, out string sha256)
    {
        var info=new FileInfo(path);
        if(!info.Exists || info.Length<4096 || info.Length>512L*1048576)
            throw new InvalidDataException("Waveform file must be 4 KiB to 512 MiB.");
        using(var input=info.OpenRead()) sha256=Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        if(Path.GetExtension(path).Equals(".tdms",StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".tmds",StringComparison.OrdinalIgnoreCase))
            return LoadTdms(path,rate);
        if(!Path.GetExtension(path).Equals(".cs16",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Select .tdms or .cs16 IQ.");
        var bytes=File.ReadAllBytes(path);
        using var metadata=JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(path,".json")));
        var m=metadata.RootElement;
        if(bytes.Length%4!=0 || m.GetProperty("format").GetString()!="CS16_LE_IQ" || m.GetProperty("rate_hz").GetDouble()!=rate ||
           m.GetProperty("sha256").GetString()!=sha256 || m.GetProperty("samples").GetInt64()!=bytes.Length/4)
            throw new InvalidDataException("Waveform rate, format, samples or SHA-256 does not match metadata.");
        return bytes;
    }
    private sealed class Channel
    {
        public uint Type; public ulong Count; public bool Active;
        public Dictionary<string,object> Properties=new();
    }
    private static string String(BinaryReader r)
    {
        uint n=r.ReadUInt32();
        if(n>1048576 || n>r.BaseStream.Length-r.BaseStream.Position) throw new InvalidDataException("Invalid TDMS string length.");
        return Encoding.UTF8.GetString(r.ReadBytes((int)n)).TrimEnd('\0');
    }
    private static object Property(BinaryReader r,uint type) => type switch
    {
        1=>r.ReadSByte(),2=>r.ReadInt16(),3=>r.ReadInt32(),4=>r.ReadInt64(),5=>r.ReadByte(),6=>r.ReadUInt16(),7=>r.ReadUInt32(),8=>r.ReadUInt64(),
        9 or 0x19=>r.ReadSingle(),10 or 0x1a=>r.ReadDouble(),0x20=>String(r),0x21=>r.ReadByte()!=0,0x44=>r.ReadBytes(16),
        _=>throw new InvalidDataException($"Unsupported TDMS property type 0x{type:X}.")
    };
    private static int Width(uint type)=>type switch{2=>2,9=>4,10=>8,_=>throw new InvalidDataException("TDMS IQ channels must be Int16, Float32 or Float64.")};
    private static short Sample(BinaryReader r,uint type)
    {
        if(type==2)return r.ReadInt16();
        double x=type==9?r.ReadSingle():r.ReadDouble();
        if(!double.IsFinite(x) || x < -1 || x > 1) throw new InvalidDataException("Floating IQ must be finite and normalized to [-1,1].");
        return (short)Math.Round(x*32767);
    }
    private static byte[] LoadTdms(string path,double rate)
    {
        using var file=File.OpenRead(path);using var reader=new BinaryReader(file);
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);
        var known=new Dictionary<string,Channel>();var order=new List<string>();
        bool rateFound=false;
        while(file.Position<file.Length)
        {
            if(file.Length-file.Position<28 || Encoding.ASCII.GetString(reader.ReadBytes(4))!="TDSm")throw new InvalidDataException("Invalid TDMS segment.");
            uint toc=reader.ReadUInt32(),version=reader.ReadUInt32();long next=reader.ReadInt64(),raw=reader.ReadInt64(),start=file.Position;
            if((toc&0xc0)!=0 || version is not (4712 or 4713))throw new InvalidDataException("TDMS requires little-endian standard data; DAQmx scaling is unsupported.");
            long end=next==-1?file.Length:checked(start+next),rawStart=checked(start+raw);
            if(raw<0 || end>file.Length || end<rawStart || rawStart<start)throw new InvalidDataException("Truncated TDMS segment.");
            if((toc&2)!=0)
            {
                uint count=reader.ReadUInt32();if(count>10000)throw new InvalidDataException("Too many TDMS objects.");
                if((toc&4)!=0)order.Clear();
                for(uint n=0;n<count;n++)
                {
                    var name=String(reader);if(!known.TryGetValue(name,out var channel)) {channel=new();known.Add(name,channel);}
                    if(!order.Contains(name))order.Add(name);
                    uint index=reader.ReadUInt32();
                    if(index==uint.MaxValue)channel.Active=false;
                    else if(index==0) {if(channel.Count==0)throw new InvalidDataException("Missing previous TDMS raw index.");channel.Active=true;}
                    else
                    {
                        if(index!=20)throw new InvalidDataException("Unsupported TDMS raw index (expected one-dimensional numeric channels).");
                        channel.Type=reader.ReadUInt32();if(reader.ReadUInt32()!=1)throw new InvalidDataException("TDMS channel dimension must be 1.");
                        channel.Count=reader.ReadUInt64();channel.Active=true;_ = Width(channel.Type);
                    }
                    uint props=reader.ReadUInt32();if(props>10000)throw new InvalidDataException("Too many TDMS properties.");
                    for(uint p=0;p<props;p++){var key=String(reader);channel.Properties[key]=Property(reader,reader.ReadUInt32());}
                }
                if(file.Position!=rawStart)throw new InvalidDataException("TDMS metadata length mismatch.");
            }
            if((toc&8)!=0 && end>rawStart)
            {
                var names=order.Where(n=>known[n].Active).ToArray();
                if(names.Length!=2)throw new InvalidDataException("TDMS playback requires exactly two raw channels named I and Q in one group.");
                string? iName=names.FirstOrDefault(n=>n.EndsWith("/'I'",StringComparison.OrdinalIgnoreCase));
                string? qName=names.FirstOrDefault(n=>n.EndsWith("/'Q'",StringComparison.OrdinalIgnoreCase));
                if(iName==null || qName==null || iName[..^4]!=qName[..^4])throw new InvalidDataException("Expected matching TDMS I and Q channels.");
                foreach(var name in order)
                {
                    foreach(var pair in known[name].Properties.Where(p=>p.Key is "rate_hz" or "sample_rate_hz" or "wf_increment"))
                    {
                        double value=Convert.ToDouble(pair.Value,System.Globalization.CultureInfo.InvariantCulture);
                        double declared=pair.Key=="wf_increment"?1/value:value;
                        if(!double.IsFinite(declared) || Math.Abs(declared-rate)>rate*1e-9)throw new InvalidDataException("TDMS sample rate does not match TX (120 MS/s). Resample before playback.");
                        rateFound=true;
                    }
                }
                if(!rateFound)throw new InvalidDataException("TDMS requires rate_hz, sample_rate_hz or channel wf_increment metadata.");
                var ic=known[iName];var qc=known[qName];
                if(ic.Count==0 || ic.Count!=qc.Count || ic.Count>134217728)throw new InvalidDataException("TDMS I/Q sample counts must match.");
                int samples=checked((int)ic.Count);long chunk=checked((long)samples*(Width(ic.Type)+Width(qc.Type)));
                if((end-rawStart)%chunk!=0)throw new InvalidDataException("Truncated TDMS IQ chunk.");
                var iData=new short[samples];var qData=new short[samples];file.Position=rawStart;
                while(file.Position<end)
                {
                    if(output.Length+(long)samples*4>512L*1048576)throw new InvalidDataException("Decoded TDMS exceeds 512 MiB replay limit.");
                    if((toc&32)!=0)
                        for(int k=0;k<samples;k++)foreach(var name in names)(name==iName?iData:qData)[k]=Sample(reader,known[name].Type);
                    else
                        foreach(var name in names)for(int k=0;k<samples;k++)(name==iName?iData:qData)[k]=Sample(reader,known[name].Type);
                    for(int k=0;k<samples;k++){writer.Write(iData[k]);writer.Write(qData[k]);}
                }
            }
            file.Position=end;
        }
        if(output.Length<4096)throw new InvalidDataException("TDMS has fewer than 1024 IQ samples.");
        return output.ToArray();
    }
}
