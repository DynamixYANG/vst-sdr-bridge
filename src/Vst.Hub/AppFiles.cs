using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Vst.Core;

namespace Vst.Hub;

internal sealed class AppFiles
{
    public string Root { get; }
    public string ConfigPath => Path.Combine(Root,"settings.json");
    public string LogDirectory => Path.Combine(Root,"logs");
    public string StatusPath => Path.Combine(Root,"status.json");
    public string GqrxConfig => Path.Combine(Root,"gqrx.conf");
    public AppFiles(string? root=null)
    {
        Root=Path.GetFullPath(root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VSTHub"));
        Directory.CreateDirectory(Root);
    }
    public HubOptions Load()
    {
        if(!File.Exists(ConfigPath)) {var defaults=new HubOptions(); Save(defaults); return defaults;}
        var options=JsonSerializer.Deserialize<HubOptions>(File.ReadAllText(ConfigPath),JsonDefaults.Options)??throw new InvalidDataException("Empty settings.json");
        options.Validate(); return options;
    }
    public void Save(HubOptions options)
    {
        options.Validate(); Atomic(ConfigPath,JsonDefaults.Serialize(options));
    }
    public static void Atomic(string path,string text)
    {
        var temporary=path+".tmp";
        File.WriteAllText(temporary,text);
        File.Move(temporary,path,true);
    }
    public static string DeviceArguments(HubOptions options) => $"soapy=0,driver=vst,resource={options.Resource}";
    public string InstallPlugin(HubOptions options)
    {
        var bin=Path.GetDirectoryName(options.GqrxPath)??throw new InvalidDataException("GQRX path invalid");
        var library=Directory.GetParent(bin)?.FullName??throw new InvalidDataException("GQRX library directory missing");
        if(!File.Exists(Path.Combine(bin,"SoapySDR.dll"))) throw new FileNotFoundException("Select radioconda Library\\bin\\gqrx.exe (Soapy 0.8 x64).");
        var destination=Path.Combine(library,@"lib\SoapySDR\modules0.8\vstSupport.dll");
        using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("vstSupport.dll")??throw new FileNotFoundException("Embedded Soapy module missing");
        using var memory=new MemoryStream(); resource.CopyTo(memory); var payload=memory.ToArray();
        if(File.Exists(destination) && SHA256.HashData(File.ReadAllBytes(destination)).SequenceEqual(SHA256.HashData(payload))) return "Soapy plugin is ready.";
        if(Process.GetProcessesByName("gqrx").Length!=0) throw new IOException("Close GQRX before updating the Soapy plugin.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if(File.Exists(destination))
        {
            var backup=Path.Combine(Root,"backups"); Directory.CreateDirectory(backup);
            File.Copy(destination,Path.Combine(backup,$"vstSupport-{DateTime.Now:yyyyMMdd-HHmmss}.dll"),true);
        }
        File.WriteAllBytes(destination+".tmp",payload); File.Move(destination+".tmp",destination,true);
        return "Soapy plugin installed. Any previous version was backed up.";
    }
    public void WriteGqrxConfig(HubOptions options)
    {
        File.WriteAllText(GqrxConfig,$"""
            [General]
            configversion=4
            crashed=false
            [input]
            device="{DeviceArguments(options)}"
            sample_rate={JsonDefaults.Number(options.Rx.RateHz)}
            frequency={JsonDefaults.Number(options.Rx.CenterHz)}
            [receiver]
            demod=Demod Off
            frequency={JsonDefaults.Number(options.Rx.CenterHz)}
            filter_offset=0
            sql_enabled=false
            [fft]
            fft_window=hann
            fft_avg=0.5
            panadapter_min_db=-120
            panadapter_max_db=0
            waterfall_min_db=-120
            plot_y_unit=dbfs
            [remote_control]
            enabled=true
            allowed_hosts=127.0.0.1
            [audio]
            gain=-60
            """);
    }
    public void LaunchGnuRadio(HubOptions options)
    {
        if (!File.Exists(options.GnuRadioPath)) throw new FileNotFoundException("Select gnuradio-companion.exe on the Bridge page.");
        InstallPlugin(options);
        var radio = Directory.GetParent(Path.GetDirectoryName(options.GnuRadioPath)!)!.FullName;
        var info = new ProcessStartInfo(options.GnuRadioPath) { UseShellExecute=false, WorkingDirectory=radio, Arguments=options.GnuRadioArguments };
        info.Environment["PATH"]=Path.Combine(radio,@"Library\bin")+";"+radio+";"+Path.Combine(radio,"Scripts")+";"+Environment.GetEnvironmentVariable("PATH");
        info.Environment["GR_CONF_DEFAULT_BUFFER_SIZE"]="1048576";
        info.Environment["SOAPY_SDR_ROOT"]=Path.Combine(radio,"Library");
        Process.Start(info)?.Dispose();
    }
    public void LaunchGqrx(HubOptions options)
    {
        if(Process.GetProcessesByName("gqrx").Length!=0) throw new InvalidOperationException("GQRX is already running. Use its existing window.");
        InstallPlugin(options);
        WriteGqrxConfig(options);
        var info=new ProcessStartInfo(options.GqrxPath) {UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(options.GqrxPath)};
        info.Arguments=$"-c \"{GqrxConfig}\" {options.GqrxArguments}";
        info.Environment["GR_CONF_DEFAULT_BUFFER_SIZE"]="1048576";
        info.Environment["SOAPY_SDR_ROOT"]=Directory.GetParent(Path.GetDirectoryName(options.GqrxPath)!)!.FullName;
        info.Environment["PATH"]=Path.GetDirectoryName(options.GqrxPath)+";"+Environment.GetEnvironmentVariable("PATH");
        Process.Start(info)?.Dispose();
    }
}
