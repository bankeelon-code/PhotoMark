using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace PhotoMark;

internal static class SelfTest
{
    public static int Run(string root)
    {
        Directory.CreateDirectory(root);
        var results = new List<string>();
        try
        {
            string input = Path.Combine(root, "输入照片"), output = Path.Combine(root, "导出照片");
            Directory.CreateDirectory(input); Directory.CreateDirectory(output);
            string photo = Path.Combine(input, "中文 横图.png");
            using (var b = new Bitmap(640, 480)) { using var g = Graphics.FromImage(b); g.Clear(Color.FromArgb(32, 70, 90)); b.Save(photo, ImageFormat.Png); }
            string logoPath = Path.Combine(root, "透明Logo.png");
            using (var b = new Bitmap(100, 100, PixelFormat.Format32bppArgb))
            { using var g = Graphics.FromImage(b); g.Clear(Color.Transparent); using var brush = new SolidBrush(Color.FromArgb(128, 255, 0, 0)); g.FillRectangle(brush, 25, 25, 50, 50); b.Save(logoPath); }
            var settings = new WatermarkSettings { Text = "© 中文摄影 2026", SizePercent = 8, Opacity = 100, Position = 2 };
            byte[] before = SHA256.HashData(File.ReadAllBytes(photo));
            using (var source = Imaging.Load(photo))
            using (var rendered = Imaging.Render(source.Pixels, settings))
            {
                int changed = 0;
                for (int y = 0; y < 480; y++) for (int x = 0; x < 640; x++) if (source.Pixels.GetPixel(x,y) != rendered.GetPixel(x,y)) changed++;
                Check(changed > 100, "Chinese text actually renders", results);
                rendered.Save(Path.Combine(root, "文字水印样例.png"));
                using var zero = Imaging.Render(source.Pixels, settings with { Opacity = 0 });
                Check(zero.GetPixel(320,240) == source.Pixels.GetPixel(320,240), "Zero opacity preserves pixel", results);
            }
            using (var solid = new Bitmap(1000, 1000))
            using (var logo = Imaging.Load(logoPath))
            {
                using (var g = Graphics.FromImage(solid)) g.Clear(Color.White);
                var ls = settings with { UseLogo = true, LogoPercent = 10, Opacity = 50, MarginPercent = 0, Position = 0 };
                using var marked = Imaging.Render(solid, ls, logo.Pixels);
                var c = marked.GetPixel(50, 50);
                // GDI+ HighQuality compositing blends in a gamma-corrected space (approximately 224).
                Check(c.R >= 250 && c.G is >= 222 and <= 226 && c.B is >= 222 and <= 226, $"PNG alpha multiplied by opacity (gamma-corrected): {c}", results);
                Check(marked.GetPixel(5,5).ToArgb() == Color.White.ToArgb(), "Transparent logo margin stays transparent", results);
                using var transparent = new Bitmap(100, 100, PixelFormat.Format32bppArgb);
                string transparentInput = Path.Combine(input, "透明底图.png"); transparent.Save(transparentInput);
                string transparentOut = Imaging.Export(transparentInput, output, ls, logo.Pixels);
                using var transparentResult = Imaging.Load(transparentOut);
                Check(transparentResult.Pixels.GetPixel(90,90).A == 0, "PNG output retains source transparency", results);
                // Compare actual mark geometry after full-size rendering versus preview-sized rendering.
                using var half = new Bitmap(500, 500); using (var g = Graphics.FromImage(half)) g.Clear(Color.White);
                var loc = ls with { Position = 4, MarginPercent = 4, LogoPercent = 20, Opacity = 100 };
                using var fullRendered = Imaging.Render(solid, loc, logo.Pixels);
                using var halfRendered = Imaging.Render(half, loc, logo.Pixels);
                Check(fullRendered.GetPixel(860,860).G < 200 && halfRendered.GetPixel(430,430).G < 200, "Preview/export relative watermark placement agrees", results);
            }
            string one = Imaging.Export(photo, output, settings, null), two = Imaging.Export(photo, output, settings, null);
            Check(one != two && File.Exists(one) && File.Exists(two), "Repeated exports auto-number without overwrite", results);
            Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(photo))), "Original file unchanged", results);
            string sameFolder = Imaging.Export(photo, input, settings, null);
            Check(sameFolder != photo && before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(photo))), "Export into source folder still preserves original", results);

            string jpeg = Path.Combine(input, "竖拍方向.jpg");
            using (var b = new Bitmap(80, 40)) { using var g = Graphics.FromImage(b); g.Clear(Color.Blue); g.FillRectangle(Brushes.Red, 0, 0, 40, 40); b.Save(jpeg, ImageFormat.Jpeg); }
            byte[] jpg = File.ReadAllBytes(jpeg);
            // Little-endian EXIF IFD containing orientation=6 (90 degrees clockwise).
            byte[] exif = [0xFF,0xE1,0,34,69,120,105,102,0,0,73,73,42,0,8,0,0,0,1,0,0x12,1,3,0,1,0,0,0,6,0,0,0,0,0,0,0];
            File.WriteAllBytes(jpeg, jpg.Take(2).Concat(exif).Concat(jpg.Skip(2)).ToArray());
            using (var rotated = Imaging.Load(jpeg))
                Check(rotated.Pixels.Width == 40 && rotated.Pixels.Height == 80 && rotated.Pixels.GetPixel(20,10).R > 180, "EXIF portrait orientation corrected", results);
            string rotatedOut = Imaging.Export(jpeg, output, settings with { Opacity = 0 }, null);
            using (var decoded = Image.FromFile(rotatedOut))
                Check(decoded.Width == 40 && decoded.Height == 80 && !decoded.PropertyIdList.Contains(0x0112) && !decoded.PropertyIdList.Contains(0x8825), "Export has corrected dimensions and no EXIF orientation/GPS", results);

            // Verify exact ICC APP2 preservation independently of the image renderer.
            byte[] iccPayload = Encoding.ASCII.GetBytes("ICC_PROFILE\0").Concat(new byte[] {1,1}).Concat(new byte[128]).ToArray();
            byte[] app2 = new byte[] {255,226,(byte)((iccPayload.Length+2)>>8),(byte)(iccPayload.Length+2)}.Concat(iccPayload).ToArray();
            string profiled = Path.Combine(input, "颜色标记.jpg");
            File.WriteAllBytes(profiled, jpg.Take(2).Concat(app2).Concat(jpg.Skip(2)).ToArray());
            string profiledOut = Imaging.Export(profiled, output, settings, null);
            using (var stream = File.OpenRead(profiledOut)) Check(Imaging.ReadColorChunks(stream, false).Single().SequenceEqual(app2), "JPEG ICC APP2 bytes preserved", results);

            byte[] gammaData = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(gammaData, 45455);
            byte[] gamma = PngChunk("gAMA", gammaData);
            byte[] pngBytes = File.ReadAllBytes(photo);
            string taggedPng = Path.Combine(input, "PNG颜色.png");
            File.WriteAllBytes(taggedPng, pngBytes.Take(33).Concat(gamma).Concat(pngBytes.Skip(33)).ToArray());
            string taggedOut = Imaging.Export(taggedPng, output, settings, null);
            using (var stream = File.OpenRead(taggedOut)) Check(Imaging.ReadColorChunks(stream, true).Any(c => c.SequenceEqual(gamma)), "PNG color chunk preserved", results);
            using (var b = new Bitmap(6000, 4000))
            { using var g = Graphics.FromImage(b); g.Clear(Color.SteelBlue); b.Save(Path.Combine(input, "2400万像素.jpg"), ImageFormat.Jpeg); }
            string largeOut = Imaging.Export(Path.Combine(input, "2400万像素.jpg"), output, settings, null);
            using (var large = Image.FromFile(largeOut)) Check(large.Width == 6000 && large.Height == 4000, "24 MP photo exports at original dimensions", results);

            var batchFiles = new List<string>();
            for (int i = 0; i < 100; i++)
            {
                string path = Path.Combine(input, $"批量-{i:000}" + (i % 2 == 0 ? ".png" : ".jpg"));
                using var b = new Bitmap(i % 2 == 0 ? 320 : 240, i % 2 == 0 ? 240 : 320);
                using (var g = Graphics.FromImage(b)) g.Clear(Color.FromArgb(60+i, 100, 140));
                b.Save(path, i % 2 == 0 ? ImageFormat.Png : ImageFormat.Jpeg); batchFiles.Add(path);
            }
            string bad = Path.Combine(input, "损坏图片.jpg"); File.WriteAllText(bad, "not an image"); batchFiles.Insert(17, bad);
            var result = BatchExporter.Run(batchFiles, Path.Combine(root, "批量导出"), settings, null, CancellationToken.None);
            Check(result.Items.Count(i => i.Output != null) == 100 && result.Items.Count(i => i.Error != null) == 1, "100 mixed photos succeed; corrupt file isolated", results);
            using var cancellation = new CancellationTokenSource();
            int events = 0;
            var cancelResult = BatchExporter.Run(batchFiles, Path.Combine(root, "取消测试"), settings, new InlineProgress(p => { events++; cancellation.Cancel(); }), cancellation.Token);
            Check(cancelResult.Cancelled && events == 1 && cancelResult.Items.Count == 1, "Cancellation stops between images", results);
            Check(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "No partial temporary output left behind", results);
            Check(new WatermarkSettings { SizePercent = 1000, Opacity = -1, Position = 90 }.Normalized() is { SizePercent: 20, Opacity: 0, Position: 8 }, "Settings values clamped", results);
            CheckLayouts(root, results);
            MakeDemo(Path.Combine(root, "示例风景.png"));
            MakeLayoutSheet(root);
            File.WriteAllLines(Path.Combine(root, "test-results.txt"), results.Append($"PASS: {results.Count} checks"));
            return 0;
        }
        catch (Exception ex) { File.WriteAllLines(Path.Combine(root, "test-results.txt"), results.Append("FAIL: " + ex)); return 1; }
    }
    static void Check(bool condition, string name, List<string> results)
    { if (!condition) throw new Exception(name); results.Add("PASS: " + name); }
    static void CheckLayouts(string root, List<string> results)
    {
        foreach(int mode in new[]{6,7}) foreach(var dimensions in new[]{new Size(1200,800),new Size(800,1200)})
        {
            var settings=new WatermarkSettings{LayoutMode=mode,PatternRadiusPercent=40,SizePercent=20};
            var slots=WatermarkLayouts.Create(dimensions,settings);
            Check(slots.Count==(mode==6?22:36),$"New mode {mode} count / {dimensions}",results);
            Check(slots.All(p=>p.Area.Left>=0&&p.Area.Top>=0&&p.Area.Right<=dimensions.Width&&p.Area.Bottom<=dimensions.Height),$"New mode {mode} inside photo",results);
            Check(slots.SelectMany((a,i)=>slots.Skip(i+1).Select(b=>a.Area.IntersectsWith(b.Area))).All(x=>!x),$"New mode {mode} cells do not overlap",results);
            using var input=new Bitmap(dimensions.Width,dimensions.Height);
            using var output=Imaging.Render(input,settings);
            Check(output.Size==dimensions,$"New mode {mode} preserves dimensions",results);
        }
        using var blank=new Bitmap(600,400);using(var g=Graphics.FromImage(blank))g.Clear(Color.Black);
        using var logo=new Bitmap(80,40);using(var g=Graphics.FromImage(logo))g.Clear(Color.White);
        var s=new WatermarkSettings{UseLogo=true,LogoPercent=10,Opacity=100,MarginPercent=5};
        Rectangle Bounds(Bitmap b, int threshold=100)
        {
            int left=b.Width,top=b.Height,right=-1,bottom=-1;
            for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)if(b.GetPixel(x,y).R>threshold){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
            return right<left?Rectangle.Empty:Rectangle.FromLTRB(left,top,right+1,bottom+1);
        }
        using(var five=Imaging.Render(blank,s,logo))
        {
            Point[] centers=[new(50,35),new(550,35),new(300,200),new(50,365),new(550,365)];
            Check(centers.All(p=>five.GetPixel(p.X,p.Y).R>150),"Five-point mode displays center and all four corners together",results);
            Check(CountComponents(five)==5,"Five-point mode renders exactly five separated marks",results);
            using var legacy=Imaging.Render(blank,s with{Position=0},logo);
            Check(Bounds(legacy)==Bounds(five),"Legacy individual-position setting no longer changes five-point pattern",results);
        }
        using(var rotated=Imaging.Render(blank,s with{Position=4,Rotation=-45},logo))
        {var b=Bounds(rotated);Check(b.Right<=581&&b.Bottom<=381&&b.Width>60&&b.Height>30,"Rotated logo remains inside margin",results);}
        using(var tiled=Imaging.Render(blank,s with{LayoutMode=1,TileSpacingPercent=34,LogoPercent=10},logo))
        {
            int count=0;for(int row=0;row<3;row++)for(int col=0;col<4;col++)if(tiled.GetPixel((int)((col+.5)*150),(int)((row+.5)*400/3)).R>150)count++;
            Check(count==12,"Uniform tiling covers all 12 evenly spaced cell centers",results);
        }
        using(var tiled=Imaging.Render(blank,s with{LayoutMode=2,Rotation=-25,TileSpacingPercent=34,LogoPercent=10},logo))
            Check(tiled.GetPixel(75,67).R>150&&tiled.GetPixel(525,333).R>150,"Diagonal tiling covers opposite corners",results);
        using(var full=Imaging.Render(blank,s with{LayoutMode=2,Rotation=-25},logo))
        using(var smallSource=new Bitmap(300,200))
        using(var smaller=Imaging.Render(smallSource,s with{LayoutMode=2,Rotation=-25},logo))
            Check(full.GetPixel(75,67).R>150&&smaller.GetPixel(37,33).R>150,"Tile layout scales consistently in preview and export",results);
        using(var text=Imaging.Render(blank,new WatermarkSettings{Text="模拟IC互助",LayoutMode=2,Rotation=-25,ColorArgb=Color.FromArgb(140,200,240).ToArgb(),Opacity=43,SizePercent=4}))
        {Check(Bounds(text,20).Width>400,"Chinese diagonal text fills multiple columns",results);text.Save(Path.Combine(root,"斜向平铺效果.png"));}
        var v11=new WatermarkSettings{LayoutMode=2,Rotation=-35,TileSpacingPercent=29,Position=8};
        var back=System.Text.Json.JsonSerializer.Deserialize<WatermarkSettings>(System.Text.Json.JsonSerializer.Serialize(v11))!;
        Check(back==v11,"New layout settings round-trip",results);
        var old=System.Text.Json.JsonSerializer.Deserialize<WatermarkSettings>("{\"Position\":4,\"Text\":\"legacy\"}")!;
        Check(old.LayoutMode==0&&old.Rotation==0,"Old point mode loads as new five-point pattern",results);
        foreach(var mode in new[]{3,4,5})
        {
            using var mark=Imaging.Render(blank,s with{LayoutMode=mode},logo);
            int expectedCount=mode==3?13:mode==4?8:5;
            Check(CountComponents(mark)==expectedCount,$"Pattern {mode} renders {expectedCount} separated marks",results);
            var b=Bounds(mark);Check(b.Left>0&&b.Top>0&&b.Right<600&&b.Bottom<400,$"Pattern {mode} stays inside photo",results);
        }
        using(var ring=Imaging.Render(blank,s with{LayoutMode=4},logo))
        {
            Check(ring.GetPixel(300,200).R==0,"Circular layout leaves the center empty",results);
            Check(ring.GetPixel(300,80).R>150&&ring.GetPixel(420,200).R>150,"Circular layout reaches uniform radius",results);
        }
        using(var dense=Imaging.Render(blank,s with{LayoutMode=4,PatternCount=16,PatternRadiusPercent=40,LogoPercent=60,Rotation=90},logo))
            Check(CountComponents(dense)==16,"Dense circular layout keeps large rotated marks separate",results);
        using(var scatter=Imaging.Render(blank,s with{LayoutMode=3,PatternCount=16,PatternRadiusPercent=15,LogoPercent=60},logo))
            Check(CountComponents(scatter)==25,"Dense center scatter remains separated",results);
        var newSettings=new WatermarkSettings{LayoutMode=4,PatternCount=12,PatternRadiusPercent=38};
        Check(System.Text.Json.JsonSerializer.Deserialize<WatermarkSettings>(System.Text.Json.JsonSerializer.Serialize(newSettings))==newSettings,"Radius and count settings round-trip",results);
        foreach(int mode in new[]{0,3,4,5})
        {
            using var portrait=new Bitmap(400,600);
            using var portraitMark=Imaging.Render(portrait,s with{LayoutMode=mode},logo);
            int count=mode==3?13:mode==4?8:5;
            Check(CountComponents(portraitMark)==count,$"Pattern {mode} supports portrait photos",results);
        }
    }
    static int CountComponents(Bitmap bitmap)
    {
        int width=bitmap.Width,height=bitmap.Height,count=0;var marked=new bool[width*height];var queue=new Queue<int>();
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)marked[y*width+x]=bitmap.GetPixel(x,y).R>100;
        for(int start=0;start<marked.Length;start++)
        {
            if(!marked[start])continue;count++;marked[start]=false;queue.Enqueue(start);
            while(queue.Count>0)
            {
                int p=queue.Dequeue(),x=p%width,y=p/width;
                foreach(int n in new[]{x>0?p-1:-1,x<width-1?p+1:-1,y>0?p-width:-1,y<height-1?p+width:-1})
                    if(n>=0&&marked[n]){marked[n]=false;queue.Enqueue(n);}
            }
        }
        return count;
    }
    sealed class InlineProgress(Action<BatchProgress> action) : IProgress<BatchProgress> { public void Report(BatchProgress value) => action(value); }
    static byte[] PngChunk(string kind, byte[] data)
    {
        byte[] typeAndData = Encoding.ASCII.GetBytes(kind).Concat(data).ToArray();
        uint crc = 0xFFFFFFFF;
        foreach (byte b in typeAndData)
        { crc ^= b; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u); }
        byte[] result = new byte[data.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)data.Length);
        typeAndData.CopyTo(result, 4); BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(result.Length - 4), ~crc);
        return result;
    }
    static void MakeDemo(string path)
    {
        using var image = new Bitmap(1500, 1000); using var g = Graphics.FromImage(image);
        using var sky = new System.Drawing.Drawing2D.LinearGradientBrush(new Point(0,0), new Point(0,1000), Color.FromArgb(204,225,225), Color.FromArgb(239,227,196));
        g.FillRectangle(sky, 0,0,1500,1000); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var sun = new SolidBrush(Color.FromArgb(244,236,204)); g.FillEllipse(sun, 1050,180,140,140);
        using var far = new SolidBrush(Color.FromArgb(114,151,150)); g.FillPolygon(far, [new(0,700),new(420,330),new(720,610),new(980,430),new(1500,750),new(1500,1000),new(0,1000)]);
        using var near = new SolidBrush(Color.FromArgb(40,91,93)); g.FillPolygon(near, [new(0,820),new(380,620),new(800,840),new(1220,560),new(1500,780),new(1500,1000),new(0,1000)]);
        image.Save(path, ImageFormat.Png);
    }
    static void MakeLayoutSheet(string root)
    {
        using var source=Imaging.Load(Path.Combine(root,"示例风景.png"),900);
        using var sheet=new Bitmap(1500,850);using var g=Graphics.FromImage(sheet);Imaging.Quality(g);g.Clear(Color.FromArgb(242,245,246));
        using var titleFont=new Font("Microsoft YaHei UI",20,FontStyle.Bold);using var labelFont=new Font("Microsoft YaHei UI",13,FontStyle.Bold);
        using var noteFont=new Font("Microsoft YaHei UI",10);using var ink=new SolidBrush(StudioTheme.Ink);using var muted=new SolidBrush(StudioTheme.Muted);
        g.DrawString("拾印 PhotoMark 1.2 · 六种分布模式",titleFont,ink,24,16);
        for(int mode=0;mode<6;mode++)
        {
            int x=mode%3*500,y=76+mode/3*370;
            g.DrawString(WatermarkLayouts.Names[mode],labelFont,ink,x+24,y);
            using var mark=Imaging.Render(source.Pixels,new WatermarkSettings{Text="模拟IC互助",ColorArgb=Color.FromArgb(140,200,240).ToArgb(),Opacity=75,SizePercent=5,LayoutMode=mode,Rotation=mode==2?-25:0});
            g.DrawImage(mark,new Rectangle(x+24,y+36,452,301));
        }
        g.DrawString("示例设置：浅蓝文字 / 字号 5% / 不透明度 75% · 图案半径、数量、角度可继续调节",noteFont,muted,24,819);
        sheet.Save(Path.Combine(root,"六种模式效果.png"));
    }
}
