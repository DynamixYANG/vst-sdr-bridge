using System.Diagnostics;
using Vst.Core;
namespace Vst.Hub;

internal sealed partial class MonitorForm
{
    private readonly Label bridgeStatus=new(),bridgePipelines=new();
    private readonly TextBox grPath=new();
    private void BuildBridge(Control parent)
    {
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16)};
        parent.Controls.Add(layout);
        void TextRow(string text,int size=11) {var label=Label(text,size,Muted);label.AutoSize=true;label.Margin=new Padding(0,0,0,18);layout.Controls.Add(label);}
        TextRow("Connect your radio workflow",20);
        TextRow("Configure, launch, monitor and debug GNU Radio / GQRX through one NI device owner.\r\nRX and TX start and stop independently. Run either direction, or both at 120 MS/s.");
        bridgeStatus.AutoSize=true;bridgeStatus.ForeColor=Accent;bridgeStatus.Font=new Font(Font,FontStyle.Bold);bridgeStatus.Margin=new Padding(0,0,0,18);layout.Controls.Add(bridgeStatus);
        bridgePipelines.AutoSize=true;bridgePipelines.ForeColor=Color.WhiteSmoke;bridgePipelines.Margin=new Padding(0,0,0,18);layout.Controls.Add(bridgePipelines);
        TextRow("RX: RF IN → FPGA DMA → Hub shared memory → SoapySDR → GNU Radio / GQRX\r\nTX: GNU Radio / SoapySDR or TDMS / CS16 → Hub queue → FPGA DMA → RF OUT");
        TextRow("Choose your application",14);
        TextRow("GQRX: live spectrum, waterfall and receive demodulation.\r\nGNU Radio Companion: editable RX, TX and full-duplex flowgraphs.\r\nOne active RX consumer; a GNU Radio TX flowgraph can run alongside GQRX RX.");
        grPath.Text=options.GnuRadioPath;grPath.Width=820;grPath.BackColor=PanelColor;grPath.ForeColor=Color.WhiteSmoke;layout.Controls.Add(grPath);
        var row=new FlowLayoutPanel{AutoSize=true,Margin=new Padding(0,10,0,18)};
        var save=new HubButton();Button(save,"Save GNU Radio Path",()=>{var updated=options with{GnuRadioPath=grPath.Text.Trim()};if(!File.Exists(updated.GnuRadioPath))throw new FileNotFoundException("GNU Radio executable not found.");files.Save(updated);options=updated;return Task.CompletedTask;});row.Controls.Add(save);
        var stopTx=new HubButton();Button(stopTx,"Stop TX / RF Off",async()=>await Send("TXSTOP"));row.Controls.Add(stopTx);
        var export=new HubButton();Button(export,"Export Debug Snapshot",()=>{using var dialog=new SaveFileDialog{Filter="JSON snapshot|*.json",FileName="vst-bridge-diagnostics.json"};if(dialog.ShowDialog(this)==DialogResult.OK)AppFiles.Atomic(dialog.FileName,JsonDefaults.Serialize(engine.Snapshot));return Task.CompletedTask;});row.Controls.Add(export);layout.Controls.Add(row);
        TextRow("Use RX / TX Configuration for rates, tuning and playback. Monitor pages expose every queue,\r\nDMA counter, heartbeat, loss and recovery. Logs & Debug shows timestamped engine events.\r\nFile playback loads validated IQ into memory before streaming; no Python runtime is required.",10);
    }
    private void RefreshBridge(HubSnapshot s)
    {
        bridgeStatus.Text=$"RX {s.Status}  ·  TX {s.Tx.Status}  ·  RF {(s.Tx.RfEnabled?"ON":"OFF")}";
        bridgePipelines.Text=$"RX DMA {s.DmaMsps:F2} → client {s.DeliveredMsps:F2} MS/s  |  TX source {s.Tx.SourceMsps:F2} → FPGA {s.Tx.ProcessedMsps:F2} MS/s\r\n"+
            $"RX client: {s.ClientState}  ·  TX source: {s.Tx.Waveform}\r\nControl: 127.0.0.1:{options.ControlPort}  ·  Device: {options.Resource}  ·  driver=vst";
    }
}
