using System.IO.Compression;
using System.Xml.Linq;
namespace MechCue;
public static partial class SelfTest
{
    static void TestChartTables()
    {
        string directory=Path.Combine(AppContext.BaseDirectory,"table-tests");Directory.CreateDirectory(directory);
        void Assert(bool value,string message) { if(!value)throw new Exception(message); }
        var tracks=new List<Track> {
            new() { Name="機構,\"A\"\n搬送",Axis="Y",Points=[new(0,215.89999999999995),new(1.25,-12.5)] },
            new() { Name="=1+1",Kind="角度拘束",Points=[new(0,0),new(2,90)] },
            new() { Name="'literal",Kind="部品座標",Axis="Z",Points=[new(0,123),new(3,150)] }
        };
        foreach(string extension in new[]{"csv","xlsx"})
        {
            string file=Path.Combine(directory,"roundtrip."+extension);ChartTable.Write(file,tracks);var read=ChartTable.Read(file);
            Assert(read.Count==tracks.Count,"Table track count");
            for(int i=0;i<tracks.Count;i++)Assert(read[i].Id==tracks[i].Id && read[i].Name==tracks[i].Name && read[i].Kind==tracks[i].Kind && read[i].Axis==tracks[i].Axis && read[i].Points.SequenceEqual(tracks[i].Points),"Lossless "+extension+" roundtrip");
        }
        string csv=File.ReadAllText(Path.Combine(directory,"roundtrip.csv"));
        Assert(csv.Contains("\"'=1+1\""),"CSV name formula escaping");
        void Reject(string name,string content)
        {
            string file=Path.Combine(directory,name+".csv");File.WriteAllText(file,content);
            bool rejected=false;try { ChartTable.Read(file); }catch(InvalidDataException){rejected=true;}catch(InvalidOperationException){rejected=true;}
            Assert(rejected,"Reject invalid chart: "+name);
        }
        Reject("units",csv.Replace("\"mm\"","\"deg\""));
        Reject("duplicate-times",csv.Replace("\"1.25\"","\"0\""));
        Reject("nonfinite",csv.Replace("\"-12.5\"","\"NaN\""));
        Reject("bad-kind",csv.Replace("\"distance\"","\"unknown\""));
        Reject("bad-quotes",csv+"\"unclosed");
        Reject("bad-id",csv.Replace(tracks[0].Id.ToString(),"not-a-guid"));
        // Simulate Excel's shared-string storage and a recalculated formula.
        string formula=Path.Combine(directory,"excel-edited.xlsx");File.Copy(Path.Combine(directory,"roundtrip.xlsx"),formula,true);
        XNamespace s="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using(var zip=ZipFile.Open(formula,ZipArchiveMode.Update))
        {
            var entry=zip.GetEntry("xl/worksheets/sheet1.xml")!;XDocument sheet;using(var stream=entry.Open())sheet=XDocument.Load(stream);entry.Delete();
            var name=sheet.Descendants(s+"c").Single(c=>(string?)c.Attribute("r")=="B2");name.SetAttributeValue("t","s");name.RemoveNodes();name.Add(new XElement(s+"v",0));
            var value=sheet.Descendants(s+"c").Single(c=>(string?)c.Attribute("r")=="F2");value.RemoveNodes();value.Add(new XElement(s+"f","200+15.9"),new XElement(s+"v","215.9"));
            using(var stream=zip.CreateEntry("xl/worksheets/sheet1.xml").Open())sheet.Save(stream);
            using(var stream=zip.CreateEntry("xl/sharedStrings.xml").Open())new XDocument(new XElement(s+"sst",new XElement(s+"si",new XElement(s+"t",tracks[0].Name)))).Save(stream);
        }
        Assert(ChartTable.Read(formula)[0].Points[0].Value==215.9,"Excel cached formula and shared strings");
        using(var zip=ZipFile.Open(formula,ZipArchiveMode.Update))
        {
            var entry=zip.GetEntry("xl/worksheets/sheet1.xml")!;XDocument sheet;using(var stream=entry.Open())sheet=XDocument.Load(stream);entry.Delete();
            sheet.Descendants(s+"c").Single(c=>(string?)c.Attribute("r")=="F2").Element(s+"v")!.Remove();
            using(var stream=zip.CreateEntry("xl/worksheets/sheet1.xml").Open())sheet.Save(stream);
        }
        bool missing=false;try{ChartTable.Read(formula);}catch(InvalidDataException){missing=true;}
        Assert(missing,"Formula without saved result must not import as zero");
        File.WriteAllText(Path.Combine(directory,"result.txt"),"PASS: CSV/XLSX precision, names, IDs, invalid rows, shared strings, cached and missing formula results");
    }
}