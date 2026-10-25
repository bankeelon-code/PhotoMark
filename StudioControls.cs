using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PhotoMark;

internal static class StudioTheme
{
    public static readonly Color Surface = Color.FromArgb(25,30,37);
    public static readonly Color Raised = Color.FromArgb(35,41,49);
    public static readonly Color Ink = Color.FromArgb(235,239,242);
    public static readonly Color Muted = Color.FromArgb(151,164,174);
    public static readonly Color Accent = Color.FromArgb(153,206,194);
    public static readonly Color Border = Color.FromArgb(49,58,68);
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right-d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90); path.AddArc(r.X, r.Bottom-d, d, d, 90, 90); path.CloseFigure(); return path;
    }
}

internal sealed class StudioButton : Button
{
    bool hover;
    public StudioButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Color.White); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = StudioTheme.Round(new RectangleF(1,1,Width-3,Height-3), 8 * DeviceDpi / 96f);
        Color fill = !Enabled ? Color.FromArgb(31,36,43) : hover ? ControlPaint.Light(BackColor, .10f) : BackColor;
        using var brush = new SolidBrush(fill); e.Graphics.FillPath(brush,path);
        if (FlatAppearance.BorderSize > 0) { using var pen = new Pen(FlatAppearance.BorderColor); e.Graphics.DrawPath(pen,path); }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(164,173,181), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle,-5,-5));
    }
}

// A keyboard-accessible continuous slider with a permanently visible numeric value.
internal sealed class SliderField : Control
{
    int minimum, maximum = 100, value;
    string caption = "", unit = "%";
    public event EventHandler? ValueChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum { get => minimum; set { minimum = value; if (maximum < minimum) maximum = minimum; Value = this.value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum { get => maximum; set { maximum = Math.Max(value, minimum); Value = this.value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get => value; set { int next = Math.Clamp(value, minimum, maximum); if (this.value == next) return; this.value = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); AccessibilityNotifyClients(AccessibleEvents.ValueChange,-1); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption { get => caption; set { caption = value; AccessibleName = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Unit { get => unit; set { unit = value; Invalidate(); } }
    public SliderField()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true; Cursor = Cursors.Hand; Height = 64; AccessibleRole = AccessibleRole.Slider;
    }
    float D => DeviceDpi / 96f;
    float StartX => 9 * D;
    float EndX => Math.Max(StartX + 1, Width - 9 * D);
    void SetFromX(int x) => Value = minimum + (int)Math.Round(Math.Clamp((x-StartX)/(EndX-StartX),0,1)*(maximum-minimum));
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Capture = true; SetFromX(e.X); } }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) SetFromX(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        int step = e.Shift ? 5 : 1;
        if (e.KeyCode is Keys.Left or Keys.Down) Value -= step;
        else if (e.KeyCode is Keys.Right or Keys.Up) Value += step;
        else if (e.KeyCode == Keys.Home) Value = Minimum;
        else if (e.KeyCode == Keys.End) Value = Maximum;
        else { base.OnKeyDown(e); return; }
        e.Handled = true;
    }
    protected override void OnMouseWheel(MouseEventArgs e) { if (Focused) Value += Math.Sign(e.Delta); base.OnMouseWheel(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
        Color ink = Enabled ? StudioTheme.Ink : StudioTheme.Muted;
        TextRenderer.DrawText(g,Caption,Font,new Rectangle(0,0,Width-(int)(66*D),(int)(24*D)),ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
        var badge = new RectangleF(Width-62*D,2*D,60*D,22*D);
        using var bp=StudioTheme.Round(badge,5*D); using var bb=new SolidBrush(Color.FromArgb(35,45,49)); g.FillPath(bb,bp);
        TextRenderer.DrawText(g,Value + Unit,Font,Rectangle.Round(badge),Enabled ? StudioTheme.Accent : StudioTheme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        float y=43*D, x=StartX+(EndX-StartX)*(Value-Minimum)/Math.Max(1f,Maximum-Minimum);
        using var track=new Pen(Color.FromArgb(53,63,73),4*D) { StartCap=LineCap.Round, EndCap=LineCap.Round }; g.DrawLine(track,StartX,y,EndX,y);
        using var fill=new Pen(Enabled ? StudioTheme.Accent : Color.Silver,4*D) { StartCap=LineCap.Round, EndCap=LineCap.Round }; g.DrawLine(fill,StartX,y,x,y);
        using var halo=new SolidBrush(Color.FromArgb(Focused ? 38 : 20,StudioTheme.Accent)); g.FillEllipse(halo,x-10*D,y-10*D,20*D,20*D);
        using var white=new SolidBrush(Color.White); g.FillEllipse(white,x-6*D,y-6*D,12*D,12*D);
        using var edge=new Pen(Enabled ? StudioTheme.Accent : Color.Silver,2*D); g.DrawEllipse(edge,x-6*D,y-6*D,12*D,12*D);
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessibility(this);
    sealed class SliderAccessibility(SliderField owner) : ControlAccessibleObject(owner)
    {
        public override string? Value { get => owner.Value.ToString(); set { if (int.TryParse(value,out int v)) owner.Value=v; } }
        public override AccessibleRole Role => AccessibleRole.Slider;
    }
    internal void TestKey(Keys key) => OnKeyDown(new KeyEventArgs(key));
    internal void TestPointer(float fraction) { OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,(int)(StartX+(EndX-StartX)*fraction),39,0)); OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,0,0,0)); }
}


