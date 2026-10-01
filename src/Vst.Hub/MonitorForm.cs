using System.Diagnostics;
using Vst.Core;

namespace Vst.Hub;

internal sealed partial class MonitorForm : Form
{
    private static readonly Color Background=Color.FromArgb(14,21,33), PanelColor=Color.FromArgb(23,34,49), Muted=Color.FromArgb(153,174,192), Accent=Color.FromArgb(53,207,168);
    private readonly AppFiles files;
    private HubOptions options;
    private HubEngine engine;
    private readonly System.Windows.Forms.Timer timer=new(){Interval=500};
    private readonly ToolTip tips=new(){AutoPopDelay=12000,InitialDelay=350,ReshowDelay=200,ShowAlways=true};
    private readonly Label state=new(), details=new(), banner=new(), diagnostics=new(), pipelineDetail=new();
    private readonly Label dma=new(), shm=new(), client=new();
    private readonly Label dmaGbps=new(), shmGbps=new(), clientGbps=new();
    private readonly Label fifoLabel=new(), ringLabel=new();
    private readonly MeterBar fifo=new(), ring=new();
    private readonly TrendPlot trend=new();
    private readonly TextBox events=new(), arguments=new(), gqrxPath=new();
    private readonly NumericUpDown frequency=new(), reference=new(), sampleRate=new(), ringMiB=new();
    private readonly Button start=new HubButton(), halt=new HubButton(), apply=new HubButton(), gqrx=new HubButton(), applyPath=new HubButton();
    private bool closing, canClose;
    private string lastEvents="";
    private string notice="";
    private DateTime noticeUntil;
    public MonitorForm(AppFiles files,HubOptions options,string? renderCheck)
    {
        this.files=files; this.options=options;
        engine=new HubEngine(options,files.LogDirectory);
        Text="VST Bridge · GNU Radio / GQRX"; Size=new Size(1180,900); MinimumSize=new Size(1040,780);
        BackColor=Background; ForeColor=Color.WhiteSmoke; Font=new Font("Segoe UI",10);
        AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi; StartPosition=FormStartPosition.CenterScreen;
        // Text and actions take their preferred height; only the plot expands.
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20,20,20,20),ColumnCount=1,RowCount=7};
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));  // tabs
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,16));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,10));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        var heading=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=3,Padding=new Padding(0,0,0,10)};
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title=Label("VST Bridge",22,Color.White); title.AutoSize=true; title.Margin=new Padding(0,0,18,0); heading.Controls.Add(title,0,0);
        var subtitle=Label("GNU Radio / GQRX middleware  ·  v2.1",10,Muted); subtitle.AutoSize=true; subtitle.Anchor=AnchorStyles.Left; heading.Controls.Add(subtitle,1,0);
        state.Font=new Font(Font.FontFamily,13,FontStyle.Bold); state.ForeColor=Accent; state.AutoSize=true; state.Anchor=AnchorStyles.Right; state.TextAlign=ContentAlignment.MiddleRight; heading.Controls.Add(state,2,0); root.Controls.Add(heading);
        banner.Dock=DockStyle.Fill; banner.AutoSize=true; banner.Padding=new Padding(12,8,12,8); banner.BackColor=PanelColor; banner.ForeColor=Muted; root.Controls.Add(banner);
        tips.SetToolTip(banner,"Status messages and the last applied-settings notice appear here.");
        var tabs=new MonitorTabs{Dock=DockStyle.Fill,Padding=new Point(18,8)}; root.Controls.Add(tabs);
        var bridge=Page(tabs,"Bridge"); BuildBridge(bridge); var overview=Page(tabs,"RX Monitor"); var txPage=Page(tabs,"TX Monitor"); var connection=Page(tabs,"RX Configuration"); var txSetup=Page(tabs,"TX Configuration"); var logPage=Page(tabs,"Logs & Debug");
        BuildOverview(overview); BuildConnection(connection);
        BuildTxMonitor(txPage);BuildTxConfiguration(txSetup);
        events.Multiline=true; events.ReadOnly=true; events.ScrollBars=ScrollBars.Both; events.WordWrap=false; events.Dock=DockStyle.Fill;
        events.BackColor=PanelColor; events.ForeColor=Muted; events.Font=new Font("Consolas",10); logPage.Controls.Add(events);
        tips.SetToolTip(events,"Rotating UTC JSONL event log (newest at the bottom). Open Logs for files on disk.");
        root.Controls.Add(new Panel{Dock=DockStyle.Fill,BackColor=Background}); // spacer: tabs -> buttons
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(0,4,0,4),WrapContents=true,FlowDirection=FlowDirection.LeftToRight};
        Button(start,"Start / Retry RX",async()=>{if(!engine.IsAlive) engine.Start(); else await Send("START");});
        Button(halt,"Stop RX",async()=>await Send("STOP"));
        Button(gqrx,"Launch GQRX",()=>{try {files.LaunchGqrx(this.options);}catch(Exception ex){ShowError(ex.Message);} return Task.CompletedTask;});
        var copy=new HubButton(); Button(copy,"Copy Device String",()=>{Clipboard.SetText(arguments.Text); banner.Text="Device string copied. Set GQRX input rate to match Sample Rate.";return Task.CompletedTask;});
        var logs=new HubButton(); Button(logs,"Open Logs",()=>{Process.Start(new ProcessStartInfo(files.LogDirectory){UseShellExecute=true}); return Task.CompletedTask;});
        var grc=new HubButton(); Button(grc,"Launch GNU Radio",()=>{files.LaunchGnuRadio(this.options);return Task.CompletedTask;});
        buttons.Controls.AddRange([start,halt,gqrx,grc,copy,logs]); root.Controls.Add(buttons);
        root.Controls.Add(new Panel{Dock=DockStyle.Fill,BackColor=Background}); // spacer: buttons -> footer
        details.Dock=DockStyle.Fill; details.AutoSize=true; details.TextAlign=ContentAlignment.MiddleLeft; details.ForeColor=Muted; details.Font=new Font(Font.FontFamily,9); details.Margin=new Padding(0); root.Controls.Add(details);
        tips.SetToolTip(details,"Uptime, Hub CPU load (cores), logger drop count, and log folder path.");
        timer.Tick+=(_,_)=>RefreshSnapshot();
        Shown+=(_,_)=>
        {
            try {banner.Text=files.InstallPlugin(this.options);} catch(Exception ex) {ShowError(ex.Message);}
            engine.Start(); timer.Start();
            if(renderCheck!=null)
            {
                var renderTimer=new System.Windows.Forms.Timer{Interval=1000};
                int attempts=0;
                renderTimer.Tick+=async(_,_)=>
                {
                    if(engine.Snapshot.Status==EngineState.INITIALIZING && ++attempts<60) return;
                    renderTimer.Stop(); await Task.Delay(1500); RefreshSnapshot();
                    var originalSize=Size;
                    foreach(var size in new[]{originalSize,MinimumSize})
                    {
                        Size=size;
                        for(int page=0;page<tabs.TabCount;page++)
                        {
                            tabs.SelectedIndex=page; PerformLayout(); tabs.PerformLayout();
                            await Task.Delay(300); Refresh();
                            using var bitmap=new Bitmap(Width,Height); DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));
                            string suffix=(size==originalSize?"":"-minimum")+(page==0?"":$"-{page}");
                            bitmap.Save(Path.Combine(Path.GetDirectoryName(renderCheck)!,Path.GetFileNameWithoutExtension(renderCheck)+suffix+".png"));
                            var layoutEvidence=new {
                                window=new {Width,Height},dpi=DeviceDpi,page=tabs.SelectedTab!.Text,
                                rows=root.GetRowHeights(),
                                controls=root.Controls.Cast<Control>().Select(c=>new {kind=c.GetType().Name,c.Text,c.Left,c.Top,c.Width,c.Height,c.Visible}).ToArray()
                            };
                            File.WriteAllText(Path.Combine(Path.GetDirectoryName(renderCheck)!,Path.GetFileNameWithoutExtension(renderCheck)+suffix+".layout.json"),JsonDefaults.Serialize(layoutEvidence));
                        }
                    }
                    Size=originalSize; tabs.SelectedIndex=0; renderTimer.Dispose();
                };
                renderTimer.Start();
            }
        };
        FormClosing+=async(_,e)=>
        {
            if(canClose) return; e.Cancel=true; if(closing) return; closing=true; timer.Stop(); Enabled=false;
            Text="VST Hub · Releasing device…";
            await Task.Run(()=>{ try {engine.Dispose();} catch { } }); canClose=true; Close();
        };
    }
    private TabPage Page(TabControl tabs,string text)
    {
        var page=new TabPage(text){BackColor=Background,ForeColor=Color.WhiteSmoke,Padding=new Padding(10)}; tabs.TabPages.Add(page); return page;
    }
    private static Label Label(string text,float size,Color color) => new(){Text=text,ForeColor=color,Font=new Font("Segoe UI",size),AutoEllipsis=true};
    private void Tip(Control control,string text) => tips.SetToolTip(control,text);
    private void BuildOverview(Control parent)
    {
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        foreach(var h in new[]{34,48,48,56}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); parent.Controls.Add(layout);

        var cards=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true};
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.33f));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.33f));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.34f));
        cards.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cards.ColumnCount=3;
        cards.RowCount=1;
        var cardDma=Card("DMA",dma,dmaGbps,"FPGA -> host | raw IQ 4 B/sample","Measured complex sample rate from the FPGA DMA FIFO into Hub. Matching Gbps = RateHz x 32e-9.");
        var cardShm=Card("Shared Memory",shm,shmGbps,"Host -> SHM ring | no IQ files","Rate at which Hub publishes complex samples into the named shared-memory ring. Matching Gbps = RateHz x 32e-9.");
        var cardClient=Card("Delivered",client,clientGbps,"Soapy -> GQRX/GR | CF32 8 B/sample","Rate delivered to the Soapy consumer. Matching Gbps = RateHz x 32e-9 from the live delivered rate.");
        cards.Controls.Add(cardDma,0,0);
        cards.Controls.Add(cardShm,1,0);
        cards.Controls.Add(cardClient,2,0);
        layout.Controls.Add(cards);

        pipelineDetail.Dock=DockStyle.Fill; pipelineDetail.ForeColor=Muted; pipelineDetail.TextAlign=ContentAlignment.MiddleLeft; layout.Controls.Add(pipelineDetail);
        Tip(pipelineDetail,"Configured sample rate (applied NI-RFSA IQ rate) plus RF center, reference, preamp, and bandwidth. Live Gbps is under each Msps tile (RateHz x 32e-9).");
        BufferRow(layout,"DMA FIFO",fifoLabel,fifo,"FPGA host DMA FIFO occupancy. High values mean the host is not draining the FIFO fast enough.");
        BufferRow(layout,"Shared Memory",ringLabel,ring,"Named shared-memory ring occupancy and queued time at the applied sample rate.");
        diagnostics.Dock=DockStyle.Fill; diagnostics.ForeColor=Muted; diagnostics.Padding=new Padding(4,6,0,0); layout.Controls.Add(diagnostics);
        Tip(diagnostics,
            "Dropped: samples lost while an active consumer lagged behind the producer.\r\n"+
            "FIFO overflows: FPGA DMA FIFO overflow events (hardware overrun).\r\n"+
            "Recoveries: automatic hardware reopen attempts after a read failure.\r\n"+
            "Epoch: configuration generation; increments on each successful retune.\r\n"+
            "Last configure: time of the most recent successful RF configuration.\r\n"+
            "Producer heartbeat: age of the last DMA write into shared memory.\r\n"+
            "Client heartbeat: age of the last Soapy/GQRX consumer heartbeat (- if none).\r\n"+
            "Idle overwrites: samples discarded while no consumer is attached (intentional).");
        trend.Dock=DockStyle.Fill; trend.BackColor=PanelColor; layout.Controls.Add(trend);
        Tip(trend,"Rolling 60-second plot of DMA (green) and delivered (blue) rates in MS/s.");
    }
    private Control Card(string name,Label value,Label gbps,string hint,string tip)
    {
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=4,BackColor=PanelColor,Margin=new Padding(4),Padding=new Padding(12)};
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<4;i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading=Label(name,11,Muted); var note=Label(hint,8,Muted);
        value.Text="- MS/s"; value.Font=new Font("Segoe UI",18,FontStyle.Bold); value.ForeColor=Accent;
        gbps.Text="- Gbps"; gbps.Font=new Font("Segoe UI",10f); gbps.ForeColor=Muted;
        var rows=new[]{heading,value,gbps,note};
        for(int i=0;i<rows.Length;i++) {
            rows[i].AutoSize=true; rows[i].AutoEllipsis=false; rows[i].Dock=DockStyle.Fill;
            rows[i].Margin=new Padding(0,i==3?12:0,0,4);
            panel.Controls.Add(rows[i],0,i);
        }
        Tip(panel,tip); Tip(value,tip); Tip(gbps,tip); Tip(heading,tip);
        return panel;
    }
    private void BufferRow(TableLayoutPanel parent,string title,Label caption,MeterBar bar,string tip)
    {
        var panel=new Panel{Dock=DockStyle.Fill,Padding=new Padding(4,2,4,6)};
        caption.Text=title; caption.AutoEllipsis=false; caption.Dock=DockStyle.Top; caption.Height=24; caption.ForeColor=Muted; caption.TextAlign=ContentAlignment.MiddleLeft;
        bar.Dock=DockStyle.Bottom; bar.Height=12;
        panel.Controls.Add(caption); panel.Controls.Add(bar); parent.Controls.Add(panel);
        Tip(panel,tip); Tip(caption,tip); Tip(bar,tip);
    }
    private void BuildConnection(Control parent)
    {
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,ColumnCount=2,Padding=new Padding(4)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,230));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        parent.Controls.Add(layout);

        void Row(string name,Control control,int height=44,string? tip=null)
        {
            int row=layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            var label=Label(name,10,Muted); label.AutoEllipsis=false; label.Dock=DockStyle.Fill; label.TextAlign=ContentAlignment.MiddleLeft;
            if(control is Button)
            {
                control.Dock=DockStyle.Left; control.Margin=new Padding(4,4,8,4);
                control.MinimumSize=new Size(160,32);
            }
            else
            {
                control.Dock=DockStyle.Fill; control.Margin=new Padding(4,6,8,6);
            }
            if(control is TextBox tb){ tb.MinimumSize=new Size(200,28); }
            if(control is NumericUpDown nud){ nud.MinimumSize=new Size(160,28); }
            layout.Controls.Add(label,0,row); layout.Controls.Add(control,1,row);
            if(tip!=null){ Tip(label,tip); Tip(control,tip); }
        }

        void StyleNumeric(NumericUpDown nud)
        {
            nud.BackColor=PanelColor; nud.ForeColor=Color.WhiteSmoke; nud.BorderStyle=BorderStyle.FixedSingle;
        }
        StyleNumeric(sampleRate); StyleNumeric(frequency); StyleNumeric(reference); StyleNumeric(ringMiB);

        arguments.Text=AppFiles.DeviceArguments(options); arguments.ReadOnly=true; arguments.BackColor=PanelColor; arguments.ForeColor=Color.WhiteSmoke;
        Row("GQRX device string",arguments,44,"Paste into GQRX Device. VST Hub uses driver=vst and Local\\vst_live_v2 shared memory.");

        sampleRate.Minimum=1; sampleRate.Maximum=120; sampleRate.DecimalPlaces=3; sampleRate.Increment=1;
        sampleRate.Value=ClampDecimal((decimal)(options.Rx.RateHz/1e6),sampleRate.Minimum,sampleRate.Maximum);
        Row("Sample rate / MS/s",sampleRate,44,"NI Streaming for VST / ContinuousDma IQ rate. Valid range 1-120 MS/s. Driver may coerce; Hub shows the read-back rate. Match GQRX input rate after Apply.");

        frequency.Minimum=65; frequency.Maximum=6000; frequency.DecimalPlaces=6; frequency.Increment=1; frequency.Value=ClampDecimal((decimal)(options.Rx.CenterHz/1e6),frequency.Minimum,frequency.Maximum);
        reference.Minimum=-50; reference.Maximum=30; reference.DecimalPlaces=1; reference.Value=ClampDecimal((decimal)options.Rx.ReferenceDbm,reference.Minimum,reference.Maximum);
        Row("Center / MHz",frequency,44,"RF center frequency (65 MHz - 6 GHz). Applied to the live NI-RFSA session.");
        Row("Reference / dBm",reference,44,"NI-RFSA reference level in dBm (-50 ... +30). Not a linear software gain.");

        var preamp=Label("Auto (driver selects On/Off)",10,Color.WhiteSmoke); preamp.TextAlign=ContentAlignment.MiddleLeft;
        Row("Preamplifier",preamp,40,"5644R exposes Auto only. Actual On/Off is read back on Overview.");

        ringMiB.Minimum=64; ringMiB.Maximum=1024; ringMiB.Increment=64; ringMiB.DecimalPlaces=0;
        ringMiB.Value=ClampDecimal(options.RingMiB,ringMiB.Minimum,ringMiB.Maximum);
        Row("Shared memory / MiB",ringMiB,44,"Named IQ ring capacity (64-1024 MiB, steps of 64). Changing size recreates the ring; Stop RX first, then Apply. Soapy consumers must reconnect.");

        Button(apply,"Apply RX Settings",ApplyRxAsync);
        Row("",apply,52,"Apply center, sample rate, and reference to the running session. Shared-memory size applies when RX is stopped.");

        gqrxPath.Text=options.GqrxPath; gqrxPath.BackColor=PanelColor; gqrxPath.ForeColor=Color.WhiteSmoke;
        Row("GQRX executable",gqrxPath,44,"Path to radioconda Library\\bin\\gqrx.exe (x64 Soapy 0.8).");
        Button(applyPath,"Save Path && Install Plugin",()=>
        {
            try {var updated=options with {GqrxPath=gqrxPath.Text.Trim()}; files.InstallPlugin(updated); files.Save(updated); options=updated; arguments.Text=AppFiles.DeviceArguments(options); banner.Text="GQRX path saved; Soapy plugin checked.";}
            catch(Exception ex) {ShowError(ex.Message);} return Task.CompletedTask;
        });
        Row("",applyPath,52,"Save the GQRX path and install/verify the embedded Soapy module.");

        var note=Label("One RX consumer at a time: GQRX or GNU Radio. TX may run independently from a file or GNU Radio / SoapySDR.",9,Muted);
        note.AutoEllipsis=false; note.AutoSize=false; note.TextAlign=ContentAlignment.MiddleLeft;
        Row("Notes",note,64,"Hover Overview metrics for detailed English tooltips. Verbose help is kept out of the main layout.");
    }
    private static decimal ClampDecimal(decimal value,decimal min,decimal max) => Math.Min(max,Math.Max(min,value));
    private async Task ApplyRxAsync()
    {
        int requestedRing=(int)ringMiB.Value;
        if(requestedRing%64!=0) {ShowError("Shared memory size must be a multiple of 64 MiB."); return;}
        var requested=options.Rx with {
            CenterHz=(double)frequency.Value*1e6,
            RateHz=(double)sampleRate.Value*1e6,
            ReferenceDbm=(double)reference.Value
        };
        try {requested.Validate();} catch(ArgumentException ex) {ShowError(ex.Message); return;}

        bool ringChanged=requestedRing!=options.RingMiB;
        if(ringChanged)
        {
            if(engine.Snapshot.Tx.Status is "CONFIGURING" or "PREFILLING" or "STREAMING" or "STOPPING")
            {ShowError("Stop TX before resizing the shared device buffers. RX start/stop remains independent."); return;}
            if(engine.Snapshot.Status==EngineState.RUNNING || engine.Snapshot.Status==EngineState.TUNING || engine.Snapshot.Status==EngineState.RECOVERING)
            {ShowError("Stop RX before changing shared memory size."); return;}
            options=options with {Rx=requested,RingMiB=requestedRing};
            options.Validate(); files.Save(options);
            RecreateEngine();
            banner.Text=$"Shared memory set to {requestedRing} MiB. Engine restarted with new ring.";
            return;
        }

        if(engine.Snapshot.Status!=EngineState.RUNNING)
        {
            if(!await Send($"CONFIG2 center_hz={JsonDefaults.Number(requested.CenterHz)},rate_hz={JsonDefaults.Number(requested.RateHz)},reference_level_dbm={JsonDefaults.Number(requested.ReferenceDbm)},preamp_mode=auto"))return;
            options=options with {Rx=requested}; files.Save(options);
            try {files.WriteGqrxConfig(options);} catch { }
            banner.Text=$"Saved {requested.RateHz/1e6:F3} MS/s for next Start. Match GQRX input rate after RX is running.";
            return;
        }
        if(await Send($"CONFIG2 center_hz={JsonDefaults.Number(requested.CenterHz)},rate_hz={JsonDefaults.Number(requested.RateHz)},reference_level_dbm={JsonDefaults.Number(requested.ReferenceDbm)},preamp_mode=auto"))
        {
            options=options with {Rx=requested}; files.Save(options);
            try {files.WriteGqrxConfig(options);} catch { }
            banner.Text=$"Applied {requested.RateHz/1e6:F3} MS/s · {HubOptions.RawIqGbps(requested.RateHz):F3} Gbps raw IQ. Match GQRX input rate.";
        }
    }
    private void RecreateEngine()
    {
        timer.Stop();
        try {engine.Dispose();} catch { }
        engine=new HubEngine(options,files.LogDirectory);
        engine.Start();
        timer.Start();
    }
    private void Button(Button button,string text,Func<Task> action)
    {
        button.Text=text; button.AutoSize=true; button.AutoSizeMode=AutoSizeMode.GrowAndShrink; button.Height=36; button.Padding=new Padding(12,4,12,4);
        button.TextAlign=ContentAlignment.MiddleCenter;
        button.BackColor=PanelColor; button.ForeColor=Color.WhiteSmoke; button.FlatStyle=FlatStyle.Flat;
        button.FlatAppearance.BorderColor=Color.FromArgb(57,79,98); button.Margin=new Padding(0,0,8,0);
        button.Click+=async(_,_)=>{button.Enabled=false; try {notice=""; await action();} catch(Exception ex) {ShowError(ex.Message); engine.Log.Event("WARN","ui.action_failed",ex.Message);} finally {if(!button.IsDisposed) button.Enabled=true;}};
    }
    private async Task<bool> Send(string command)
    {
        var reply=await engine.Command(command);
        if(!reply.StartsWith("OK ")) {ShowError(reply); return false;}
        banner.Text="Settings applied."; return true;
    }
    private void ShowError(string error) {notice=error; noticeUntil=DateTime.UtcNow.AddSeconds(15); banner.ForeColor=Color.Salmon; banner.Text=error;}
    private void RefreshSnapshot()
    {
        if(engine.ShutdownRequested) {Close(); return;}
        var s=engine.Snapshot;
        RefreshTx(s.Tx);
        state.Text=s.Status switch {EngineState.RUNNING=>s.ClientState,EngineState.INITIALIZING=>"Initializing…",EngineState.TUNING=>"Configuring…",EngineState.RECOVERING=>"Recovering…",EngineState.STOPPED=>"RX stopped",_=>"Attention required"};
        state.ForeColor=s.Status==EngineState.ERROR?Color.Salmon:Accent;
        dma.Text=$"{s.DmaMsps:F2} MS/s"; shm.Text=$"{s.ShmMsps:F2} MS/s"; client.Text=$"{s.DeliveredMsps:F2} MS/s";
        dmaGbps.Text=$"{HubOptions.RawIqGbps(s.DmaMsps*1e6):F3} Gbps";
        shmGbps.Text=$"{HubOptions.RawIqGbps(s.ShmMsps*1e6):F3} Gbps";
        clientGbps.Text=$"{HubOptions.RawIqGbps(s.DeliveredMsps*1e6):F3} Gbps";
        double rate=s.AppliedRateHz>0?s.AppliedRateHz:options.Rx.RateHz;
        fifo.Percent=s.FifoPercent; ring.Percent=s.Ring.BufferPercent;
        fifoLabel.Text=$"DMA FIFO     {s.FifoRemaining*4d/1048576:F1} / {s.FifoCapacity*4d/1048576:F0} MiB     {s.FifoPercent:F1}%";
        double queuedMs=rate>0?s.Ring.Occupancy/(rate/1e3):0;
        ringLabel.Text=$"Shared Memory     {s.Ring.OccupancyMiB:F1} / {s.Ring.CapacitySamples*4d/1048576:F0} MiB     {s.Ring.BufferPercent:F1}%     Queued {queuedMs:F1} ms";
        pipelineDetail.Text=$"Sample rate {rate/1e6:F3} MS/s   ·   RF {s.AppliedCenterHz/1e6:F3} MHz   ·   Ref {s.AppliedRefDbm:F1} dBm   ·   Preamp {s.PreampActual}   ·   BW {s.EffectiveBandwidthHz/1e6:F1} MHz";
        diagnostics.Text=$"Dropped {s.Ring.Drops:N0}   ·   FIFO overflows {s.OverflowCount}   ·   Recoveries {s.Recoveries}   ·   Epoch {s.Ring.Epoch}   ·   Last configure {s.LastTuneS*1000:F0} ms\r\n"+
            $"Producer heartbeat {s.Ring.HeartbeatAgeMs} ms   ·   Client heartbeat {(s.Ring.ConsumerHeartbeatAgeMs==ulong.MaxValue?"-":s.Ring.ConsumerHeartbeatAgeMs.ToString())} ms   ·   Idle overwrites {s.Ring.IdleDiscardSamples:N0}";
        var message=s.Error??(!string.IsNullOrEmpty(s.LogError)?"Log write failed: "+s.LogError:s.Status==EngineState.RUNNING?
            (s.Ring.ConsumerActive?"RX delivering to client. TX and RX have independent controls and monitoring.":"Bridge ready. Choose GQRX or GNU Radio; match the client sample rate."):
            "Start RX, then connect one GQRX or GNU Radio consumer.");
        bool hasNotice=notice.Length>0 && DateTime.UtcNow<noticeUntil;
        banner.Text=hasNotice?notice:message; banner.ForeColor=hasNotice||s.Error!=null||s.LogError.Length>0?Color.Salmon:Muted;
        details.Text=$"Uptime {TimeSpan.FromSeconds(s.ElapsedS):hh\\:mm\\:ss}   ·   Hub CPU {s.CpuCores:F2} cores   ·   Log drops {s.LogDropped}   ·   {files.LogDirectory}";
        start.Enabled=s.Status is EngineState.STOPPED or EngineState.ERROR;
        halt.Enabled=s.Status is EngineState.RUNNING; apply.Enabled=true;
        gqrx.Enabled=true; RefreshBridge(s);
        ringMiB.Enabled=s.Status is EngineState.STOPPED or EngineState.ERROR or EngineState.INITIALIZING;
        // Configuration controls are an editable draft. Applied values are shown in monitors.
        trend.Add(s.DmaMsps,s.DeliveredMsps);
        string history=string.Join(Environment.NewLine,engine.Log.Recent.Reverse());
        if(history!=lastEvents) {events.Text=history; lastEvents=history;}
        if(Interlocked.CompareExchange(ref writingStatus,1,0)==0) _=Task.Run(()=>
        {
            try {AppFiles.Atomic(files.StatusPath,JsonDefaults.Serialize(s));}
            catch(IOException) { }
            finally {Interlocked.Exchange(ref writingStatus,0);}
        });
    }
    private int writingStatus;
}

internal sealed class HubButton : Button
{
    // WinForms paints disabled flat-button text black on a dark background.
    protected override void OnPaint(PaintEventArgs e)
    {
        if(Enabled) {base.OnPaint(e); return;}
        e.Graphics.Clear(BackColor);
        using var border=new Pen(Color.FromArgb(57,79,98));
        e.Graphics.DrawRectangle(border,0,0,Width-1,Height-1);
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Color.FromArgb(145,161,181),
            TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
    }
}

internal sealed class MonitorTabs : TabControl
{
    // A scrollable configuration page must not enlarge the root percent row.
    public override Size GetPreferredSize(Size proposedSize) => new(320,180);
}

internal sealed class MeterBar : Control
{
    private double percent;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Percent
    {
        get => percent;
        set { percent=Math.Clamp(value,0,100); Invalidate(); }
    }
    public MeterBar()
    {
        DoubleBuffered=true;
        Height=12;
        BackColor=Color.FromArgb(30,42,58);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;
        using var track=new SolidBrush(Color.FromArgb(30,42,58));
        using var fill=new SolidBrush(Color.FromArgb(53,207,168));
        using var edge=new Pen(Color.FromArgb(57,79,98));
        var rect=ClientRectangle;
        rect.Width=Math.Max(0,rect.Width-1); rect.Height=Math.Max(0,rect.Height-1);
        g.FillRectangle(track,rect);
        int w=(int)Math.Round(rect.Width*percent/100.0);
        if(w>0) g.FillRectangle(fill,new Rectangle(rect.X,rect.Y,w,rect.Height));
        g.DrawRectangle(edge,rect);
    }
}

internal sealed class TrendPlot : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Caption {get;set;} = "Last 60 s  ·  Green DMA / Blue delivered  [MS/s]";
    private readonly Queue<(double Dma,double Client)> history=new();
    public TrendPlot() {DoubleBuffered=true;}
    public void Add(double dma,double client) {history.Enqueue((dma,client)); while(history.Count>120) history.Dequeue(); Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var font=new Font("Segoe UI",9); using var gray=new SolidBrush(Color.FromArgb(153,174,192));
        g.DrawString(Caption,font,gray,12,8);
        var rect=new RectangleF(44,36,Math.Max(1,Width-60),Math.Max(1,Height-56));
        using var grid=new Pen(Color.FromArgb(40,57,73));
        int divisions=rect.Height>=3*font.GetHeight(g)+12?3:1;
        for(int i=0;i<=divisions;i++) {float y=rect.Bottom-i*rect.Height/divisions; g.DrawLine(grid,rect.Left,y,rect.Right,y); g.DrawString((i*150/divisions).ToString(),font,gray,4,y-9);}
        var values=history.ToArray(); if(values.Length<2) return;
        PointF[] Points(bool dma)=>values.Select((v,i)=>new PointF(rect.Left+i*rect.Width/119,rect.Bottom-(float)Math.Clamp((dma?v.Dma:v.Client)/150,0,1)*rect.Height)).ToArray();
        using var p1=new Pen(Color.FromArgb(53,207,168),2); using var p2=new Pen(Color.FromArgb(92,157,255),2);
        g.DrawLines(p1,Points(true)); g.DrawLines(p2,Points(false));
    }
}
