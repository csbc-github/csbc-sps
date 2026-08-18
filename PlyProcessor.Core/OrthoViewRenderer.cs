using System.Drawing;
using System.Drawing.Imaging;

namespace PlyProcessor.Core;

public static class OrthoViewRenderer
{
    public enum View { Top, Front, Side }

    public static byte[] Render(PlyPoint[] pts, View view, int size = 700)
    {
        int axH = view switch { View.Top => 0, View.Front => 0, _ => 1 };
        int axV = view switch { View.Top => 1, View.Front => 2, _ => 2 };
        string lH = view switch { View.Top => "X", View.Front => "X", _ => "Y" };
        string lV = view switch { View.Top => "Y", View.Front => "Z", _ => "Z" };

        float[] hs = new float[pts.Length], vs = new float[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            float[] v = [pts[i].X, pts[i].Y, pts[i].Z];
            hs[i] = v[axH]; vs[i] = v[axV];
        }
        float minH=hs.Min(), maxH=hs.Max(), minV=vs.Min(), maxV=vs.Max();
        float spanH=Math.Max(maxH-minH,1), spanV=Math.Max(maxV-minV,1);
        const int PAD = 52;
        float scale = Math.Min((size-2*PAD)/spanH, (size-2*PAD)/spanV);
        float drawW=spanH*scale, drawHpx=spanV*scale;
        float offH=(size-drawW)/2, offV=(size-drawHpx)/2;

        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(0x0d, 0x11, 0x17));
        using var borderPen = new Pen(Color.FromArgb(40,255,255,255));
        g.DrawRectangle(borderPen, offH, offV, drawW, drawHpx);

        var ptColor = Color.FromArgb(0x0e, 0xa5, 0xc8);
        for (int i = 0; i < pts.Length; i++)
        {
            int px = (int)((hs[i]-minH)*scale+offH);
            int py = size - 1 - (int)((vs[i]-minV)*scale+offV);
            if (px>=0&&px<size&&py>=0&&py<size)
                bmp.SetPixel(px, py, ptColor);
        }

        using var font = new Font("Courier New", 10);
        using var brush = new SolidBrush(Color.FromArgb(0xae, 0xb7, 0xbd));
        g.DrawString($"{lH}: {spanH:F1} mm", font, brush, size/2-40, offV+drawHpx+6);
        g.DrawString($"{lV}: {spanV:F1} mm", font, brush, offH-44, size/2-8);

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
