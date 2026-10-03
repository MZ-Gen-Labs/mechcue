namespace MechCue;
static class CommandIcons
{
    public static Image? Create(string command)
    {
        if (command is not ("Solid Edgeに接続" or "切断" or "開く" or "保存" or "▶ 再生" or "一時停止" or "停止" or "元に戻す" or "やり直す" or "最小表示" or "編集画面へ戻る" or "基準状態に戻す")) return null;
        var bitmap = new Bitmap(18, 18);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(35, 100, 170), 1.8f);
        using var brush = new SolidBrush(Color.FromArgb(35, 100, 170));
        switch (command)
        {
            case "▶ 再生": g.FillPolygon(brush, new Point[] {new(5,3),new(15,9),new(5,15)}); break;
            case "一時停止": g.FillRectangle(brush, 4,3,4,12); g.FillRectangle(brush, 11,3,4,12); break;
            case "停止": g.FillRectangle(brush, 4,4,10,10); break;
            case "保存": g.DrawRectangle(pen,3,2,12,14); g.DrawRectangle(pen,5,2,7,5); g.DrawRectangle(pen,5,10,8,6); break;
            case "開く": g.DrawLines(pen,new Point[] {new(2,14),new(2,4),new(7,4),new(9,6),new(15,6)}); g.DrawPolygon(pen,new Point[] {new(2,14),new(5,7),new(16,7),new(13,14)}); break;
            case "Solid Edgeに接続": case "切断":
                g.DrawLine(pen,6,2,6,6);g.DrawLine(pen,11,2,11,6);g.DrawRectangle(pen,4,6,9,5);g.DrawLine(pen,8,11,8,16);
                if (command == "切断") { using var red = new Pen(Color.Firebrick,2);g.DrawLine(red,2,15,16,2); } break;
            case "元に戻す": case "基準状態に戻す":
                g.DrawArc(pen,4,5,11,10,190,270);g.DrawLines(pen,new Point[] {new(2,8),new(5,4),new(9,7)});break;
            case "やり直す":
                g.DrawArc(pen,3,5,11,10,80,270);g.DrawLines(pen,new Point[] {new(9,7),new(13,4),new(16,8)});break;
            default:
                g.DrawRectangle(pen,2,3,14,12);g.DrawLine(pen,2,7,16,7);
                if(command=="最小表示")g.DrawRectangle(pen,7,9,7,4);else g.DrawLine(pen,7,7,7,15);break;
        }
        return bitmap;
    }
}
