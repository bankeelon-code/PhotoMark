namespace PhotoMark;

internal readonly record struct WatermarkSlot(RectangleF Area, float Angle, int AlignX = 0, int AlignY = 0);

internal static class WatermarkLayouts
{
    public static readonly string[] Names = ["五点分布", "均匀平铺", "斜向平铺", "中心散射", "圆形环绕", "十字分布", "螺旋", "多层圆环"];

    public static List<WatermarkSlot> Create(Size image, WatermarkSettings settings)
    {
        var s = settings.Normalized();
        float w = image.Width, h = image.Height, shortEdge = Math.Min(w,h);
        var slots = new List<WatermarkSlot>();
        if (s.LayoutMode == 0)
        {
            float margin = shortEdge * s.MarginPercent / 100f;
            float areaW = Math.Max(1,w-2*margin), areaH = Math.Max(1,h-2*margin);
            float slotW = areaW*.30f, slotH = areaH*.30f;
            // Five disjoint regions: all four corners and the center, simultaneously.
            slots.Add(new(new(margin,margin,slotW,slotH),s.Rotation,-1,-1));
            slots.Add(new(new(w-margin-slotW,margin,slotW,slotH),s.Rotation,1,-1));
            slots.Add(new(new((w-slotW)/2,(h-slotH)/2,slotW,slotH),s.Rotation));
            slots.Add(new(new(margin,h-margin-slotH,slotW,slotH),s.Rotation,-1,1));
            slots.Add(new(new(w-margin-slotW,h-margin-slotH,slotW,slotH),s.Rotation,1,1));
            return slots;
        }
        if (s.LayoutMode is 1 or 2)
        {
            int cols = Math.Clamp((int)Math.Round(w/(shortEdge*s.TileSpacingPercent/100f)),1,64);
            int rows = Math.Clamp((int)Math.Round(h/(shortEdge*s.TileSpacingPercent/100f)),1,64);
            float cellW=w/cols, cellH=h/rows;
            for(int row=0;row<rows;row++) for(int col=0;col<cols;col++)
                slots.Add(new(new(col*cellW+cellW*.09f,row*cellH+cellH*.09f,cellW*.82f,cellH*.82f),s.Rotation));
            return slots;
        }

        float radius=shortEdge*s.PatternRadiusPercent/100f;
        float cx=w/2, cy=h/2;
        void Place(float x,float y,float side,float angle)
        {
            // Keep marks centered on the intended path, even near an image edge.
            float room=2*Math.Min(Math.Min(x-shortEdge*.02f,w-shortEdge*.02f-x),Math.Min(y-shortEdge*.02f,h-shortEdge*.02f-y));
            float length=Math.Max(.1f,Math.Min(side,room));
            slots.Add(new(new(x-length/2,y-length/2,length,length),angle));
        }
        void Ring(int count,float ringRadius,float offset,float side,bool tangent)
        {
            for(int i=0;i<count;i++)
            {
                float degrees=-90+360f*i/count+offset;
                double radians=degrees*Math.PI/180;
                float angle=degrees+(tangent?90:0);
                // Keep words readable rather than upside down on the lower/left arcs.
                while(angle>90)angle-=180;
                while(angle< -90)angle+=180;
                Place(cx+(float)Math.Cos(radians)*ringRadius,cy+(float)Math.Sin(radians)*ringRadius,side,angle+s.Rotation);
            }
        }
        if(s.LayoutMode==3)
        {
            int innerCount=Math.Max(3,s.PatternCount/2);
            float innerRadius=radius*.48f;
            float centerClearance=Enumerable.Range(0,innerCount).Select(i=>
            {
                double a=(-90+360.0*i/innerCount)*Math.PI/180;
                return (float)Math.Max(Math.Abs(Math.Cos(a)),Math.Abs(Math.Sin(a)))*innerRadius*.9f;
            }).Min();
            float side=Math.Min(centerClearance,Math.Min(2*innerRadius*(float)Math.Sin(Math.PI/innerCount)*.64f,radius*.46f));
            Place(cx,cy,side,s.Rotation);
            Ring(innerCount,innerRadius,0,side,false);
            Ring(s.PatternCount,radius,0,side,false);
        }
        else if(s.LayoutMode==4)
        {
            float side=2*radius*(float)Math.Sin(Math.PI/s.PatternCount)*.64f;
            Ring(s.PatternCount,radius,0,side,true);
        }
        else if(s.LayoutMode==6)
        {
            // Sample an Archimedean spiral, then fit disjoint square cells to neighbours.
            var points=new List<PointF>();
            for(int i=0;i<22;i++)
            {
                float t=i/21f, r=radius*(.12f+.88f*t);
                double a=(-90+720*t)*Math.PI/180;
                points.Add(new(cx+r*(float)Math.Cos(a),cy+r*(float)Math.Sin(a)));
            }
            for(int i=0;i<points.Count;i++)
            {
                var p=points[i];
                float nearest=points.Where((_,j)=>j!=i).Min(q=>Math.Max(Math.Abs(q.X-p.X),Math.Abs(q.Y-p.Y)));
                Place(p.X,p.Y,nearest*.76f,0);
            }
        }
        else if(s.LayoutMode==7)
        {
            Ring(6,radius*.32f,0,radius*.19f,true);
            Ring(12,radius*.66f,15,radius*.19f,true);
            Ring(18,radius,0,radius*.19f,true);
        }
        else
        {
            float side=radius*.62f;
            Place(cx,cy,side,s.Rotation);
            Place(cx-radius,cy,side,s.Rotation);
            Place(cx+radius,cy,side,s.Rotation);
            Place(cx,cy-radius,side,s.Rotation);
            Place(cx,cy+radius,side,s.Rotation);
        }
        return slots;
    }
}
