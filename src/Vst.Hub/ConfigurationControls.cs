using System.Globalization;
using System.Runtime.InteropServices;
using Vst.Core;
namespace Vst.Hub;

internal class DarkChoice : ComboBox
{
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct ComboInfo
    {public int Size;public NativeRect Item,Button;public int ButtonState;public IntPtr Combo,Edit,List;}
    [DllImport("user32.dll")] private static extern bool GetComboBoxInfo(IntPtr handle,ref ComboInfo info);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr handle,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr handle,int index,IntPtr value);
    [DllImport("user32.dll",EntryPoint="SendMessageW")] private static extern IntPtr SendMessage(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc,uint color);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(IntPtr dc,uint color);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    private IntPtr fieldBrush;
    private static uint ColorRef(Color color)=>(uint)(color.R|(color.G<<8)|(color.B<<16));
    public DarkChoice()
    {
        BackColor=Color.FromArgb(23,34,49);ForeColor=Color.WhiteSmoke;
        FlatStyle=FlatStyle.Flat;DrawMode=DrawMode.OwnerDrawFixed;
    }
    internal void ClearInactiveSelection()
    {
        if(IsHandleCreated&&DropDownStyle==ComboBoxStyle.DropDown&&!ContainsFocus&&!DroppedDown)
        {
            var info=new ComboInfo{Size=Marshal.SizeOf<ComboInfo>()};
            if(GetComboBoxInfo(Handle,ref info)&&info.Edit!=IntPtr.Zero)
            {
                var selection=SendMessage(info.Edit,0x00B0,IntPtr.Zero,IntPtr.Zero).ToInt64();
                if((selection&0xffff)!=Text.Length||((selection>>16)&0xffff)!=Text.Length)
                    SendMessage(info.Edit,0x00B1,new IntPtr(Text.Length),new IntPtr(Text.Length));
            }
        }
    }
    private void ClearSelectionAfterNativeUpdate()
    {if(IsHandleCreated&&!IsDisposed)BeginInvoke((Action)(()=>{if(!IsDisposed)ClearInactiveSelection();}));}
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var info=new ComboInfo{Size=Marshal.SizeOf<ComboInfo>()};
        if(DropDownStyle==ComboBoxStyle.DropDown&&GetComboBoxInfo(Handle,ref info)&&info.Edit!=IntPtr.Zero)
        {
            // Editable ComboBox children retain ES_NOHIDESEL by default.
            // Remove it so an intentional edit selection only appears while focused.
            var style=GetWindowLongPtr(info.Edit,-16).ToInt64();
            SetWindowLongPtr(info.Edit,-16,new IntPtr(style&~0x0100L));
        }
        ClearInactiveSelection();
        ClearSelectionAfterNativeUpdate();
    }
    protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);ClearSelectionAfterNativeUpdate();}
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);ClearSelectionAfterNativeUpdate();}
    protected override void OnLeave(EventArgs e)
    {base.OnLeave(e);if(DropDownStyle==ComboBoxStyle.DropDown){SelectionStart=Text.Length;SelectionLength=0;}}
    protected override void OnBackColorChanged(EventArgs e)
    {if(fieldBrush!=IntPtr.Zero){DeleteObject(fieldBrush);fieldBrush=IntPtr.Zero;}base.OnBackColorChanged(e);}
    protected override void OnHandleDestroyed(EventArgs e)
    {if(fieldBrush!=IntPtr.Zero){DeleteObject(fieldBrush);fieldBrush=IntPtr.Zero;}base.OnHandleDestroyed(e);}
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var selected=(e.State&DrawItemState.Selected)!=0;
        using var brush=new SolidBrush(selected?Color.FromArgb(42,65,83):BackColor);
        e.Graphics.FillRectangle(brush,e.Bounds);
        var text=e.Index>=0?GetItemText(Items[e.Index]):Text;
        TextRenderer.DrawText(e.Graphics,text,e.Font,e.Bounds,Color.WhiteSmoke,
            TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg is 0x000F or 0x0317 or 0x0318)ClearInactiveSelection();
        base.WndProc(ref m);
        if(m.Msg is 0x0133 or 0x0138)
        {
            // The native edit requests STATIC colors when its combo is disabled.
            // Keep the same dark field instead of the system white/grey brush.
            SetTextColor(m.WParam,ColorRef(ForeColor));SetBkColor(m.WParam,ColorRef(BackColor));
            if(fieldBrush==IntPtr.Zero)fieldBrush=CreateSolidBrush(ColorRef(BackColor));
            m.Result=fieldBrush;return;
        }
        bool print=m.Msg is 0x0317 or 0x0318;
        if(m.Msg!=0x000F&&!print)return;
        var info=new ComboInfo{Size=Marshal.SizeOf<ComboInfo>()};
        if(!GetComboBoxInfo(Handle,ref info))return;
        using var graphics=print?Graphics.FromHdc(m.WParam):Graphics.FromHwnd(Handle);
        if(DropDownStyle==ComboBoxStyle.DropDownList||!Enabled||(print&&!ContainsFocus))
        {
            var item=Rectangle.FromLTRB(info.Item.Left,info.Item.Top,info.Item.Right,info.Item.Bottom);
            using var field=new SolidBrush(BackColor);graphics.FillRectangle(field,item);
            TextRenderer.DrawText(graphics,Text,Font,item,ForeColor,
                TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        }
        var button=Rectangle.FromLTRB(info.Button.Left,info.Button.Top,info.Button.Right,info.Button.Bottom);
        using var background=new SolidBrush(BackColor);graphics.FillRectangle(background,button);
        using var arrow=new SolidBrush(Enabled?ForeColor:Color.FromArgb(153,174,192));
        float cx=button.Left+button.Width/2f,cy=button.Top+button.Height/2f,size=3f*DeviceDpi/96f;
        graphics.FillPolygon(arrow,new[]{new PointF(cx-size,cy-size/2),new PointF(cx+size,cy-size/2),new PointF(cx,cy+size/2)});
    }
}
internal sealed class NumberChoice : DarkChoice
{
    private decimal minimum,maximum;
    public void Configure(decimal min,decimal max,decimal value,params decimal[] recommendations)
    {
        minimum=min;maximum=max;DropDownStyle=ComboBoxStyle.DropDown;FlatStyle=FlatStyle.Flat;
        Items.Clear();foreach(var v in recommendations)Items.Add(v.ToString("0.######",CultureInfo.InvariantCulture));Value=value;
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal Value
    {
        get
        {
            if(!decimal.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||value<minimum||value>maximum)
                throw new ArgumentException($"Enter a number between {minimum} and {maximum}.");
            return value;
        }
        set=>Text=value.ToString("0.######",CultureInfo.InvariantCulture);
    }
}
internal sealed partial class MonitorForm
{
    private TableLayoutPanel ConfigurationForm(Control parent)
    {
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};parent.Controls.Add(scroll);
        var table=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,RowCount=0,Padding=new Padding(16)};
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,230));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        scroll.Controls.Add(table);return table;
    }
    private void ConfigurationRow(TableLayoutPanel table,string name,Control control,string tip)
    {
        int row=table.RowCount++;table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption=Label(name,10,Muted);caption.AutoSize=true;caption.Anchor=AnchorStyles.Left;caption.Margin=new Padding(0,8,12,12);
        control.Dock=DockStyle.Fill;control.Margin=new Padding(0,4,0,12);
        if(control is TextBox or ComboBox){control.BackColor=PanelColor;control.ForeColor=Color.WhiteSmoke;control.MinimumSize=new Size(180,30);}
        table.Controls.Add(caption,0,row);table.Controls.Add(control,1,row);Tip(caption,tip);Tip(control,tip);
    }
    private static int IntegerChoice(NumberChoice control)
    {
        decimal value=control.Value;
        if(value!=decimal.Truncate(value))throw new ArgumentException("Buffer sizes and block counts must be whole numbers.");
        return checked((int)value);
    }
    private FlowLayoutPanel ActionRow(params Control[] controls)
    {
        var row=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,WrapContents=true,Margin=new Padding(0),Padding=new Padding(0,6,0,4)};
        row.Controls.AddRange(controls);return row;
    }
    private void BuildConnection(Control parent)
    {
        var table=ConfigurationForm(parent);
        sampleRate.Configure(1,120,(decimal)(options.Rx.RateHz/1e6),1,5,10,20,30.72m,40,60,61.44m,80,100,120);
        frequency.Configure(65,6000,(decimal)(options.Rx.CenterHz/1e6),100,433,915,1000,2400,2450,2500,3500,5800);
        reference.Configure(-50,30,(decimal)options.Rx.ReferenceDbm,-50,-40,-30,-20,-10,0,10,20,30);
        ringMiB.Configure(64,1024,options.RingMiB,64,128,256,512,1024);
        ConfigurationRow(table,"Sample rate / MS/s",sampleRate,"Requested RX complex IQ rate, 1–120 MS/s. The monitor shows actual hardware readback.");
        ConfigurationRow(table,"Center / MHz",frequency,"RF input center frequency, 65 MHz–6 GHz.");
        ConfigurationRow(table,"Reference / dBm",reference,"RFSA reference level, -50 to +30 dBm. Lower values increase receive sensitivity.");
        var preamp=new DarkChoice{DropDownStyle=ComboBoxStyle.DropDownList};preamp.Items.Add("Auto");preamp.SelectedIndex=0;
        ConfigurationRow(table,"Preamplifier",preamp,"The driver selects the analog preamplifier. Actual state is shown in RX Monitor.");
        ConfigurationRow(table,"Shared memory / MiB",ringMiB,"64–1024 MiB, multiples of 64. Stop both directions before resizing; the client must reconnect.");
        Button(apply,"Apply RX",ApplyRxAsync);
        ConfigurationRow(table,"",ActionRow(apply),"Apply RX configuration. Start and Stop are always available in the window footer.");
    }
}
