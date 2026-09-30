using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MechCue;
public static class ChartTable
{
    static readonly string[] Header = ["TrackId", "Name", "Kind", "Axis", "Time_s", "Value", "Unit"];
    static readonly Dictionary<string,string> Kinds = new() { ["距離拘束"]="distance",["角度拘束"]="angle",["部品移動"]="translation",["部品回転"]="rotation",["部品座標"]="position" };
    static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static List<string[]> Rows(IEnumerable<Track> tracks)
    {
        var rows = new List<string[]> { Header };
        foreach (var track in tracks)
        {
            track.Validate();
            foreach (var point in track.Points) rows.Add([track.Id.ToString("D"), track.Name, Kinds[track.Kind], track.Axis, point.Time.ToString("R",CultureInfo.InvariantCulture), point.Value.ToString("R",CultureInfo.InvariantCulture), Plot.IsAngle(track) ? "deg" : "mm"]);
        }
        return rows;
    }
    public static void Write(string path, IEnumerable<Track> tracks)
    {
        string temp=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,Guid.NewGuid()+Path.GetExtension(path));
        try { WriteCore(temp,tracks); File.Move(temp,path,true); }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
    static void WriteCore(string path, IEnumerable<Track> tracks)
    {
        var rows = Rows(tracks);
        if(rows.Count>1048576)throw new InvalidDataException("Table exceeds the Excel row limit");
        if (Path.GetExtension(path).Equals(".xlsx",StringComparison.OrdinalIgnoreCase)) WriteExcel(path,rows);
        else if (Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase))
            File.WriteAllText(path,string.Join("\r\n",rows.Select(row=>string.Join(',',row.Select((cell,i)=>Quote(i==1 ? EscapeName(cell) : cell)))))+"\r\n",new UTF8Encoding(true));
        else throw new InvalidDataException("Use .csv or .xlsx");
    }
    static string EscapeName(string name) => name.TrimStart().StartsWith('=') || name.TrimStart().StartsWith('+') || name.TrimStart().StartsWith('-') || name.TrimStart().StartsWith('@') || name.StartsWith('\'') ? "'"+name : name;
    static string Quote(string cell) => "\""+cell.Replace("\"","\"\"")+"\"";
    public static List<Track> Read(string path)
    {
        if (new FileInfo(path).Length > 64*1024*1024) throw new InvalidDataException("Chart file exceeds 64 MB");
        bool excel = Path.GetExtension(path).Equals(".xlsx",StringComparison.OrdinalIgnoreCase);
        var rows = excel ? ReadExcel(path) : Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase) ? ParseCsv(File.ReadAllText(path, new UTF8Encoding(false,true))) : throw new InvalidDataException("Use .csv or .xlsx");
        if (rows.Count < 2) throw new InvalidDataException("No chart rows");
        var indexes = Header.Select(h=>Array.IndexOf(rows[0],h)).ToArray();
        if (indexes.Any(i=>i<0) || rows[0].Distinct().Count()!=rows[0].Length) throw new InvalidDataException("Required columns: "+string.Join(", ",Header));
        var tracks = new Dictionary<Guid,Track>();
        for (int r=1;r<rows.Count;r++)
        {
            var row=rows[r]; if (row.All(string.IsNullOrWhiteSpace)) continue;
            string Cell(int index) => indexes[index] < row.Length ? row[indexes[index]] : throw new InvalidDataException("Missing cell at row "+(r+1));
            if (!Guid.TryParse(Cell(0),out var id) || id==Guid.Empty) throw new InvalidDataException("Invalid TrackId at row "+(r+1));
            var kind=Kinds.SingleOrDefault(p=>p.Value==Cell(2)).Key;
            if (kind==null || Cell(3) is not ("X" or "Y" or "Z") || Cell(6)!=(kind is "角度拘束" or "部品回転" ? "deg" : "mm")) throw new InvalidDataException("Invalid Kind, Axis or Unit at row "+(r+1));
            var name=Cell(1); if (!excel && name.StartsWith('\'')) name=name[1..];
            if (!tracks.TryGetValue(id,out var track)) { track=new Track { Id=id, Name=name, Kind=kind, Axis=Cell(3), Points=[] }; tracks.Add(id,track); }
            if (track.Name!=name || track.Kind!=kind || track.Axis!=Cell(3)) throw new InvalidDataException("Track metadata differs at row "+(r+1));
            if (!double.TryParse(Cell(4),NumberStyles.Float,CultureInfo.InvariantCulture,out double time) || !double.TryParse(Cell(5),NumberStyles.Float,CultureInfo.InvariantCulture,out double value)) throw new InvalidDataException("Invalid number at row "+(r+1));
            track.Points.Add(new(time,value));
        }
        if (tracks.Count==0) throw new InvalidDataException("No chart tracks");
        foreach (var track in tracks.Values) { track.Points=track.Points.OrderBy(p=>p.Time).ToList(); track.Validate(); if(track.Points[^1].Time>100000) throw new InvalidDataException("Time exceeds 100000 seconds"); }
        return tracks.Values.ToList();
    }
    internal static List<string[]> ParseCsv(string text)
    {
        var result=new List<string[]>(); var row=new List<string>(); var cell=new StringBuilder(); bool quoted=false, afterQuote=false;
        for(int i=0;i<text.Length;i++)
        {
            char c=text[i];
            if(quoted) { if(c=='"') { if(i+1<text.Length && text[i+1]=='"') { cell.Append('"');i++; } else { quoted=false;afterQuote=true; } } else cell.Append(c); continue; }
            if(c=='"' && cell.Length==0 && !afterQuote) { quoted=true;continue; }
            if(c==',' || c=='\r' || c=='\n') { row.Add(cell.ToString());cell.Clear();afterQuote=false; if(c!=',') { result.Add(row.ToArray());row.Clear();if(c=='\r' && i+1<text.Length && text[i+1]=='\n')i++; } continue; }
            if(afterQuote || c=='"') throw new InvalidDataException("Invalid CSV quoting");
            cell.Append(c);
        }
        if(quoted) throw new InvalidDataException("Unclosed CSV quote");
        if(cell.Length>0 || row.Count>0 || afterQuote) { row.Add(cell.ToString());result.Add(row.ToArray()); }
        return result;
    }
    static XDocument Xml(ZipArchive zip,string name)
    {
        var entry=zip.GetEntry(name) ?? throw new InvalidDataException("Missing Excel part: "+name);
        if(entry.Length>32*1024*1024) throw new InvalidDataException("Excel part exceeds 32 MB");
        using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024 });return XDocument.Load(reader);
    }
    static List<string[]> ReadExcel(string path)
    {
        using var zip=ZipFile.OpenRead(path);
        if(zip.Entries.Sum(e=>e.Length)>64*1024*1024)throw new InvalidDataException("Uncompressed workbook exceeds 64 MB");
        var workbook=Xml(zip,"xl/workbook.xml");var sheets=workbook.Descendants(S+"sheet").ToList();
        var sheet=sheets.FirstOrDefault(e=>(string?)e.Attribute("name")=="MechCue") ?? sheets.FirstOrDefault() ?? throw new InvalidDataException("No worksheet");
        string relationship=(string?)sheet.Attribute(R+"id") ?? throw new InvalidDataException("No worksheet relationship");
        var link=Xml(zip,"xl/_rels/workbook.xml.rels").Root!.Elements().Single(e=>(string?)e.Attribute("Id")==relationship);
        var target=(string?)link.Attribute("Target") ?? throw new InvalidDataException("No worksheet target");
        if((string?)link.Attribute("TargetMode")=="External" || target.Contains(".."))throw new InvalidDataException("Unsupported worksheet relationship");
        string sheetPath=target.StartsWith('/') ? target.TrimStart('/') : "xl/"+target;
        var shared=zip.GetEntry("xl/sharedStrings.xml")!=null ? Xml(zip,"xl/sharedStrings.xml").Descendants(S+"si").Select(e=>string.Concat(e.Descendants(S+"t").Select(t=>t.Value))).ToArray() : [];
        var result=new List<string[]>();
        foreach(var row in Xml(zip,sheetPath).Descendants(S+"row"))
        {
            var cells=new Dictionary<int,string>();int next=0;
            foreach(var cell in row.Elements(S+"c"))
            {
                var reference=(string?)cell.Attribute("r");int column=next;
                if(reference!=null){ column=0;foreach(char c in reference.TakeWhile(char.IsLetter))column=column*26+(char.ToUpperInvariant(c)-'A'+1);column--; }
                if(column<0 || column>255) throw new InvalidDataException("Invalid worksheet column");
                string type=(string?)cell.Attribute("t") ?? "n";
                string value=type=="inlineStr" ? string.Concat(cell.Descendants(S+"t").Select(t=>t.Value)) : cell.Element(S+"v")?.Value ?? "";
                if(type=="s") { if(!int.TryParse(value,out int index) || index<0 || index>=shared.Length)throw new InvalidDataException("Invalid shared string");value=shared[index]; }
                if(type=="e" || (cell.Element(S+"f")!=null && string.IsNullOrEmpty(value)))throw new InvalidDataException("Formula error or missing calculated value. Recalculate and save in Excel.");
                if(!cells.TryAdd(column,value))throw new InvalidDataException("Duplicate worksheet cell");next=column+1;
            }
            if(cells.Count>0)result.Add(Enumerable.Range(0,cells.Keys.Max()+1).Select(i=>cells.GetValueOrDefault(i,"")).ToArray());
        }
        return result;
    }
    static void WriteExcel(string path,List<string[]> rows)
    {
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        void Put(string name,XDocument doc) { using var stream=zip.CreateEntry(name).Open();doc.Save(stream); }
        XNamespace content="http://schemas.openxmlformats.org/package/2006/content-types", rel="http://schemas.openxmlformats.org/package/2006/relationships";
        Put("[Content_Types].xml",new(new XElement(content+"Types",new XElement(content+"Default",new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),new XElement(content+"Default",new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")),new XElement(content+"Override",new XAttribute("PartName","/xl/workbook.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),new XElement(content+"Override",new XAttribute("PartName","/xl/worksheets/sheet1.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
        Put("_rels/.rels",new(new XElement(rel+"Relationships",new XElement(rel+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type",R.NamespaceName+"/officeDocument"),new XAttribute("Target","xl/workbook.xml")))));
        Put("xl/workbook.xml",new(new XElement(S+"workbook",new XAttribute(XNamespace.Xmlns+"r",R),new XElement(S+"sheets",new XElement(S+"sheet",new XAttribute("name","MechCue"),new XAttribute("sheetId",1),new XAttribute(R+"id","rId1"))))));
        Put("xl/_rels/workbook.xml.rels",new(new XElement(rel+"Relationships",new XElement(rel+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type",R.NamespaceName+"/worksheet"),new XAttribute("Target","worksheets/sheet1.xml")))));
        var data=new XElement(S+"sheetData");
        for(int r=0;r<rows.Count;r++)
        {
            var row=new XElement(S+"row",new XAttribute("r",r+1));
            for(int c=0;c<Header.Length;c++)
            {
                string value=rows[r][c];var cell=new XElement(S+"c",new XAttribute("r",((char)('A'+c)).ToString()+(r+1)));
                if(r>0 && c is 4 or 5)cell.Add(new XElement(S+"v",value));
                else {cell.Add(new XAttribute("t","inlineStr"),new XElement(S+"is",new XElement(S+"t",new XAttribute(XNamespace.Xml+"space","preserve"),value)));}
                row.Add(cell);
            }
            data.Add(row);
        }
        Put("xl/worksheets/sheet1.xml",new(new XElement(S+"worksheet",new XElement(S+"sheetViews",new XElement(S+"sheetView",new XAttribute("workbookViewId",0),new XElement(S+"pane",new XAttribute("ySplit",1),new XAttribute("topLeftCell","A2"),new XAttribute("activePane","bottomLeft"),new XAttribute("state","frozen")))),new XElement(S+"cols",new XElement(S+"col",new XAttribute("min",1),new XAttribute("max",1),new XAttribute("width",38),new XAttribute("customWidth",1)),new XElement(S+"col",new XAttribute("min",2),new XAttribute("max",7),new XAttribute("width",20),new XAttribute("customWidth",1))),data,new XElement(S+"autoFilter",new XAttribute("ref","A1:G"+rows.Count)))));
    }
}
