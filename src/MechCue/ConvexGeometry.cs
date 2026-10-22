namespace MechCue;

public static class ConvexGeometry
{
    public static void ValidateVertices(double[][]? points)
    {
        if (points == null || points.Length is < 4 or > 256 || points.Any(p => p == null || p.Length != 3 || p.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e6)))
            throw new ArgumentException("A convex body needs 4..256 finite 3D vertices within coordinate limits.");
        double[] Sub(double[] a, double[] b) => a.Zip(b).Select(p => p.First - p.Second).ToArray();
        double Norm(double[] a) => Math.Sqrt(a.Sum(v => v * v));
        var a = points[0]; var b = points.MaxBy(p => Norm(Sub(p, a)))!; var ab = Sub(b, a); double length = Norm(ab);
        if (length < 1e-8) throw new ArgumentException("Convex vertices collapse to a point.");
        double[] Cross(double[] u, double[] v) => [u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]];
        var normal = points.Select(p => Cross(ab, Sub(p, a))).MaxBy(Norm)!; double area = Norm(normal);
        if (area < length * length * 1e-8 || points.Max(p => Math.Abs(Sub(p, a).Zip(normal).Sum(v => v.First * v.Second))) < area * length * 1e-8)
            throw new ArgumentException("Convex vertices must enclose a nonzero 3D volume.");
    }

    public static void ValidateSupportingPlane(double[][] points, double[] origin, double[] normal, double tolerance = 1e-7)
    {
        double length = Math.Sqrt(normal.Sum(v => v * v));
        if (origin.Length != 3 || normal.Length != 3 || origin.Concat(normal).Any(v => !double.IsFinite(v)) || length < 1e-12) throw new ArgumentException("Invalid face plane.");
        var distances = points.Select(p => Enumerable.Range(0, 3).Sum(k => (p[k]-origin[k])*normal[k])/length).ToArray();
        if (distances.Min() < -tolerance && distances.Max() > tolerance) throw new ArgumentException("Concave or hollow part: a face plane cuts through its vertices. Convex decomposition is required.");
    }
}
