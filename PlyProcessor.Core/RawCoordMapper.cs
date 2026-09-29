namespace PlyProcessor.Core;

// 目前沒有任何呼叫。舊 Python raw↔processed 對應用（含 zThresh 濾點）。
// 現在座標 JSON 由 Pipeline.Export 直接寫 raw／processed 點，不經此對應。
#if false

/// <summary>
/// Derives the raw→processed affine transform by intensity-run matching,
/// then builds a complete output-row → raw-row index mapping.
/// </summary>
public static class RawCoordMapper
{
    public record AffineTransform(double[] Cx, double[] Cy, double[] Cz);

    public static AffineTransform? FitTransform(PlyPoint[] rawPts, PlyPoint[] outPts,
        int sampleCount = 20000)
    {
        var A = new List<double[]>(); var bx=new List<double>(); var by=new List<double>(); var bz=new List<double>();
        int r=0, o=0, run=0;
        int limit = Math.Min(rawPts.Length, Math.Min(outPts.Length, sampleCount));
        while (r < rawPts.Length && o < outPts.Length && bx.Count < limit)
        {
            if (rawPts[r].Intensity == outPts[o].Intensity)
            {
                if (++run >= 3)
                {
                    A.Add([rawPts[r].X, rawPts[r].Y, rawPts[r].Z, 1.0]);
                    bx.Add(outPts[o].X); by.Add(outPts[o].Y); bz.Add(outPts[o].Z);
                }
                r++; o++;
            }
            else { r++; run = 0; }
        }
        if (A.Count < 20) return null;
        return new AffineTransform(SolveAffine4(A, bx), SolveAffine4(A, by), SolveAffine4(A, bz));
    }

    public static int[] BuildMapping(PlyPoint[] rawPts, PlyPoint[] outPts,
        AffineTransform tf, float zThresh, float tol = 0.05f)
    {
        // Forward-transform every raw point, keep those above zThresh, then two-pointer match.
        var keptIdx = new List<int>(outPts.Length);
        var keptX = new List<float>(outPts.Length);
        var keptY = new List<float>(outPts.Length);
        var keptZ = new List<float>(outPts.Length);

        for (int i = 0; i < rawPts.Length; i++)
        {
            float px = Apply(rawPts[i], tf.Cx);
            float py = Apply(rawPts[i], tf.Cy);
            float pz = Apply(rawPts[i], tf.Cz);
            if (pz > zThresh) { keptIdx.Add(i); keptX.Add(px); keptY.Add(py); keptZ.Add(pz); }
        }

        var mapping = new int[outPts.Length];
        Array.Fill(mapping, -1);
        int ki = 0, oi = 0;
        while (ki < keptIdx.Count && oi < outPts.Length)
        {
            if (Math.Abs(keptX[ki]-outPts[oi].X) <= tol &&
                Math.Abs(keptY[ki]-outPts[oi].Y) <= tol &&
                Math.Abs(keptZ[ki]-outPts[oi].Z) <= tol)
                mapping[oi++] = keptIdx[ki++];
            else
                ki++;
        }
        return mapping;
    }

    static float Apply(PlyPoint p, double[] c) =>
        (float)(c[0]*p.X + c[1]*p.Y + c[2]*p.Z + c[3]);

    static double[] SolveAffine4(List<double[]> A, List<double> b)
    {
        // Normal equations: (A^T A) x = A^T b, solved by 4×4 Gaussian elimination
        var ATA = new double[4,5];
        for (int i = 0; i < A.Count; i++)
            for (int r = 0; r < 4; r++)
            {
                ATA[r,4] += A[i][r]*b[i];
                for (int c=0;c<4;c++) ATA[r,c] += A[i][r]*A[i][c];
            }
        for (int col=0;col<4;col++)
        {
            int piv=col;
            for (int row=col+1;row<4;row++)
                if (Math.Abs(ATA[row,col])>Math.Abs(ATA[piv,col])) piv=row;
            if (piv!=col)
                for (int c=0;c<=4;c++) (ATA[col,c], ATA[piv,c]) = (ATA[piv,c], ATA[col,c]);
            double d=ATA[col,col];
            if (Math.Abs(d)<1e-12) continue;
            for (int c=col;c<=4;c++) ATA[col,c]/=d;
            for (int row=0;row<4;row++)
            {
                if (row==col) continue;
                double f=ATA[row,col];
                for (int c=col;c<=4;c++) ATA[row,c]-=f*ATA[col,c];
            }
        }
        return [ATA[0,4], ATA[1,4], ATA[2,4], ATA[3,4]];
    }
}
#endif
