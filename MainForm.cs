using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace PhotoMark;

internal sealed class MainForm : Form
{
    static readonly Color Ink = StudioTheme.Ink;
    static readonly Color Muted = StudioTheme.Muted;
    static readonly Color Accent = StudioTheme.Accent;
    static readonly Color Paper = Color.FromArgb(17, 20, 25);
    readonly List<string> files = [];
    readonly ListBox fileList = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, HorizontalScrollbar = true, ItemHeight = 32 };
    readonly Bitmap demoPhoto = LoadDemoPhoto();
    readonly PreviewCanvas preview = new() { Dock = DockStyle.Fill };
    readonly Label countLabel = new(), photoLabel = new(), status = new(), previewTag = new();
    readonly TextBox watermarkText = new() { MaxLength = 200 }, outputPath = new() { ReadOnly = true };
    readonly ComboBox fontBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly RadioButton textMode = new() { Text = "文字水印", AutoSize = true }, logoMode = new() { Text = "PNG Logo", AutoSize = true };
    readonly SliderField size = new() { Minimum=1, Maximum=20, Caption="字号 / 相对尺寸" }, opacity = new() { Caption="不透明度" }, margin = new() { Maximum=20, Caption="边距" };
    readonly SliderField rotation = new() { Minimum=-90, Maximum=90, Caption="旋转角度", Unit="°" }, spacing = new() { Minimum=15, Maximum=80, Caption="平铺间距" };
    readonly SliderField radius = new() { Minimum=15, Maximum=40, Caption="分布半径" }, patternCount = new() { Minimum=6, Maximum=16, Caption="分布数量", Unit=" 个" };
    readonly Button chooseLogo = new StudioButton(), colorButton = new StudioButton(), export = new StudioButton(), cancel = new StudioButton(), openFolder = new StudioButton();
    readonly Button compareButton = new StudioButton();
    readonly List<Button> layoutButtons = [];
    readonly Panel placementArea = new() { Dock=DockStyle.Fill };
    readonly Label patternHint = new() { Dock=DockStyle.Fill, TextAlign=ContentAlignment.MiddleLeft, ForeColor=StudioTheme.Muted };
    readonly Panel fivePointSketch = new() { Dock=DockStyle.Fill };
    int layoutMode;
    bool showOriginal;
    readonly Panel editing = new() { Dock = DockStyle.Fill };
    readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous };
    readonly System.Windows.Forms.Timer debounce = new() { Interval = 45 };
    readonly ToolTip tips = new();
    WatermarkSettings saved = SettingsStore.Load();
    LoadedPhoto? basePhoto;
    LoadedPhoto? logoPhoto;
    Color textColor;
    string logoPath = "";
    int loadVersion;
    bool initializing = true, busy, loadingPhoto;
    CancellationTokenSource? batchCancellation;
    List<BatchItem> failed = [];
    readonly Button failuresButton = new StudioButton();

    public MainForm(string[] startupFiles)
    {
        Text = "拾印 PhotoMark 1.4 · 照片水印工作室";
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Paper; ForeColor = Ink;
        ClientSize = new Size(1280, Math.Min(900, Screen.PrimaryScreen!.WorkingArea.Height-60)); MinimumSize = new Size(1080, 740);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AllowDrop = true;
        BuildLayout();
        LoadSettings();
        debounce.Tick += (_, _) => { debounce.Stop(); UpdatePreview(); };
        fileList.SelectedIndexChanged += async (_, _) => await LoadSelection();
        DragEnter += (_, e) => e.Effect = !busy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (!busy && e.Data?.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths); };
        FormClosing += (_, e) =>
        {
            if (busy) { batchCancellation?.Cancel(); status.Text = "正在取消，请等待当前照片完成后再关闭。"; e.Cancel = true; return; }
            try { SettingsStore.Save(Current()); } catch { /* Session works even if settings storage is unavailable. */ }
        };
        FormClosed += (_, _) => { loadVersion++; debounce.Dispose(); basePhoto?.Dispose(); logoPhoto?.Dispose(); preview.DisposeImage(); demoPhoto.Dispose(); tips.Dispose(); };
        Shown += (_, _) => { if (startupFiles.Length > 0) AddFiles(startupFiles); };
        initializing = false;
        SetMode(); UpdatePreview();
        UpdateActions();
    }

    static Bitmap LoadDemoPhoto()
    {
        using var stream=typeof(MainForm).Assembly.GetManifestResourceStream("PhotoMark.Assets.nature.jpg")!;
        using var image=Image.FromStream(stream);
        return new Bitmap(image);
    }

    static Label LabelOf(string text, int size = 9, bool bold = false) => new() { Text = text, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ink, Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular) };
    static Button ButtonOf(string text, EventHandler action, bool primary = false)
    {
        var b = new StudioButton(); StyleButton(b, text, primary); b.Click += action; return b;
    }
    static void StyleButton(Button b, string text, bool primary = false)
    {
        b.Text = text; b.Height = 36; b.AutoSize = false; b.Width = 100;
        b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = primary ? 0 : 1;
        b.FlatAppearance.BorderColor = StudioTheme.Border;
        b.BackColor = primary ? Accent : StudioTheme.Surface; b.ForeColor = primary ? Color.FromArgb(18,30,31) : Ink;
        b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false;
        b.AccessibleName = text;
    }
    static TableLayoutPanel Table(int columns, params float[] rows)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows.Length, Margin = Padding.Empty };
        if (columns == 1) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float h in rows) t.RowStyles.Add(h < 0 ? new RowStyle(SizeType.Percent, -h) : new RowStyle(SizeType.Absolute, h));
        return t;
    }

    void BuildLayout()
    {
        var root = Table(1, 76, -100, 112); Controls.Add(root);
        var header = Table(3, -100); header.BackColor=StudioTheme.Surface; header.Padding=new Padding(26,12,26,12);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,294)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));
        var title=LabelOf("拾印  PhotoMark",22,true); header.Controls.Add(title,0,0);
        var slogan=LabelOf("WATERMARK STUDIO   /   照片水印工作室"); slogan.ForeColor=Muted; header.Controls.Add(slogan,1,0);
        var offline=LabelOf("●  离线工作空间    v1.4"); offline.ForeColor=Accent; offline.TextAlign=ContentAlignment.MiddleRight; header.Controls.Add(offline,2,0); root.Controls.Add(header,0,0);
        editing.Padding=new Padding(20,20,20,20); root.Controls.Add(editing,0,1);
        var columns=Table(3,-100); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,208)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,360)); editing.Controls.Add(columns);

        var left=Table(1,34,26,46,-100,42,48); left.BackColor=StudioTheme.Surface; left.Padding=new Padding(14); left.Margin=new Padding(0,0,14,0);
        left.Controls.Add(LabelOf("素材库",12,true),0,0);
        countLabel.Dock=DockStyle.Fill; countLabel.ForeColor=Muted; countLabel.TextAlign=ContentAlignment.MiddleLeft; left.Controls.Add(countLabel,0,1);
        var import=ButtonOf("＋  添加照片",(_,_)=>PickPhotos(),true); import.Dock=DockStyle.Fill; left.Controls.Add(import,0,2);
        fileList.DrawMode=DrawMode.OwnerDrawFixed; fileList.ItemHeight=56; fileList.BackColor=StudioTheme.Surface;
        fileList.DrawItem+=(_,e)=>
        {
            if(e.Index<0||e.Index>=files.Count)return;
            bool selected=(e.State&DrawItemState.Selected)!=0;
            using var fill=new SolidBrush(selected?Color.FromArgb(37,52,53):StudioTheme.Surface);
            using var path=StudioTheme.Round(new RectangleF(e.Bounds.X,e.Bounds.Y+3,e.Bounds.Width-2,e.Bounds.Height-6),7); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias; e.Graphics.FillPath(fill,path);
            string name=Path.GetFileName(files[e.Index]);
            TextRenderer.DrawText(e.Graphics,name,Font,new Rectangle(e.Bounds.X+10,e.Bounds.Y+8,e.Bounds.Width-18,22),selected?Accent:Ink,TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics,$"{e.Index+1:00}   ·   {Path.GetExtension(name).TrimStart('.').ToUpperInvariant()}",Font,new Rectangle(e.Bounds.X+10,e.Bounds.Y+30,e.Bounds.Width-18,18),Muted,TextFormatFlags.EndEllipsis);
        };
        fileList.AccessibleName="已添加的照片"; left.Controls.Add(fileList,0,3);
        var listActions=Table(2,-100); listActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,56)); listActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,44));
        var remove=ButtonOf("移除选中",(_,_)=>RemoveSelected()); remove.Dock=DockStyle.Fill;
        var clear=ButtonOf("清空",(_,_)=>{files.Clear();fileList.Items.Clear();ClearPreview();UpdateActions();}); clear.Dock=DockStyle.Fill;
        listActions.Controls.Add(remove,0,0);listActions.Controls.Add(clear,1,0);left.Controls.Add(listActions,0,4);
        var help=LabelOf("拖入文件或文件夹\n支持 JPG / PNG");help.ForeColor=Muted;left.Controls.Add(help,0,5);columns.Controls.Add(left,0,0);

        Color stage=Color.FromArgb(20,24,30);
        var center=Table(1,46,-100,42);center.BackColor=stage;center.Padding=new Padding(16);center.Margin=new Padding(0,0,14,0);
        var previewHeader=Table(2,-100);previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));previewHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,118));
        var previewTitle=LabelOf("预览画布",11,true);previewTitle.ForeColor=Ink;previewHeader.Controls.Add(previewTitle,0,0);
        StyleButton(compareButton,"查看原图");compareButton.BackColor=Color.FromArgb(35,41,49);compareButton.ForeColor=Ink;compareButton.FlatAppearance.BorderColor=StudioTheme.Border;compareButton.Dock=DockStyle.Fill;
        compareButton.Click+=(_,_)=>{showOriginal=!showOriginal;compareButton.Text=showOriginal?"返回水印预览":"查看原图";UpdatePreview();};previewHeader.Controls.Add(compareButton,1,0);
        center.Controls.Add(previewHeader,0,0);center.Controls.Add(preview,0,1);
        var previewBottom=Table(2,-100);previewBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));previewBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));
        photoLabel.Dock=DockStyle.Fill;photoLabel.TextAlign=ContentAlignment.MiddleLeft;photoLabel.AutoEllipsis=true;photoLabel.ForeColor=Muted;photoLabel.Text="等待导入照片";previewBottom.Controls.Add(photoLabel,0,0);
        previewTag.Dock=DockStyle.Fill;previewTag.ForeColor=Muted;previewTag.Text="适应窗口";previewTag.TextAlign=ContentAlignment.MiddleRight;previewBottom.Controls.Add(previewTag,1,0);center.Controls.Add(previewBottom,0,2);columns.Controls.Add(center,1,0);

        var right=new Panel{Dock=DockStyle.Fill,BackColor=StudioTheme.Surface,AutoScroll=true,Padding=new Padding(18)};
        var inspector=Table(1,40,42,28,40,42,36,68,68,36,120,68,0,0,48);inspector.Name="Inspector";inspector.Dock=DockStyle.Top;inspector.AutoSize=true;inspector.AutoSizeMode=AutoSizeMode.GrowAndShrink;
        inspector.Controls.Add(LabelOf("水印样式",12,true),0,0);
        var modes=Table(2,-100);modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        foreach(var rb in new[]{textMode,logoMode}){rb.Appearance=Appearance.Button;rb.AutoSize=false;rb.Dock=DockStyle.Fill;rb.FlatStyle=FlatStyle.Flat;rb.FlatAppearance.BorderSize=0;rb.TextAlign=ContentAlignment.MiddleCenter;rb.Cursor=Cursors.Hand;rb.Margin=new Padding(2);}
        textMode.Text="Tt   文字";logoMode.Text="▧   图片 Logo";modes.Controls.Add(textMode,0,0);modes.Controls.Add(logoMode,1,0);inspector.Controls.Add(modes,0,1);
        inspector.Controls.Add(LabelOf("水印内容"),0,2);
        var content=new Panel{Dock=DockStyle.Fill};watermarkText.Dock=DockStyle.Top;watermarkText.Font=new Font(Font.FontFamily,11);watermarkText.BorderStyle=BorderStyle.FixedSingle;watermarkText.BackColor=StudioTheme.Raised;watermarkText.ForeColor=Ink;watermarkText.AccessibleName="水印文字";content.Controls.Add(watermarkText);
        StyleButton(chooseLogo,"选择透明 PNG…");chooseLogo.Dock=DockStyle.Fill;chooseLogo.Click+=(_,_)=>PickLogo();content.Controls.Add(chooseLogo);inspector.Controls.Add(content,0,3);
        var fontRow=Table(2,-100);fontRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));fontRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,68));
        fontBox.Dock=DockStyle.Fill;fontBox.FlatStyle=FlatStyle.Flat;fontBox.BackColor=StudioTheme.Raised;fontBox.ForeColor=Ink;fontBox.AccessibleName="文字字体";
        foreach(var family in FontFamily.Families.OrderBy(f=>f.Name)){fontBox.Items.Add(family.Name);family.Dispose();}
        StyleButton(colorButton,"颜色");colorButton.Dock=DockStyle.Fill;colorButton.Click+=(_,_)=>{using var d=new ColorDialog{Color=textColor,FullOpen=true};if(d.ShowDialog(this)==DialogResult.OK){textColor=d.Color;UpdateColor();Changed();}};
        fontRow.Controls.Add(fontBox,0,0);fontRow.Controls.Add(colorButton,1,0);inspector.Controls.Add(fontRow,0,4);
        var palette=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};
        foreach(var color in new[]{Color.White,Color.FromArgb(140,200,240),Color.FromArgb(167,216,199),Color.FromArgb(246,210,141),Color.FromArgb(232,164,170),Color.FromArgb(33,42,52)})
        {var swatch=ButtonOf("",(_,_)=>{textColor=color;UpdateColor();Changed();});swatch.Size=new Size(27,24);swatch.BackColor=color;swatch.Margin=new Padding(0,1,8,1);swatch.AccessibleName="颜色 "+ColorTranslator.ToHtml(color);palette.Controls.Add(swatch);}
        inspector.Controls.Add(palette,0,5);
        size.Dock=DockStyle.Fill;opacity.Dock=DockStyle.Fill;inspector.Controls.Add(size,0,6);inspector.Controls.Add(opacity,0,7);
        inspector.Controls.Add(LabelOf("位置模式",9,true),0,8);
        var layouts=Table(3,40,40,40);for(int i=0;i<3;i++)layouts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.33f));
        for(int i=0;i<WatermarkLayouts.Names.Length;i++){int index=i;var b=ButtonOf(WatermarkLayouts.Names[i],(_,_)=>SetLayout(index,true));b.Dock=DockStyle.Fill;b.Margin=new Padding(1,2,1,2);layoutButtons.Add(b);layouts.Controls.Add(b,i%3,i/3);}inspector.Controls.Add(layouts,0,9);
        // A two-row parameter area replaces the old individual position buttons.
        var patternControls=Table(1,68,0);
        var parameterTop=new Panel{Dock=DockStyle.Fill};var parameterBottom=new Panel{Dock=DockStyle.Fill};
        spacing.Dock=DockStyle.Fill;radius.Dock=DockStyle.Fill;patternCount.Dock=DockStyle.Fill;
        parameterTop.Controls.Add(spacing);parameterTop.Controls.Add(radius);parameterTop.Controls.Add(fivePointSketch);parameterBottom.Controls.Add(patternHint);
        fivePointSketch.Paint+=(_,e)=>
        {
            float d=DeviceDpi/96f;var r=new RectangleF(8*d,7*d,112*d,34*d);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using var p=new Pen(StudioTheme.Border);using var shape=StudioTheme.Round(r,5*d);e.Graphics.DrawPath(p,shape);
            using var brush=new SolidBrush(Accent);
            foreach(var pt in new[]{new PointF(r.Left+10*d,r.Top+8*d),new PointF(r.Right-10*d,r.Top+8*d),new PointF(r.Left+10*d,r.Bottom-8*d),new PointF(r.Right-10*d,r.Bottom-8*d),new PointF(r.Left+r.Width/2,r.Top+r.Height/2)})e.Graphics.FillEllipse(brush,pt.X-2.5f*d,pt.Y-2.5f*d,5*d,5*d);
        };
        patternControls.Controls.Add(parameterTop,0,0);patternControls.Controls.Add(parameterBottom,0,1);
        placementArea.Controls.Add(patternControls);inspector.Controls.Add(placementArea,0,10);
        margin.Visible=false;rotation.Visible=false;
        var preset=ButtonOf("套用：模拟IC互助 · 浅蓝平铺",(_,_)=>ApplyCommunityPreset());preset.Dock=DockStyle.Fill;preset.BackColor=Color.FromArgb(37,52,53);preset.ForeColor=Accent;preset.FlatAppearance.BorderSize=0;inspector.Controls.Add(preset,0,13);
        right.Controls.Add(inspector);columns.Controls.Add(right,2,0);

        var footer=Table(3,40,14,32);footer.BackColor=StudioTheme.Surface;footer.Padding=new Padding(24,10,24,10);footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));
        var folderRow=Table(3,-100);folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));folderRow.Controls.Add(LabelOf("保存位置"),0,0);
        outputPath.Dock=DockStyle.Fill;outputPath.Margin=new Padding(0,9,10,0);outputPath.BorderStyle=BorderStyle.None;outputPath.BackColor=StudioTheme.Surface;outputPath.ForeColor=Muted;outputPath.AccessibleName="导出文件夹";folderRow.Controls.Add(outputPath,1,0);
        var chooseOutput=ButtonOf("更改…",(_,_)=>{if(busy)return;using var d=new FolderBrowserDialog{Description="选择导出文件夹",UseDescriptionForTitle=true,SelectedPath=outputPath.Text};if(d.ShowDialog(this)==DialogResult.OK){outputPath.Text=d.SelectedPath;SaveSettings();UpdateActions();}});chooseOutput.Dock=DockStyle.Fill;folderRow.Controls.Add(chooseOutput,2,0);footer.Controls.Add(folderRow,0,0);
        StyleButton(cancel,"取消任务");cancel.Dock=DockStyle.Fill;cancel.Enabled=false;cancel.Click+=(_,_)=>{batchCancellation?.Cancel();cancel.Enabled=false;status.Text="正在取消，当前照片完成后停止…";};footer.Controls.Add(cancel,1,0);
        StyleButton(export,"导出照片  →",true);export.Dock=DockStyle.Fill;export.Font=new Font(Font,FontStyle.Bold);export.Click+=async(_,_)=>await ExportBatch();footer.Controls.Add(export,2,0);
        progress.Margin=new Padding(0,5,0,5);footer.Controls.Add(progress,0,1);footer.SetColumnSpan(progress,3);
        status.Dock=DockStyle.Fill;status.ForeColor=Muted;status.TextAlign=ContentAlignment.MiddleLeft;status.AutoEllipsis=true;status.Text="准备就绪 · 原图始终保留，重名自动编号";footer.Controls.Add(status,0,2);
        StyleButton(failuresButton,"失败详情");failuresButton.Dock=DockStyle.Fill;failuresButton.Enabled=false;failuresButton.Click+=(_,_)=>ShowFailures();footer.Controls.Add(failuresButton,1,2);
        StyleButton(openFolder,"打开导出文件夹");openFolder.Dock=DockStyle.Fill;openFolder.Click+=(_,_)=>{if(Directory.Exists(outputPath.Text))Process.Start(new ProcessStartInfo(outputPath.Text){UseShellExecute=true});};footer.Controls.Add(openFolder,2,2);root.Controls.Add(footer,0,2);
        textMode.CheckedChanged+=(_,_)=>{if(!initializing&&textMode.Checked)SetMode();};logoMode.CheckedChanged+=(_,_)=>{if(!initializing&&logoMode.Checked)SetMode();};
        watermarkText.TextChanged+=(_,_)=>Changed();fontBox.SelectedIndexChanged+=(_,_)=>Changed();
        foreach(var slider in new[]{size,opacity,margin,rotation,spacing,radius,patternCount})slider.ValueChanged+=(_,_)=>Changed();
        tips.SetToolTip(size,"文字字号按照片短边计算，Logo 按照片宽度计算。拖动调节，方向键精调，Shift + 方向键每次 5。");
        tips.SetToolTip(spacing,"间距越小越密集；水印过大时自动缩小，避免相互遮挡。");tips.SetToolTip(compareButton,"切换原图与水印效果，仅影响预览。");
        tips.SetToolTip(radius,"以照片短边为基准，调整图案向外扩展的半径。");tips.SetToolTip(patternCount,"散射模式调整外圈数量，内圈自动配合；圆形模式调整圆周数量。");
    }

    void LoadSettings()
    {
        watermarkText.Text = saved.Text; textMode.Checked = !saved.UseLogo; logoMode.Checked = saved.UseLogo;
        fontBox.SelectedItem = saved.FontName; if (fontBox.SelectedIndex < 0) fontBox.SelectedItem = "Microsoft YaHei UI"; if (fontBox.SelectedIndex < 0 && fontBox.Items.Count > 0) fontBox.SelectedIndex = 0;
        textColor = Color.FromArgb(saved.ColorArgb); logoPath = saved.LogoPath;
        size.Maximum = saved.UseLogo ? 60 : 20; size.Minimum = saved.UseLogo ? 5 : 1; size.Value = saved.UseLogo ? saved.LogoPercent : saved.SizePercent;
        opacity.Value = saved.Opacity; margin.Value = saved.MarginPercent;
        rotation.Value = saved.Rotation; spacing.Value = saved.TileSpacingPercent; layoutMode = saved.LayoutMode;
        radius.Value=saved.PatternRadiusPercent;patternCount.Value=saved.PatternCount;
        outputPath.Text = saved.OutputFolder;
        if (File.Exists(logoPath))
        {
            try { logoPhoto = Imaging.Load(logoPath, 1600); if (!logoPhoto.IsPng) { logoPhoto.Dispose(); logoPhoto = null; } }
            catch { logoPhoto = null; }
        }
        UpdateLogoButton(); UpdateColor(); SetLayout(layoutMode, false);
    }
    WatermarkSettings Current() => saved with
    {
        UiVersion = 14,
        UseLogo = logoMode.Checked, Text = watermarkText.Text, FontName = fontBox.SelectedItem?.ToString() ?? "Microsoft YaHei UI",
        LogoPath = logoPath, ColorArgb = textColor.ToArgb(), SizePercent = textMode.Checked ? (int)size.Value : saved.SizePercent,
        LogoPercent = logoMode.Checked ? (int)size.Value : saved.LogoPercent, Opacity = (int)opacity.Value,
        MarginPercent = 4, OutputFolder = outputPath.Text,
        LayoutMode = layoutMode, Rotation = layoutMode==2?-25:0, TileSpacingPercent = spacing.Value,
        PatternRadiusPercent=radius.Value,PatternCount=8
    };
    void SetMode()
    {
        initializing = true;
        size.Minimum = 1; size.Maximum = logoMode.Checked ? 60 : 20;
        size.Value = logoMode.Checked ? saved.LogoPercent : saved.SizePercent; size.Minimum = logoMode.Checked ? 5 : 1;
        watermarkText.Enabled = textMode.Checked; watermarkText.Visible = textMode.Checked;
        fontBox.Enabled = textMode.Checked; colorButton.Enabled = textMode.Checked; chooseLogo.Enabled = logoMode.Checked; chooseLogo.Visible = logoMode.Checked;
        textMode.BackColor=textMode.Checked?Color.FromArgb(37,52,53):Color.FromArgb(35,41,49);
        logoMode.BackColor=logoMode.Checked?Color.FromArgb(37,52,53):Color.FromArgb(35,41,49);
        textMode.FlatAppearance.CheckedBackColor=StudioTheme.Raised;logoMode.FlatAppearance.CheckedBackColor=StudioTheme.Raised;textMode.ForeColor=textMode.Checked?Accent:Muted;logoMode.ForeColor=logoMode.Checked?Accent:Muted;
        size.Caption=logoMode.Checked?"Logo 大小 / 相对宽度":"字号 / 相对尺寸";
        initializing = false; Changed();
    }
    void UpdateColor() { colorButton.BackColor = textColor; colorButton.ForeColor = textColor.GetBrightness() < .5 ? Color.White : Color.FromArgb(31,39,48); }
    void UpdateLogoButton() { chooseLogo.Text = logoPhoto != null ? "✓ " + Path.GetFileName(logoPath) : "选择透明 PNG…"; tips.SetToolTip(chooseLogo, logoPath); }
    void Changed() { if (initializing) return; saved = Current(); if (!debounce.Enabled) debounce.Start(); UpdateActions(); }
    void SetLayout(int mode, bool fromUser)
    {
        bool previousInitializing=initializing; initializing=true;
        layoutMode=mode;
        if(fromUser) rotation.Value=mode==2?-25:0;
        for(int i=0;i<layoutButtons.Count;i++){layoutButtons[i].BackColor=i==mode?Accent:Color.FromArgb(35,41,49);layoutButtons[i].ForeColor=i==mode?Color.FromArgb(18,30,31):Ink;layoutButtons[i].FlatAppearance.BorderSize=0;}
        spacing.Visible=mode is 1 or 2;radius.Visible=mode>=3;patternCount.Visible=false;margin.Enabled=mode==0;
        fivePointSketch.Visible=mode==0;
        patternCount.Caption=mode==3?"外圈数量":"圆周数量";
        patternHint.Visible=mode is 0 or 5;patternHint.Text=mode==0?"中心 + 四角，共 5 处水印\n一次显示，无需逐个选择。":"中心 + 上下左右，共 5 处水印";
        margin.Caption=mode==0?"四角边距":"边距 · 五点模式可调";
        initializing=previousInitializing;if(fromUser)Changed();
    }
    void ApplyCommunityPreset()
    {
        textMode.Checked=true;initializing=true;
        watermarkText.Text="模拟IC互助";textColor=Color.FromArgb(140,200,240);size.Value=4;opacity.Value=43;spacing.Value=34;
        SetLayout(2,false);rotation.Value=-25;UpdateColor();initializing=false;Changed();
    }
    void SaveSettings() { saved = Current(); try { SettingsStore.Save(saved); } catch (Exception ex) { status.Text = "设置未保存：" + ex.Message; } }

    void PickPhotos()
    {
        using var dialog = new OpenFileDialog { Title = "添加照片", Filter = "JPG / PNG 照片|*.jpg;*.jpeg;*.png", Multiselect = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(dialog.FileNames);
    }
    void AddFiles(IEnumerable<string> paths)
    {
        if (busy) return;
        int added = 0, ignored = 0;
        foreach (var input in paths)
        {
            IEnumerable<string> candidates;
            try { candidates = Directory.Exists(input) ? Directory.GetFiles(input) : [input]; }
            catch { ignored++; continue; }
            foreach (string path in candidates)
            {
                if (!Imaging.Supports(path) || !File.Exists(path)) { ignored++; continue; }
                string full = Path.GetFullPath(path);
                if (files.Contains(full, StringComparer.OrdinalIgnoreCase)) continue;
                files.Add(full); fileList.Items.Add(Path.GetFileName(full)); added++;
            }
        }
        if (files.Count > 0 && string.IsNullOrWhiteSpace(outputPath.Text)) outputPath.Text = Path.Combine(Path.GetDirectoryName(files[0])!, "水印照片");
        if (fileList.SelectedIndex < 0 && files.Count > 0) fileList.SelectedIndex = 0;
        status.Text = $"已添加 {added} 张照片" + (ignored > 0 ? $" · 忽略 {ignored} 个非 JPG/PNG 或无法访问的文件" : " · 选择照片查看预览");
        UpdateActions();
    }
    void RemoveSelected()
    {
        int index = fileList.SelectedIndex; if (index < 0) return;
        files.RemoveAt(index); fileList.Items.RemoveAt(index);
        if (files.Count > 0) fileList.SelectedIndex = Math.Min(index, files.Count - 1); else ClearPreview();
        UpdateActions();
    }
    void ClearPreview()
    {
        loadVersion++; loadingPhoto = false; basePhoto?.Dispose(); basePhoto = null; preview.SetImage(null); preview.Message = "把照片拖到这里\n或点击左侧「添加照片」"; preview.Invalidate(); UpdatePreview();
    }
    async Task LoadSelection()
    {
        int index = fileList.SelectedIndex; if (index < 0 || index >= files.Count) return;
        string file = files[index]; int version = ++loadVersion; loadingPhoto = true;
        basePhoto?.Dispose(); basePhoto = null; preview.SetImage(null); preview.Message = "正在读取照片…"; preview.Invalidate(); photoLabel.Text = Path.GetFileName(file);
        try
        {
            var loaded = await Task.Run(() => Imaging.Load(file, 1600));
            if (IsDisposed || version != loadVersion) { loaded.Dispose(); return; }
            basePhoto = loaded; loadingPhoto = false; UpdatePreview();
        }
        catch (Exception ex)
        {
            if (IsDisposed || version != loadVersion) return;
            loadingPhoto = false; preview.Message = "无法预览这张照片\n" + ex.Message; preview.Invalidate(); status.Text = "预览失败：" + Path.GetFileName(file);
        }
    }
    void UpdatePreview()
    {
        if (loadingPhoto) return;
        var source=basePhoto?.Pixels ?? demoPhoto; if(basePhoto==null)photoLabel.Text="自然风景 · 示例预览（不参与导出）";
        try
        {
            if (showOriginal || (logoMode.Checked && logoPhoto == null)) { preview.SetImage((Bitmap)source.Clone()); previewTag.Text = showOriginal ? "原图" : "请选择 Logo"; return; }
            preview.SetImage(Imaging.Render(source, Current(), logoPhoto?.Pixels)); previewTag.Text = WatermarkLayouts.Names[layoutMode];
        }
        catch (Exception ex) { preview.SetImage(null); preview.Message = "预览失败\n" + ex.Message; preview.Invalidate(); }
    }
    void PickLogo()
    {
        using var dialog = new OpenFileDialog { Title = "选择 PNG Logo", Filter = "PNG Logo|*.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        LoadedPhoto? newLogo = null;
        try
        {
            newLogo = Imaging.Load(dialog.FileName, 1600);
            if (!newLogo.IsPng) throw new InvalidDataException("请选择真正的 PNG 图片。");
            logoPhoto?.Dispose(); logoPhoto = newLogo; newLogo = null; logoPath = dialog.FileName; UpdateLogoButton(); Changed();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法读取 Logo", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { newLogo?.Dispose(); }
    }
    void UpdateActions()
    {
        countLabel.Text = $"{files.Count} 张照片";
        export.Enabled = !busy && files.Count > 0 && (logoMode.Checked ? logoPhoto != null : !string.IsNullOrWhiteSpace(watermarkText.Text));
        openFolder.Enabled = !busy && Directory.Exists(outputPath.Text);
    }
    async Task ExportBatch()
    {
        if (busy || files.Count == 0) return;
        var snapshot = Current(); var inputs = files.ToArray(); string folder = outputPath.Text;
        try { if (string.IsNullOrWhiteSpace(folder)) throw new IOException("请选择导出文件夹。"); Directory.CreateDirectory(folder); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法创建导出文件夹", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        busy = true; editing.Enabled = false; cancel.Enabled = true; failuresButton.Enabled = false; export.Enabled = false; openFolder.Enabled = false;
        failed = []; progress.Maximum = inputs.Length; progress.Value = 0; batchCancellation = new(); SaveSettings();
        status.Text = $"正在导出 0 / {inputs.Length}…";
        var reporter = new Progress<BatchProgress>(p => { progress.Value = p.Completed; status.Text = $"正在导出 {p.Completed} / {p.Total} · {Path.GetFileName(p.Item.Input)}"; });
        try
        {
            var result = await Task.Run(() => BatchExporter.Run(inputs, folder, snapshot, reporter, batchCancellation.Token));
            failed = result.Items.Where(i => i.Error != null).ToList();
            status.Text = $"{(result.Cancelled ? "已取消" : "导出完成")} · 成功 {result.Items.Count - failed.Count} 张 / 失败 {failed.Count} 张" + (result.Cancelled ? $" / 未处理 {inputs.Length - result.Items.Count} 张" : " · 原图未修改");
        }
        catch (Exception ex) { status.Text = "导出失败：" + ex.Message; MessageBox.Show(this, ex.Message, "无法导出", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally
        {
            busy = false; editing.Enabled = true; cancel.Enabled = false; failuresButton.Enabled = failed.Count > 0; batchCancellation.Dispose(); batchCancellation = null; UpdateActions();
        }
    }
    void ShowFailures()
    {
        using var dialog = new Form { Text = "导出失败项", Size = new Size(680, 420), StartPosition = FormStartPosition.CenterParent, Font = Font, MinimizeBox = false, MaximizeBox = false };
        dialog.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, Text = string.Join(Environment.NewLine + Environment.NewLine, failed.Select(i => i.Input + Environment.NewLine + i.Error)) });
        dialog.ShowDialog(this);
    }

    internal async Task RunUiChecks(string root)
    {
        var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add("PASS: "+name);}
        UpdatePreview();
        Check(files.Count==0&&!export.Enabled&&preview.Fingerprint().Length>0,"Embedded nature preview does not enter export queue");
        Check(watermarkText.Text=="拾印 PhotoMark","Default preview watermark");
        Check(layoutButtons.Count==8,"Eight position modes");
        foreach(var window in new[]{new Size(1280,900),MinimumSize})
        {
            Size=window;
            for(int mode=0;mode<8;mode++)
            {
                SetLayout(mode,true);UpdatePreview();PerformLayout();
                Check(previewTag.Text==WatermarkLayouts.Names[mode],"Live demo mode "+mode);
                Check(!margin.Visible&&!rotation.Visible&&!patternCount.Visible,"Removed parameters stay hidden / "+mode);
                foreach(var field in new[]{size,opacity,spacing,radius}.Where(c=>c.Visible))
                    Check(field.Height>=Math.Ceiling(58*field.DeviceDpi/96f),"Slider spacing / "+field.Caption+" / "+mode);
            }
        }
        Size=new Size(1280,900);SetLayout(6,true);UpdatePreview();
        using(var shot=new Bitmap(Width,Height)){DrawToBitmap(shot,new Rectangle(Point.Empty,Size));shot.Save(Path.Combine(root,"界面-暗黑自然.png"));}
        opacity.Value=0;UpdatePreview();var before=preview.Fingerprint();opacity.TestPointer(.65f);await Task.Delay(150);
        Check(opacity.Value==65&&!before.SequenceEqual(preview.Fingerprint()),"Slider refreshes nature demo");
        compareButton.PerformClick();Check(showOriginal,"Original comparison");compareButton.PerformClick();
        AddFiles([Path.Combine(root,"示例风景.png"),Path.Combine(root,"输入照片","损坏图片.jpg")]);await LoadSelection();
        Check(files.Count==2&&basePhoto!=null,"Import replaces demo");
        outputPath.Text=Path.Combine(root,"界面导出");
        foreach(int mode in new[]{6,7}){SetLayout(mode,true);await ExportBatch();Check(failed.Count==1&&!busy&&export.Enabled,"New mode exports and reports corrupt input / "+mode);}
        Check(Directory.GetFiles(outputPath.Text,"*.png").Length==2,"Exports auto-number without replacing originals");
        logoMode.Checked=true;Check(!export.Enabled,"Logo requires image");
        logoPath=Path.Combine(root,"透明Logo.png");logoPhoto=Imaging.Load(logoPath);UpdateLogoButton();Changed();
        await ExportBatch();Check(Directory.GetFiles(outputPath.Text,"*.png").Length==3,"Logo export in new layout");
        textMode.Checked=true;files.Clear();fileList.Items.Clear();ClearPreview();UpdateActions();
        Check(!export.Enabled&&preview.Fingerprint().Length>0,"Clear restores demo without allowing export");
        File.WriteAllLines(Path.Combine(root,"ui-results.txt"),checks.Append($"PASS: {checks.Count} UI checks (in-process; not OS input automation)"));
    }
}

internal sealed class PreviewCanvas : Control
{
    Bitmap? image;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Message { get; set; } = "把照片拖到这里\n或点击左侧「添加照片」";
    public PreviewCanvas() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.FromArgb(20,24,30); }
    public void SetImage(Bitmap? next) { var old = image; image = next; old?.Dispose(); Invalidate(); }
    public void DisposeImage() { image?.Dispose(); image = null; }
    internal byte[] Fingerprint() { if(image==null)return [];using var stream=new MemoryStream();image.Save(stream,System.Drawing.Imaging.ImageFormat.Png);return System.Security.Cryptography.SHA256.HashData(stream.ToArray()); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics;
        using (var dots=new SolidBrush(Color.FromArgb(38,44,52)))
            for(int dy=16;dy<Height;dy+=24)for(int dx=16;dx<Width;dx+=24)g.FillEllipse(dots,dx,dy,1.5f,1.5f);
        if (image == null)
        {
            int x = Width / 2, y = Height / 2 - 65;
            using var pen = new Pen(Color.FromArgb(104,150,148), 2);
            g.DrawRectangle(pen, x - 35, y - 24, 70, 53); g.DrawEllipse(pen, x + 13, y - 14, 9, 9);
            g.DrawLines(pen, [new(x - 28, y + 20), new(x - 6, y - 4), new(x + 6, y + 11), new(x + 18, y + 2), new(x + 30, y + 20)]);
            using var font = new Font("Microsoft YaHei UI", 11);
            using var brush = new SolidBrush(StudioTheme.Muted);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(Message, font, brush, new RectangleF(18, y + 45, Math.Max(1, Width - 36), 120), format); return;
        }
        float scale = Math.Min((Width - 24f) / image.Width, (Height - 24f) / image.Height); if (scale <= 0) return;
        var rect = new Rectangle((int)((Width - image.Width * scale) / 2), (int)((Height - image.Height * scale) / 2), Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
        using(var shadow=new SolidBrush(Color.FromArgb(8,11,15)))g.FillRectangle(shadow,rect.X+5,rect.Y+7,rect.Width,rect.Height);
        using var light = new SolidBrush(Color.White); using var dark = new SolidBrush(Color.FromArgb(221, 228, 229));
        g.FillRectangle(light, rect);
        var state = g.Save(); g.SetClip(rect);
        for (int y = rect.Top; y < rect.Bottom; y += 12) for (int x = rect.Left; x < rect.Right; x += 12)
            if (((x - rect.Left) / 12 + (y - rect.Top) / 12) % 2 == 0) g.FillRectangle(dark, x, y, 12, 12);
        Imaging.Quality(g); g.DrawImage(image, rect); g.Restore(state);
    }
}



