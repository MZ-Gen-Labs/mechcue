namespace MechCue;

public record KeyPoint(double Time, double Value);
public class Track
{
    public string Name { get; set; } = "軸";
    public string Kind { get; set; } = "距離拘束";
    public string Axis { get; set; } = "X";
    public List<KeyPoint> Points { get; set; } = [new(0, 0), new(2, 100), new(4, 0)];
    public void MovePoint(int index, double time, double value, double end)
    {
        if (!double.IsFinite(time) || !double.IsFinite(value)) throw new ArgumentException("時間と変位は有限の数値で指定してください。");
        double lower = index == 0 ? 0 : Math.BitIncrement(Points[index - 1].Time);
        double upper = index == Points.Count - 1 ? end : Math.BitDecrement(Points[index + 1].Time);
        Points[index] = new KeyPoint(Math.Clamp(time, lower, upper), value);
    }
    public void ShiftSegment(int index, double delta)
    {
        if (index < 0 || index >= Points.Count - 1) throw new ArgumentOutOfRangeException(nameof(index));
        var start = Points[index]; var end = Points[index + 1];
        if (!double.IsFinite(delta) || !double.IsFinite(start.Value + delta) || !double.IsFinite(end.Value + delta))
            throw new ArgumentException("移動量は有限の数値で指定してください。");
        Points[index] = start with { Value = start.Value + delta };
        Points[index + 1] = end with { Value = end.Value + delta };
    }
    public double At(double time)
    {
        Validate();
        if (time <= Points[0].Time) return Points[0].Value;
        for (int i = 1; i < Points.Count; i++)
            if (time <= Points[i].Time)
                return Points[i - 1].Value + (Points[i].Value - Points[i - 1].Value) *
                    (time - Points[i - 1].Time) / (Points[i].Time - Points[i - 1].Time);
        return Points[^1].Value;
    }
    public void Validate()
    {
        if (Points.Count < 2 || Points.Any(p => !double.IsFinite(p.Time) || !double.IsFinite(p.Value) || p.Time < 0))
            throw new InvalidOperationException("時間と変位には有限の数値を入力し、2点以上指定してください。");
        for (int i = 1; i < Points.Count; i++)
            if (Points[i].Time <= Points[i - 1].Time) throw new InvalidOperationException("時間は重複せず、小さい順に指定してください。");
    }
}

public static class Transform
{
    // Solid Edge: translation occupies elements 12..14 (zero based).
    public static double[] Apply(double[] basis, string kind, string axis, double value)
    {
        var m = (double[])basis.Clone();
        int a = axis == "X" ? 0 : axis == "Y" ? 1 : 2;
        if (kind == "部品座標") { m[12 + a] = value / 1000; return m; }
        if (kind == "部品移動") { m[12 + a] += value / 1000; return m; }
        double c = Math.Cos(value * Math.PI / 180), s = Math.Sin(value * Math.PI / 180);
        int u = (a + 1) % 3, v = (a + 2) % 3;
        for (int row = 0; row < 3; row++)
        {
            m[row * 4 + u] = basis[row * 4 + u] * c - basis[row * 4 + v] * s;
            m[row * 4 + v] = basis[row * 4 + u] * s + basis[row * 4 + v] * c;
        }
        return m;
    }
}
