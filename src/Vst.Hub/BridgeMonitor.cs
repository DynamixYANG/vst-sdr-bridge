using Vst.Core;
namespace Vst.Hub;

internal sealed partial class MonitorForm
{
    private readonly TextBox grPath=new(),grArguments=new(),gqrxArguments=new();
    private void SaveBridgeSettings()
    {
        var updated=options with {GqrxPath=gqrxPath.Text.Trim(),GnuRadioPath=grPath.Text.Trim(),GqrxArguments=gqrxArguments.Text.Trim(),GnuRadioArguments=grArguments.Text.Trim()};
        files.Save(updated);options=updated;
        engine.Log.Event("INFO","bridge.launch_settings_saved",new {updated.GqrxPath,updated.GqrxArguments,updated.GnuRadioPath,updated.GnuRadioArguments});
    }
    private void BuildBridge(Control parent)
    {
        var table=ConfigurationForm(parent);
        gqrxPath.Text=options.GqrxPath;grPath.Text=options.GnuRadioPath;
        gqrxArguments.Text=options.GqrxArguments;grArguments.Text=options.GnuRadioArguments;
        arguments.ReadOnly=true;arguments.Text=AppFiles.DeviceArguments(options);
        ConfigurationRow(table,"GQRX executable",gqrxPath,"Path to radioconda Library/bin/gqrx.exe, using SoapySDR 0.8 x64.");
        ConfigurationRow(table,"GQRX launch arguments",gqrxArguments,"Additional arguments passed directly to GQRX. The Bridge supplies its generated -c configuration. Quote paths containing spaces.");
        ConfigurationRow(table,"GQRX device",arguments,"Device string for the shared RX bridge. Only one active RX client may attach.");
        Button(gqrx,"Launch GQRX",()=>{SaveBridgeSettings();engine.Log.Event("INFO","application.launch","Starting GQRX with bridge configuration");files.LaunchGqrx(options);return Task.CompletedTask;});
        var copy=new HubButton();Button(copy,"Copy Device",()=>{Clipboard.SetText(arguments.Text);return Task.CompletedTask;});
        ConfigurationRow(table,"",ActionRow(gqrx,copy),"Launch GQRX or copy its device arguments.");
        ConfigurationRow(table,"GNU Radio executable",grPath,"Path to radioconda Scripts/gnuradio-companion.exe.");
        ConfigurationRow(table,"GNU Radio launch arguments",grArguments,"Arguments passed directly to GNU Radio Companion, such as a quoted .grc example path.");
        var launch=new HubButton();Button(launch,"Launch GNU Radio",()=>{SaveBridgeSettings();engine.Log.Event("INFO","application.launch","Starting GNU Radio Companion");files.LaunchGnuRadio(options);return Task.CompletedTask;});
        ConfigurationRow(table,"",ActionRow(launch),"Launch GNU Radio Companion. RX, TX and duplex flowgraphs use driver=vst,resource=RIO0.");
        Button(applyPath,"Save Launch Settings",()=>{SaveBridgeSettings();engine.Log.Event("INFO","plugin.ready",files.InstallPlugin(options));return Task.CompletedTask;});
        ConfigurationRow(table,"",ActionRow(applyPath),"Save both applications' executable paths and arguments, and verify the embedded Soapy module.");
    }
    private void RefreshBridge(HubSnapshot s) => arguments.Text=AppFiles.DeviceArguments(options);
}
