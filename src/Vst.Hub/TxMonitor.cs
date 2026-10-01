using Vst.Core;

namespace Vst.Hub;

internal sealed partial class MonitorForm
{
    private readonly Label txSource=new(),txDma=new(),txProcessed=new(),txSourceBits=new(),txDmaBits=new(),txProcessedBits=new();
    private readonly Label txState=new(),txQueueLabel=new(),txHostLabel=new(),txFpgaLabel=new(),txDiagnostic=new();
    private readonly MeterBar txQueueBar=new(),txHostBar=new(),txFpgaBar=new();
    private readonly TrendPlot txTrend=new(){Caption="Last 60 s  ·  Green DMA / Blue FPGA processed  [MS/s]"};
    private readonly TextBox txPath=new();
    private readonly NumericUpDown txFrequency=new(),txPeak=new();
    private readonly CheckBox txRf=new(){Text="Enable RF output on explicit Start TX",AutoSize=true};
    private readonly Button txStart=new HubButton(),txStop=new HubButton();
    private void BuildTxMonitor(Control parent)
    {
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(4)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for(int i=0;i<3;i++)layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));parent.Controls.Add(layout);
        var cards=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=3,RowCount=1};
        for(int i=0;i<3;i++)cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));
        cards.Controls.Add(Card("Source",txSource,txSourceBits,"File / live IQ -> bounded queue","Measured input rate. Producer waits when the queue is full; samples are never overwritten."),0,0);
        cards.Controls.Add(Card("TX DMA",txDma,txDmaBits,"Host queue -> FPGA FIFO","Samples accepted by DMA. Host prefill temporarily exceeds the output sample rate."),1,0);
        cards.Controls.Add(Card("FPGA Processed",txProcessed,txProcessedBits,"Hardware output-path counter","FPGA processed counter with 32-bit wrap extension. This is not an RF power measurement."),2,0);
        layout.Controls.Add(cards);
        txState.AutoSize=true;txState.Dock=DockStyle.Fill;txState.Padding=new Padding(4,6,4,6);txState.ForeColor=Muted;layout.Controls.Add(txState);
        BufferRow(layout,"TX Source Queue",txQueueLabel,txQueueBar,"Bounded in-process CS16 queue. Full queue means normal backpressure.");
        BufferRow(layout,"TX Host DMA FIFO",txHostLabel,txHostBar,"NI host FIFO capacity minus free entries. Reserve time = occupancy / applied rate.");
        BufferRow(layout,"TX FPGA FIFO",txFpgaLabel,txFpgaBar,"Raw FPGA fullness and minimum elements. Installed bitfile declares 65,541 entries. Scaling requires correlation testing.");
        txDiagnostic.AutoSize=true;txDiagnostic.Dock=DockStyle.Fill;txDiagnostic.ForeColor=Muted;txDiagnostic.Padding=new Padding(4,6,4,8);layout.Controls.Add(txDiagnostic);
        txTrend.Dock=DockStyle.Fill;txTrend.BackColor=PanelColor;layout.Controls.Add(txTrend);
    }
    private void BuildTxConfiguration(Control parent)
    {
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,ColumnCount=1,RowCount=7,Padding=new Padding(12)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));parent.Controls.Add(layout);
        for(int i=0;i<7;i++)layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var intro=Label("TX streaming · 120 MS/s · TDMS / CS16 / live Soapy IQ\r\nDirect file playback or GNU Radio live streaming. RF output defaults to OFF. TX and RX start/stop independently on the shared device session.",11,Muted);
        intro.AutoSize=true;intro.Margin=new Padding(0,0,0,16);layout.Controls.Add(intro);
        var fileRow=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2};
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        txPath.Dock=DockStyle.Fill;txPath.Text=options.Tx.WaveformPath;txPath.BackColor=PanelColor;txPath.ForeColor=Color.WhiteSmoke;
        var browse=new HubButton();Button(browse,"Select waveform…",()=>
        {
            using var dialog=new OpenFileDialog{Filter="IQ waveforms|*.tdms;*.tmds;*.cs16|NI TDMS|*.tdms;*.tmds|CS16 + JSON|*.cs16",Title="Select TDMS (I/Q channels) or CS16 with JSON metadata"};
            if(dialog.ShowDialog(this)==DialogResult.OK)txPath.Text=dialog.FileName;return Task.CompletedTask;
        });fileRow.Controls.Add(txPath,0,0);fileRow.Controls.Add(browse,1,0);layout.Controls.Add(fileRow);
        var settings=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Padding=new Padding(0,12,0,12)};
        txFrequency.Minimum=65;txFrequency.Maximum=6000;txFrequency.DecimalPlaces=3;txFrequency.Width=130;txFrequency.Value=(decimal)(options.Tx.CenterHz/1e6);
        txPeak.Minimum=-50;txPeak.Maximum=0;txPeak.DecimalPlaces=1;txPeak.Width=90;txPeak.Value=(decimal)options.Tx.PeakDbm;
        var f=Label("Center (MHz)",10,Muted);f.AutoSize=true;var p=Label("Peak level (dBm)",10,Muted);p.AutoSize=true;
        settings.Controls.AddRange([f,txFrequency,p,txPeak]);layout.Controls.Add(settings);
        txRf.Checked=false;txRf.Margin=new Padding(0,4,0,12);layout.Controls.Add(txRf);
        var buffers=Label($"Source queue: {options.Tx.QueueMiB} MiB   ·   Host DMA FIFO: {options.Tx.FifoMiB} MiB   ·   Prefill: {options.Tx.PrefillBlocks*4} MiB\r\nRFSG peak-level mode. Average RF power depends on waveform RMS and the calibrated signal path.\r\nFour 20 MHz NR carriers fit the nominal 80 MHz RF bandwidth; 120 MS/s is the IQ rate.",10,Muted);buffers.AutoSize=true;layout.Controls.Add(buffers);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(0,18,0,12)};
        Button(txStart,"Start TX",async()=>
        {
            var config=options.Tx with {Source="file",WaveformPath=txPath.Text.Trim(),CenterHz=(double)txFrequency.Value*1e6,PeakDbm=(double)txPeak.Value,RfEnabled=txRf.Checked};config.Validate();
            if(await Send("TXSTART "+JsonDefaults.Serialize(config))) {options=options with {Tx=config with {RfEnabled=false}};files.Save(options);}
        });
        Button(txStop,"Stop TX",async()=>await Send("TXSTOP"));actions.Controls.AddRange([txStart,txStop]);layout.Controls.Add(actions);
        var note=Label("TX faults latch OFF and require explicit restart. RX center/ref can change while TX runs; stop TX before changing RX sample rate.\r\nTX Monitor shows measured rates, reserves, priming and underflows.\r\nGNU Radio / Soapy writeStream provides live TX. GQRX is a receive application.\r\nRF OUT (front-panel TX) is independent of RF IN (RX). Antenna coupling needs enough peak power for path loss.",10,Muted);note.AutoSize=true;layout.Controls.Add(note);
    }
    private void RefreshTx(TxSnapshot s)
    {
        txSource.Text=$"{s.SourceMsps:F2} MS/s";txDma.Text=$"{s.DmaMsps:F2} MS/s";txProcessed.Text=$"{s.ProcessedMsps:F2} MS/s";
        txSourceBits.Text=$"{HubOptions.RawIqGbps(s.SourceMsps*1e6):F3} Gbps";txDmaBits.Text=$"{HubOptions.RawIqGbps(s.DmaMsps*1e6):F3} Gbps";txProcessedBits.Text=$"{HubOptions.RawIqGbps(s.ProcessedMsps*1e6):F3} Gbps";
        txState.Text=$"{s.Status}   ·   RF {(s.RfEnabled?"ON":"OFF")}   ·   {s.AppliedRateHz/1e6:F3} MS/s   ·   {s.AppliedCenterHz/1e6:F3} MHz   ·   Peak {s.AppliedPeakDbm:F1} dBm\r\n{s.Waveform}";
        txState.ForeColor=s.Status=="FAULT"?Color.Salmon:Muted;
        double ms(ulong n)=>s.AppliedRateHz>0?n/s.AppliedRateHz*1000:0;
        txQueueLabel.Text=$"Source Queue     {s.QueueSamples*4d/1048576:F1} / {s.QueueCapacity*4d/1048576:F0} MiB     {s.QueuePercent:F1}%     Reserve {ms(s.QueueSamples):F1} ms";
        txHostLabel.Text=$"Host DMA FIFO     {s.HostFifoSamples*4d/1048576:F1} / {s.HostFifoCapacity*4d/1048576:F0} MiB     {s.HostFifoPercent:F1}%     Reserve {ms(s.HostFifoSamples):F1} ms";
        txFpgaLabel.Text=$"FPGA FIFO     Fullness {s.FpgaFifoSamples:N0}   ·   Minimum {s.FpgaMinSamples:N0}   ·   Primed {s.Primed}   ·   State {s.FpgaState}";
        txQueueBar.Percent=s.QueuePercent;txHostBar.Percent=s.HostFifoPercent;txFpgaBar.Percent=s.FpgaFifoSamples/65541d*100;
        txDiagnostic.Text=$"Underflows {s.Underflows}   ·   Submitted {s.SubmittedSamples:N0}   ·   Processed {s.ProcessedSamples:N0}\r\nMax DMA write {s.MaxWriteMs:F1} ms   ·   Source backpressure waits {s.SourceWaits:N0}"+(s.Error.Length>0?"\r\n"+s.Error:"");
        txDiagnostic.ForeColor=s.Error.Length>0?Color.Salmon:Muted;
        txTrend.Add(s.DmaMsps,s.ProcessedMsps);
        bool active=s.Status is "CONFIGURING" or "PREFILLING" or "STREAMING" or "STOPPING";
        txStart.Enabled=!active&&engine.Snapshot.Capabilities.Tx;txStop.Enabled=active;
        txPath.Enabled=txFrequency.Enabled=txPeak.Enabled=txRf.Enabled=!active;
    }
}
