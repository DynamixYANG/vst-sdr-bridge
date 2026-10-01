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
    private readonly NumberChoice frequency=new(), reference=new(), sampleRate=new(), ringMiB=new();
    private readonly Label txBadge=new();
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
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=4};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(root);
        var heading=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(0,0,0,14)};
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title=Label("VST Bridge",22,Color.White);title.AutoSize=true;title.Margin=new Padding(0,0,18,0);heading.Controls.Add(title,0,0);
        var statuses=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Anchor=AnchorStyles.Right};
        foreach(var badge in new[]{state,txBadge}){badge.AutoSize=true;badge.Font=new Font(Font,FontStyle.Bold);badge.BackColor=PanelColor;badge.ForeColor=Accent;badge.Padding=new Padding(12,9,12,9);badge.Margin=new Padding(8,0,0,0);statuses.Controls.Add(badge);}
        state.Text="RX · Initializing";txBadge.Text="TX · Initializing";heading.Controls.Add(statuses,1,0);root.Controls.Add(heading,0,0);
        Tip(state,"Receive direction and client status. Hardware initialization steps appear in Logs & Debug.");Tip(txBadge,"Transmit direction. No client data is an idle state; hardware faults are shown separately.");
        banner.Dock=DockStyle.Fill;banner.AutoSize=true;banner.Padding=new Padding(10,6,10,6);banner.BackColor=PanelColor;banner.Visible=false;root.Controls.Add(banner,0,1);
        var tabs=new MonitorTabs{Dock=DockStyle.Fill,Padding=new Point(18,8)};root.Controls.Add(tabs,0,2);
        var bridge=Page(tabs,"Bridge");BuildBridge(bridge);var overview=Page(tabs,"RX Monitor");var txPage=Page(tabs,"TX Monitor");
        var connection=Page(tabs,"RX Configuration");var txSetup=Page(tabs,"TX Configuration");var logPage=Page(tabs,"Logs & Debug");
        BuildOverview(overview);BuildConnection(connection);BuildTxMonitor(txPage);BuildTxConfiguration(txSetup);
        var logLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};logLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));logPage.Controls.Add(logLayout);
        events.Multiline=true;events.ReadOnly=true;events.ScrollBars=ScrollBars.Both;events.WordWrap=false;events.Dock=DockStyle.Fill;
        events.BackColor=PanelColor;events.ForeColor=Muted;events.Font=new Font("Consolas",10);logLayout.Controls.Add(events);
        var logs=new HubButton();Button(logs,"Open Logs",()=>{Process.Start(new ProcessStartInfo(files.LogDirectory){UseShellExecute=true});return Task.CompletedTask;});
        var export=new HubButton();Button(export,"Export Snapshot",()=>{using var dialog=new SaveFileDialog{Filter="JSON snapshot|*.json",FileName="vst-bridge-diagnostics.json"};if(dialog.ShowDialog(this)==DialogResult.OK)AppFiles.Atomic(dialog.FileName,JsonDefaults.Serialize(engine.Snapshot));return Task.CompletedTask;});
        logLayout.Controls.Add(ActionRow(logs,export));Tip(events,"Initialization stages, application launches, configuration changes, client lifecycle and errors. Full UTC JSONL files are retained on disk.");
        BuildFooter(root);
        Tip(details,files.LogDirectory);
        timer.Tick+=(_,_)=>RefreshSnapshot();
        Shown+=(_,_)=>
        {
            engine.Log.Event("INFO","bridge.starting","Checking application settings and embedded Soapy plugin");
            try {var message=files.InstallPlugin(this.options);engine.Log.Event("INFO","plugin.ready",message);} catch(Exception ex) {ShowError(ex.Message);engine.Log.Event("ERROR","plugin.install_failed",ex.Message);}
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
                                choices=Descendants(this).OfType<NumberChoice>().Where(c=>c.Visible).Select(c=>new {c.Text,c.Enabled,focused=c.ContainsFocus,c.SelectionLength}).ToArray(),
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
            Text="VST Bridge · Releasing device…";
            await Task.Run(()=>{ try {engine.Dispose();} catch { } }); canClose=true; Close();
        };
    }
    private void BuildFooter(TableLayoutPanel root)
    {
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,RowCount=2,Padding=new Padding(0,10,0,0)};
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Button(start,"Start RX",async()=>{if(!engine.IsAlive)RecreateEngine();await Send("START");});
        Button(halt,"Stop RX",async()=>await Send("STOP"));
        Button(txStart,"Start TX",async()=>{var config=TxDraft();config.Validate();if(await Send("TXSTART "+JsonDefaults.Serialize(config)))SaveTxDraft(config);});
        Button(txStop,"Stop TX",async()=>await Send("TXSTOP"));
        var controls=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,RowCount=1,BackColor=PanelColor,Padding=new Padding(12)};
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        controls.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controls.Controls.Add(ActionRow(start,halt),0,0);controls.Controls.Add(ActionRow(txStart,txStop),1,0);
        Tip(start,"Start RX independently of TX, using the applied RX configuration.");Tip(halt,"Stop RX independently of TX.");
        Tip(txStart,"Start TDMS/CS16 playback using the TX Configuration values and Enable RF selection. GNU Radio live TX starts from its Soapy sink.");
        Tip(txStop,"Stop TX and disable RF independently of RX.");
        footer.Controls.Add(controls,0,0);
        details.Dock=DockStyle.Fill;details.AutoSize=true;details.ForeColor=Muted;details.Font=new Font(Font.FontFamily,9);details.Padding=new Padding(0,6,0,0);
        footer.Controls.Add(details,0,1);root.Controls.Add(footer,0,3);
    }
    private static IEnumerable<Control> Descendants(Control parent)
    {foreach(Control child in parent.Controls){yield return child;foreach(var descendant in Descendants(child))yield return descendant;}}
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
        for(int i=0;i<4;i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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

        pipelineDetail.AutoSize=true; pipelineDetail.Padding=new Padding(4,8,4,8); pipelineDetail.Dock=DockStyle.Fill; pipelineDetail.ForeColor=Muted; pipelineDetail.TextAlign=ContentAlignment.MiddleLeft; layout.Controls.Add(pipelineDetail);
        Tip(pipelineDetail,"Configured sample rate (applied NI-RFSA IQ rate) plus RF center, reference, preamp, and bandwidth. Live Gbps is under each Msps tile (RateHz x 32e-9).");
        BufferRow(layout,"DMA FIFO",fifoLabel,fifo,"FPGA host DMA FIFO occupancy. High values mean the host is not draining the FIFO fast enough.");
        BufferRow(layout,"Shared Memory",ringLabel,ring,"Named shared-memory ring occupancy and queued time at the applied sample rate.");
        diagnostics.AutoSize=true; diagnostics.Dock=DockStyle.Fill; diagnostics.ForeColor=Muted; diagnostics.Padding=new Padding(4,6,0,0); layout.Controls.Add(diagnostics);
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
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=3,BackColor=PanelColor,Margin=new Padding(4),Padding=new Padding(12)};
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<3;i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading=Label(name,11,Muted); var note=Label(hint,8,Muted);
        value.Text="- MS/s"; value.Font=new Font("Segoe UI",18,FontStyle.Bold); value.ForeColor=Accent;
        gbps.Text="- Gbps"; gbps.Font=new Font("Segoe UI",10f); gbps.ForeColor=Muted;
        var rows=new[]{heading,value,gbps};
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
        var panel=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=2,Padding=new Padding(4,6,4,10)};
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,14));
        caption.Text=title;caption.AutoSize=true;caption.AutoEllipsis=false;caption.Dock=DockStyle.Fill;caption.ForeColor=Muted;caption.Margin=new Padding(0,0,0,7);
        bar.Dock=DockStyle.Fill;bar.Margin=new Padding(0);panel.Controls.Add(caption,0,0);panel.Controls.Add(bar,0,1);parent.Controls.Add(panel);
        Tip(panel,tip); Tip(caption,tip); Tip(bar,tip);
    }
    private async Task ApplyRxAsync()
    {
        int requestedRing=IntegerChoice(ringMiB);
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
        state.Text="RX · "+(s.Status switch {EngineState.RUNNING=>"Running",EngineState.INITIALIZING=>"Initializing",EngineState.TUNING=>"Configuring",EngineState.RECOVERING=>"Recovering",EngineState.STOPPED=>"Stopped",_=>"Error"});
        state.ForeColor=s.Status==EngineState.ERROR?Color.Salmon:s.Status==EngineState.STOPPED?Muted:Accent;
        txBadge.Text="TX · "+(s.Status==EngineState.INITIALIZING&&!s.Capabilities.Tx?"Initializing":TxStatusText(s.Tx.Status));
        txBadge.ForeColor=s.Tx.Status=="FAULT"?Color.Salmon:s.Tx.Status=="WAITING_CLIENT"?Color.Goldenrod:s.Tx.Status=="STREAMING"?Accent:Muted;
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
        bool hasNotice=notice.Length>0 && DateTime.UtcNow<noticeUntil;
        var message=hasNotice?notice:s.Error??(s.LogError.Length>0?"Log write failed: "+s.LogError:"");
        banner.Text=message;banner.Visible=message.Length>0;banner.ForeColor=Color.Salmon;
        details.Text=$"v{Application.ProductVersion.Split('+')[0]} · Uptime {TimeSpan.FromSeconds(s.ElapsedS):hh\\:mm\\:ss}   ·   CPU {s.CpuCores:F2} cores   ·   Log drops {s.LogDropped}";
        start.Enabled=s.Status is EngineState.STOPPED or EngineState.ERROR;
        halt.Enabled=s.Status is EngineState.RUNNING; apply.Enabled=true;
        gqrx.Enabled=true; RefreshBridge(s);
        ringMiB.Enabled=s.Status is EngineState.STOPPED or EngineState.ERROR or EngineState.INITIALIZING;
        // Configuration controls are an editable draft. Applied values are shown in monitors.
        trend.Add(s.DmaMsps,s.DeliveredMsps);
        string history=string.Join(Environment.NewLine,engine.Log.Recent);
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

internal sealed class HubCheckBox : CheckBox
{
    protected override void OnPaint(PaintEventArgs e)
    {
        if(Enabled){base.OnPaint(e);return;}
        e.Graphics.Clear(BackColor);
        int side=Math.Max(12,(int)(13*DeviceDpi/96f));int y=(Height-side)/2;
        using var border=new Pen(Color.FromArgb(145,161,181));e.Graphics.DrawRectangle(border,0,y,side,side);
        if(Checked)e.Graphics.DrawLines(border,[new Point(3,y+side/2),new Point(side/2,y+side-3),new Point(side-2,y+2)]);
        TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(side+6,0,Width-side-6,Height),Color.FromArgb(145,161,181),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
    }
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
        float top=font.GetHeight(g)+26;
        var rect=new RectangleF(48,top,Math.Max(1,Width-64),Math.Max(1,Height-top-20));
        using var grid=new Pen(Color.FromArgb(40,57,73));
        int divisions=rect.Height>=3*font.GetHeight(g)+12?3:1;
        for(int i=0;i<=divisions;i++) {float y=rect.Bottom-i*rect.Height/divisions; g.DrawLine(grid,rect.Left,y,rect.Right,y); g.DrawString((i*150/divisions).ToString(),font,gray,4,y-9);}
        var values=history.ToArray(); if(values.Length<2) return;
        PointF[] Points(bool dma)=>values.Select((v,i)=>new PointF(rect.Left+i*rect.Width/119,rect.Bottom-(float)Math.Clamp((dma?v.Dma:v.Client)/150,0,1)*rect.Height)).ToArray();
        using var p1=new Pen(Color.FromArgb(53,207,168),2); using var p2=new Pen(Color.FromArgb(92,157,255),2);
        g.DrawLines(p1,Points(true)); g.DrawLines(p2,Points(false));
    }
}
