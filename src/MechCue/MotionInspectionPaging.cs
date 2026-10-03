using System.Text.Json;
namespace MechCue;
public partial class MainForm
{
    static Dictionary<string,JsonElement> InspectionOverview(JsonElement result)
    {
        var fields=result.EnumerateObject().Where(p=>p.Name is not ("samples" or "baseSampleTimes")).ToDictionary(p=>p.Name,p=>p.Value.Clone());
        fields["totalSampleCount"]=JsonSerializer.SerializeToElement(MotionInspectionSeries.Total(result,"samples"));
        if(result.TryGetProperty("continuousVerification",out var verification)){
            var shortVerification=verification.EnumerateObject().Where(p=>p.Name!="Segments").ToDictionary(p=>p.Name,p=>p.Value.Clone());
            shortVerification["totalSegmentCount"]=JsonSerializer.SerializeToElement(MotionInspectionSeries.Total(result,"segments"));fields["continuousVerification"]=JsonSerializer.SerializeToElement(shortVerification);
        }
        return fields;
    }
    static object InspectionOperationSummary(RecordedMotionInspection record)
    {var result=InspectionOverview(record.Result);result["reportId"]=JsonSerializer.SerializeToElement(record.Id);result["detailsVia"]=JsonSerializer.SerializeToElement("get_inspection_reports: reportId, includeSamples=true, sampleOffset, sampleLimit");return result;}
    object InspectionReports(JsonElement args)
    {
        int Number(string name,int fallback)=>args.TryGetProperty(name,out var v)?v.GetInt32():fallback;
        int offset=Number("offset",0),limit=Number("limit",20),sampleOffset=Number("sampleOffset",0),sampleLimit=Number("sampleLimit",100);
        bool detailed=args.TryGetProperty("includeSamples",out var include)&&include.GetBoolean();
        string? id=args.TryGetProperty("reportId",out var identity)?identity.GetString():null;
        if(offset<0||limit is <1 or >20||sampleOffset<0||sampleLimit is <1 or >100)throw new ArgumentException("Use offset/sampleOffset >=0, limit 1..20, sampleLimit 1..100");
        if(detailed&&string.IsNullOrEmpty(id))throw new ArgumentException("Detailed pages require one reportId from the overview");
        var source=motionReports.AsEnumerable();
        if(!string.IsNullOrEmpty(id)){if(!Guid.TryParse(id,out var guid))throw new ArgumentException("Invalid reportId");source=source.Where(r=>r.Id==guid);if(!source.Any())throw new InvalidOperationException("Inspection report not retained; export history before its 32 MiB/500 record budget expires");}
        var selected=source.Skip(offset).Take(limit).ToArray();
        object Build(int pageSize)=>new{totalRecords=source.Count(),offset,nextOffset=offset+selected.Length<source.Count()?(int?)(offset+selected.Length):null,includeSamples=detailed,historyByteBudget=32*1024*1024,records=selected.Select(r=>{
            var fields=InspectionOverview(r.Result);
            if(detailed){var page=MotionInspectionSeries.Read(r.Result,"samples").Skip(sampleOffset).Take(pageSize).ToArray();fields["samples"]=JsonSerializer.SerializeToElement(page);fields["sampleOffset"]=JsonSerializer.SerializeToElement(sampleOffset);fields["nextSampleOffset"]=JsonSerializer.SerializeToElement(sampleOffset+page.Length<MotionInspectionSeries.Total(r.Result,"samples")?(int?)(sampleOffset+page.Length):null);
                if(r.Result.TryGetProperty("continuousVerification",out var verification)){var shortVerification=fields["continuousVerification"].EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());var segmentPage=MotionInspectionSeries.Read(r.Result,"segments").Skip(sampleOffset).Take(pageSize).ToArray();shortVerification["Segments"]=JsonSerializer.SerializeToElement(segmentPage);shortVerification["nextSegmentOffset"]=JsonSerializer.SerializeToElement(sampleOffset+segmentPage.Length<MotionInspectionSeries.Total(r.Result,"segments")?(int?)(sampleOffset+segmentPage.Length):null);fields["continuousVerification"]=JsonSerializer.SerializeToElement(shortVerification);}
            }
            return new{r.Id,r.PatternId,r.PatternName,r.Document,r.ChartFingerprint,r.Created,r.ExpectedEnd,Result=fields};
        }).ToArray()};
        for(int size=sampleLimit;;size=Math.Max(1,size/2)){var result=JsonSerializer.SerializeToElement(Build(size));if(System.Text.Encoding.UTF8.GetByteCount(result.GetRawText())<=4*1024*1024)return result;if(!detailed||size==1)throw new InvalidOperationException("Inspection response exceeds 4 MiB; use fewer records or export the report to a file");}
    }
}
