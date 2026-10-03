using System.Text.Json;
namespace MechCue;
public static partial class SelfTest
{
    static void TestInspectionArchive()
    {
        string? previous=Environment.GetEnvironmentVariable("MECHCUE_INSPECTION_RESULTS_DIRECTORY");
        string directory=Path.Combine(AppContext.BaseDirectory,"inspection-archive-test-"+Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MECHCUE_INSPECTION_RESULTS_DIRECTORY",directory);
        try{
            var transformed=Bridge.MoveInspectionPoint([10,0,0],Bridge.CadTransform(0,0,0,0,0,0),Bridge.CadTransform(100,200,0,0,0,90));
            if(Math.Abs(transformed[0]-100)>1e-8||Math.Abs(transformed[1]-210)>1e-8)throw new Exception("Cached clearance witness points must follow common rigid translation and rotation");
            var series=new MotionInspectionSeries("samples",200,1000000);
            for(int i=0;i<137;i++)series.Add(new{time=i,passed=i!=41,payload=new string('x',100)});
            var report=JsonSerializer.SerializeToElement(new{samples=series.Preview(),detailArchive=new{samples=series.Manifest()}});
            if(!series.Archived||MotionInspectionSeries.Total(report,"samples")!=137)throw new Exception("Automatic archive did not retain all sample counts");
            var page=MotionInspectionSeries.Read(report,"samples").Skip(98).Take(17).ToArray();
            if(page.Length!=17||page[0].GetProperty("time").GetInt32()!=98||page[^1].GetProperty("time").GetInt32()!=114)throw new Exception("Archive pagination crossed a chunk incorrectly");
            if(MotionInspectionSeries.Read(report,"samples").Count()!=137||MotionInspectionSeries.Read(report,"samples").Count(p=>!p.GetProperty("passed").GetBoolean())!=1)throw new Exception("Archive lost an abnormal sample");
            bool refused=false;try{new MotionInspectionSeries("samples",10,20).Add(new{payload=new string('x',30)});}catch(IOException){refused=true;}if(!refused)throw new Exception("Archive disk budget silently exceeded");
        }finally{Environment.SetEnvironmentVariable("MECHCUE_INSPECTION_RESULTS_DIRECTORY",previous);}
    }
}
