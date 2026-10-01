using Vst.Core;
using System.Text.Json;
var root=Path.GetFullPath(args[0]);var checks=new List<string>();
foreach(var file in Directory.GetFiles(root,"*.tdms"))
{
    bool reject=Path.GetFileName(file).StartsWith("reject-");
    try
    {
        var actual=WaveformFile.Load(file,120e6,out _);
        if(reject)throw new Exception("Malformed fixture accepted: "+file);
        var expected=File.ReadAllBytes(Path.ChangeExtension(file,".expected"));
        if(!actual.SequenceEqual(expected))throw new Exception("IQ mismatch: "+file);
        checks.Add("PASS "+Path.GetFileName(file));
    }
    catch(InvalidDataException) when(reject) {checks.Add("REJECT "+Path.GetFileName(file));}
}
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks}));
