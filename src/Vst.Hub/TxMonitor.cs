using Vst.Core;

namespace Vst.Hub;

internal sealed partial class MonitorForm
{
    private readonly Label txSource=new(),txDma=new(),txProcessed=new(),txSourceBits=new(),txDmaBits=new(),txProcessedBits=new();
    private readonly Label txState=new(),txQueueLabel=new(),txHostLabel=new(),txFpgaLabel=new(),txDiagnostic=new();
    private readonly MeterBar txQueueBar=new(),txHostBar=new(),txFpgaBar=new();
    private readonly TrendPlot txTrend=new(){Caption="Last 60 s  ·  Green DMA / Blue FPGA processed  [MS/s]"};
    private readonly TextBox txPath=new();
    private readonly NumberChoice txFrequency=new(),txPeak=new(),txRate=new(),txQueue=new(),txFifo=new(),txPrefill=new();
    private readonly CheckBox txRf=new HubCheckBox(){Text="Enable RF",AutoSize=true,ForeColor=Color.WhiteSmoke};
    private readonly Button txStart=new HubButton(),txStop=new HubButton();
    private void BuildTxMonitor(Control parent)
    {
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(4)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for(int i=0;i<3;i++)layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
    private static string TxStatusText(string status) => status switch
    {
        "STREAMING"=>"Running", "WAITING_CLIENT"=>"No client data", "CONFIGURING"=>"Configuring",
        "PREFILLING"=>"Prefilling", "STOPPING"=>"Stopping", "FAULT"=>"Error", _=>"Stopped"
    };
    private TxConfiguration TxDraft() => options.Tx with
    {
        Source="file",WaveformPath=txPath.Text.Trim(),CenterHz=(double)txFrequency.Value*1e6,
        RateHz=(double)txRate.Value*1e6,PeakDbm=(double)txPeak.Value,RfEnabled=txRf.Checked,
        QueueMiB=IntegerChoice(txQueue),FifoMiB=IntegerChoice(txFifo),PrefillBlocks=IntegerChoice(txPrefill)
    };
    private void SaveTxDraft(TxConfiguration config)
    {
        options=options with {Tx=config with {RfEnabled=false}};files.Save(options);
        engine.Log.Event("INFO","tx.configuration_saved",options.Tx);
    }
    private void BuildTxConfiguration(Control parent)
    {
        var table=ConfigurationForm(parent);
        txPath.Text=options.Tx.WaveformPath;
        var fileRow=new TableLayoutPanel{AutoSize=true,ColumnCount=2,Dock=DockStyle.Top};
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        txPath.Dock=DockStyle.Fill;txPath.BackColor=PanelColor;txPath.ForeColor=Color.WhiteSmoke;
        var browse=new HubButton();Button(browse,"Browse…",()=>
        {
            using var dialog=new OpenFileDialog{Filter="IQ waveforms|*.tdms;*.tmds;*.cs16",Title="Select TDMS I/Q or CS16 with metadata"};
            if(dialog.ShowDialog(this)==DialogResult.OK)txPath.Text=dialog.FileName;return Task.CompletedTask;
        });fileRow.Controls.Add(txPath,0,0);fileRow.Controls.Add(browse,1,0);
        ConfigurationRow(table,"Waveform",fileRow,"Repeat a TDMS I/Q or CS16 waveform. The file sample rate must match the TX rate; loading and validation finish before RF starts.");
        txRate.Configure(1,120,(decimal)(options.Tx.RateHz/1e6),1,5,10,20,30.72m,40,60,61.44m,80,100,120);
        txFrequency.Configure(65,6000,(decimal)(options.Tx.CenterHz/1e6),100,433,915,1000,2400,2450,2500,3500,5800);
        txPeak.Configure(-50,0,(decimal)options.Tx.PeakDbm,-50,-40,-30,-20,-10,0);
        txQueue.Configure(16,256,options.Tx.QueueMiB,16,32,64,128,256);
        txFifo.Configure(64,512,options.Tx.FifoMiB,64,128,256,512);
        txPrefill.Configure(4,32,options.Tx.PrefillBlocks,4,8,16,24,32);
        ConfigurationRow(table,"Sample rate / MS/s",txRate,"Requested TX complex IQ rate, 1–120 MS/s. File metadata must match. Hardware readback is displayed in TX Monitor.");
        ConfigurationRow(table,"Center / MHz",txFrequency,"Independent RF output center, 65 MHz–6 GHz.");
        ConfigurationRow(table,"Peak level / dBm",txPeak,"RFSG peak level, −50…0 dBm. Average output power also depends on waveform RMS.");
        ConfigurationRow(table,"Source queue / MiB",txQueue,"Bounded CS16 queue, 16–256 MiB in multiples of 4. A full queue applies backpressure without overwriting IQ.");
        ConfigurationRow(table,"Host DMA FIFO / MiB",txFifo,"Requested TX DMA FIFO, 64–512 MiB. The monitor reports actual allocation.");
        ConfigurationRow(table,"Prefill / blocks",txPrefill,"File playback prefill: 4–32 blocks, 4 MiB per block, less than the FIFO capacity. Live TX uses a larger startup reserve, up to 48 blocks.");
        txRf.Checked=false;ConfigurationRow(table,"RF output",txRf,"Explicitly enable RF when starting TX. RF is disabled on stop, missing client data, or a hardware error.");
        var save=new HubButton();Button(save,"Apply TX",async()=>{var config=TxDraft();(config with {Source="live_ring"}).Validate();if(await Send("TXDEFAULTS "+JsonDefaults.Serialize(config)))SaveTxDraft(config);});
        ConfigurationRow(table,"",ActionRow(save),"Apply file playback settings. Start and Stop remain visible in the window footer. Live GNU Radio TX is started by the Soapy sink.");
    }
    private void RefreshTx(TxSnapshot s)
    {
        txSource.Text=$"{s.SourceMsps:F2} MS/s";txDma.Text=$"{s.DmaMsps:F2} MS/s";txProcessed.Text=$"{s.ProcessedMsps:F2} MS/s";
        txSourceBits.Text=$"{HubOptions.RawIqGbps(s.SourceMsps*1e6):F3} Gbps";txDmaBits.Text=$"{HubOptions.RawIqGbps(s.DmaMsps*1e6):F3} Gbps";txProcessedBits.Text=$"{HubOptions.RawIqGbps(s.ProcessedMsps*1e6):F3} Gbps";
        txState.Text=$"{TxStatusText(s.Status)}   ·   RF {(s.RfEnabled?"ON":"OFF")}   ·   {s.AppliedRateHz/1e6:F3} MS/s   ·   {s.AppliedCenterHz/1e6:F3} MHz   ·   Peak {s.AppliedPeakDbm:F1} dBm\r\n{s.Waveform}";
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
        txPath.Enabled=txFrequency.Enabled=txPeak.Enabled=txRate.Enabled=txQueue.Enabled=txFifo.Enabled=txPrefill.Enabled=txRf.Enabled=!active;
    }
}
