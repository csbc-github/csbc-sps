namespace PlyProcessor.Core;

public record struct PcaPlane(float Cx, float Cy, float Cz, float[] E1, float[] E2, float[] E3);

public static class PcaFitter
{
    public static PcaPlane Fit(PlyPoint[] pts)
    {
        double cx = 0, cy = 0, cz = 0;
        foreach (var p in pts) { cx += p.X; cy += p.Y; cz += p.Z; }
        cx /= pts.Length; cy /= pts.Length; cz /= pts.Length;

        double[,] cov = new double[3, 3];
        foreach (var p in pts)
        {
            double dx = p.X - cx, dy = p.Y - cy, dz = p.Z - cz;
            cov[0,0]+=dx*dx; cov[0,1]+=dx*dy; cov[0,2]+=dx*dz;
            cov[1,0]+=dy*dx; cov[1,1]+=dy*dy; cov[1,2]+=dy*dz;
            cov[2,0]+=dz*dx; cov[2,1]+=dz*dy; cov[2,2]+=dz*dz;
        }
        double n = pts.Length;
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) cov[i,j] /= n;

        var e3 = PowerIterateSmallest(cov);
        var e1 = GramSchmidt(new double[]{1,0,0}, e3);
        if (Norm(e1) < 1e-9) e1 = GramSchmidt(new double[]{0,1,0}, e3);
        e1 = Normalize(e1);
        var e2 = Cross(e3, e1);

        return new PcaPlane((float)cx,(float)cy,(float)cz,
            ToF(e1), ToF(e2), ToF(e3));
    }

    /// <summary>
    /// Full PCA: returns centroid + all 3 eigenvectors in eigenvalue order
    /// (EMax = max variance = steel length axis, EMid = medium, EMin = min = surface normal).
    /// Each eigenvector is computed independently against the ORIGINAL covariance matrix
    /// to avoid double-deflation errors.
    /// </summary>
    public static (float[] Cent, float[] EMax, float[] EMid, float[] EMin) FitFull(PlyPoint[] pts)
    {
        double cx = 0, cy = 0, cz = 0;
        foreach (var p in pts) { cx += p.X; cy += p.Y; cz += p.Z; }
        cx /= pts.Length; cy /= pts.Length; cz /= pts.Length;

        double[,] cov = new double[3, 3];
        foreach (var p in pts)
        {
            double dx = p.X - cx, dy = p.Y - cy, dz = p.Z - cz;
            cov[0,0]+=dx*dx; cov[0,1]+=dx*dy; cov[0,2]+=dx*dz;
            cov[1,0]+=dy*dx; cov[1,1]+=dy*dy; cov[1,2]+=dy*dz;
            cov[2,0]+=dz*dx; cov[2,1]+=dz*dy; cov[2,2]+=dz*dz;
        }
        double n = pts.Length;
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) cov[i,j] /= n;

        // eMin from the original matrix (already correct in Fit())
        var eMin = PowerIterateSmallest(cov);
        double lamMin = Dot(eMin, Mul(cov, eMin));
        // Deflate minimum, then find max of what remains = eMax
        var deflMin = DeflateMatrix(cov, eMin, lamMin);
        var eMax = PowerIterateMax(deflMin);   // max of (cov - lamMin * eMin * eMin^T) = true eMax
        // eMid orthogonal to both
        var eMid = Normalize(Cross(eMin, eMax));

        float[] cent = [(float)cx, (float)cy, (float)cz];
        return (cent, ToF(eMax), ToF(eMid), ToF(eMin));
    }

    static double[] PowerIterateMax(double[,] cov)
    {
        double[] v = { 1/Math.Sqrt(3), 1/Math.Sqrt(3), 1/Math.Sqrt(3) };
        for (int iter = 0; iter < 100; iter++) v = Normalize(Mul(cov, v));
        return Normalize(v);
    }

    public static PlyPoint[] RemoveDominantPlane(PlyPoint[] pts, float planeTol)
    {
        var plane = Fit(pts);
        var heights = new float[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            double dx=pts[i].X-plane.Cx, dy=pts[i].Y-plane.Cy, dz=pts[i].Z-plane.Cz;
            heights[i] = (float)(dx*plane.E3[0]+dy*plane.E3[1]+dz*plane.E3[2]);
        }
        float hMin=float.MaxValue, hMax=float.MinValue;
        foreach (var h in heights) { if(h<hMin)hMin=h; if(h>hMax)hMax=h; }

        const int nBins = 80;
        float binW = (hMax - hMin) / nBins;
        if (binW < 1e-6f) return pts;
        var bins = new int[nBins];
        foreach (var h in heights) bins[Math.Min(nBins-1,(int)((h-hMin)/binW))]++;
        int peakBin = 0;
        for (int b = 1; b < nBins; b++) if (bins[b] > bins[peakBin]) peakBin = b;
        float hPeak = hMin + (peakBin + 0.5f) * binW;

        return [.. pts.Where((p, i) => MathF.Abs(heights[i] - hPeak) > planeTol)];
    }

    static double[] PowerIterateSmallest(double[,] cov)
    {
        // Find eigenvector for the smallest eigenvalue via inverse-free deflation approach:
        // Get all three via power iteration on (maxLam*I - cov) and deflate.
        double[] v = {1/Math.Sqrt(3),1/Math.Sqrt(3),1/Math.Sqrt(3)};
        for (int iter = 0; iter < 60; iter++) v = Normalize(Mul(cov,v));
        double lam1 = Dot(v,Mul(cov,v));
        var deflated = DeflateMatrix(cov, v, lam1);

        double[] v2 = {-1/Math.Sqrt(2),1/Math.Sqrt(2),0};
        for (int iter = 0; iter < 60; iter++) v2 = Normalize(Mul(deflated,v2));
        double lam2 = Dot(v2,Mul(cov,v2));
        var deflated2 = DeflateMatrix(deflated, v2, lam2);

        double[] v3 = {0.5,-0.5,1/Math.Sqrt(2)};
        for (int iter = 0; iter < 60; iter++) v3 = Normalize(Mul(deflated2,v3));
        return Normalize(v3);
    }

    static double[,] DeflateMatrix(double[,] m, double[] v, double lam)
    {
        var r = (double[,])m.Clone();
        for (int i=0;i<3;i++) for (int j=0;j<3;j++) r[i,j]-=lam*v[i]*v[j];
        return r;
    }

    static double[] Mul(double[,] m, double[] v) => [
        m[0,0]*v[0]+m[0,1]*v[1]+m[0,2]*v[2],
        m[1,0]*v[0]+m[1,1]*v[1]+m[1,2]*v[2],
        m[2,0]*v[0]+m[2,1]*v[1]+m[2,2]*v[2]];

    static double Dot(double[] a, double[] b) => a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
    static double Norm(double[] v) => Math.Sqrt(Dot(v,v));
    static double[] Normalize(double[] v) { double n=Norm(v); return n<1e-12?v:[v[0]/n,v[1]/n,v[2]/n]; }
    static double[] Cross(double[] a, double[] b) => [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
    static double[] GramSchmidt(double[] v, double[] u) { double d=Dot(v,u); return [v[0]-d*u[0],v[1]-d*u[1],v[2]-d*u[2]]; }
    static float[] ToF(double[] v) => [(float)v[0],(float)v[1],(float)v[2]];
}
