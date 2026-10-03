using System.Text.Json;
namespace MechCue;
public partial class MainForm
{
    internal void ExportMotionInspection(string path)
    {
        using var stream=File.Create(path);using var writer=new Utf8JsonWriter(stream);
        writer.WriteStartObject();writer.WriteString("created",DateTimeOffset.Now);writer.WriteBoolean("continuousPathCertified",false);writer.WriteString("certificateScope","Inspect per-record certificates and exclusions; history is not a combined certificate.");writer.WriteStartArray("records");
        foreach(var record in motionReports){writer.WriteStartObject();foreach(var p in JsonSerializer.SerializeToElement(record).EnumerateObject()){
            if(p.Name!="Result"){p.WriteTo(writer);continue;}
            writer.WriteStartObject("Result");foreach(var field in record.Result.EnumerateObject()){
                if(field.Name=="samples"){writer.WriteStartArray("samples");foreach(var sample in MotionInspectionSeries.Read(record.Result,"samples"))sample.WriteTo(writer);writer.WriteEndArray();}
                else if(field.Name=="continuousVerification"){writer.WriteStartObject(field.Name);foreach(var v in field.Value.EnumerateObject()){
                    if(v.Name=="Segments"){writer.WriteStartArray(v.Name);foreach(var segment in MotionInspectionSeries.Read(record.Result,"segments"))segment.WriteTo(writer);writer.WriteEndArray();}else v.WriteTo(writer);
                }writer.WriteEndObject();}
                else if(field.Name is "samplesTruncated" or "segmentsTruncated")writer.WriteBoolean(field.Name,false);
                else if(field.Name!="detailArchive")field.WriteTo(writer);
            }writer.WriteEndObject();
        }writer.WriteEndObject();}writer.WriteEndArray();writer.WriteEndObject();
    }
}
