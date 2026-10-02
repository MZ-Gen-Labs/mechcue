using System.Drawing.Imaging;
using System.Text.Json;
namespace MechCue;
public static partial class SelfTest
{
    static void TestVideoExport()
    {
        string folder=Path.Combine(Path.GetTempPath(),"MechCue-Video-Test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string? oldSettings=Environment.GetEnvironmentVariable("MECHCUE_MCP_SETTINGS_PATH");
        Environment.SetEnvironmentVariable("MECHCUE_MCP_SETTINGS_PATH",Path.Combine(folder,"access.json"));McpAccessSettings.Save("write");
        void Assert(bool ok,string name){if(!ok)throw new Exception("Video: "+name);}
        try {
            byte[] Jpeg(double t){using var image=new Bitmap(320,240);using(var g=Graphics.FromImage(image))g.Clear(t>0?Color.Blue:Color.Red);using var stream=new MemoryStream();image.Save(stream,ImageFormat.Jpeg);return stream.ToArray();}
            string path=Path.Combine(folder,"success.avi");int restored=0;var positions=new List<double>();
            var job=new VideoExportJob(path,"test.asm","test",0,1,4,320,240,t=>{positions.Add(t);return Jpeg(t);},()=>restored++);
            Assert(!File.Exists(path),"no final file before completion");while(job.Running)job.Step();
            Assert(job.State=="completed"&&job.Frames==5&&restored==1&&positions.SequenceEqual(new[]{0d,.25,.5,.75,1}),"exact chart time stepping and restore");
            var data=File.ReadAllBytes(path);Assert(System.Text.Encoding.ASCII.GetString(data,0,4)=="RIFF"&&BitConverter.ToUInt32(data,4)==data.Length-8,"RIFF length");
            int idx=Find(data,"idx1"),movi=Find(data,"movi");Assert(BitConverter.ToUInt32(data,idx+4)==80,"index length");
            for(int i=0;i<5;i++){int at=idx+8+i*16;int chunk=movi+(int)BitConverter.ToUInt32(data,at+8);Assert(System.Text.Encoding.ASCII.GetString(data,chunk,4)=="00dc","index points to frame");using var stream=new MemoryStream(data,chunk+8,(int)BitConverter.ToUInt32(data,chunk+4));using var image=Image.FromStream(stream);Assert(image.Size==new Size(320,240),"JPEG frame decode");}
            try {new VideoExportJob(path,"","",0,1,4,320,240,Jpeg,()=>{});throw new Exception("Overwrite accepted");}catch(IOException){}
            job=new(Path.Combine(folder,"cancel.avi"),"","",0,1,4,320,240,Jpeg,()=>restored++);job.Step();job.Cancel();job.Cancel();Assert(job.State=="cancelled"&&!File.Exists(job.OutputPath)&&restored==2,"cancel idempotent");
            job=new(Path.Combine(folder,"fail.avi"),"","",0,1,4,320,240,_=>throw new IOException("capture failed"),()=>restored++);job.Step();Assert(job.State=="failed"&&restored==3&&!File.Exists(job.OutputPath),"capture failure restore");
            job=new(Path.Combine(folder,"rollback.avi"),"","",0,1,1,320,240,Jpeg,()=>throw new IOException("restore failed"));while(job.Running)job.Step();Assert(job.State=="failed"&&!File.Exists(job.OutputPath)&&job.Error!.Contains("restored"),"restore failure is not success");
            Assert(!Directory.EnumerateFiles(folder,"*.partial").Any(),"partial cleanup");
            foreach(var v in new[]{double.NaN,-1,1,301})try{VideoExportJob.Validate(Path.Combine(folder,"invalid.avi"),v,1,4,320,240);throw new Exception("Bad range accepted");}catch(ArgumentException){}
            TestVideoForm(folder,Assert);
            // Retain one independent-decoder fixture under diagnostic output, never package it.
            File.Copy(path,Path.Combine(AppContext.BaseDirectory,"video-test.avi"),true);
        }finally{Environment.SetEnvironmentVariable("MECHCUE_MCP_SETTINGS_PATH",oldSettings);Directory.Delete(folder,true);}
    }
    static int Find(byte[] data,string value){byte[] bytes=System.Text.Encoding.ASCII.GetBytes(value);for(int i=0;i<=data.Length-bytes.Length;i++)if(data.AsSpan(i,bytes.Length).SequenceEqual(bytes))return i;throw new Exception("AVI chunk missing: "+value);}
    static void TestVideoForm(string folder,Action<bool,string> assert)
    {
        var doc=new FakeCollisionDocument();var app=new FakeApplication{ActiveDocument=doc};app.OpenDocuments.Items.Add(doc);var part=new FakePart();doc.Occurrences.Items.Add(part);
        using var form=new MainForm(hostedApplication:app);form.VerifyVideoForm(folder,app,doc,part,assert);
    }
}
public partial class MainForm
{
    internal void VerifyVideoForm(string folder,SelfTest.FakeApplication app,SelfTest.FakeDocument doc,SelfTest.FakePart part,Action<bool,string> assert)
    {
        tracks[0].Kind="部品移動";RefreshTracks(0);bridge.Connect();bridge.Bind(tracks[0],new Target("Moving",part,"Matrix"));
        time.Value=.5m;live.Checked=true;
        // Deliberately differ from cursor pose: export must restore actual CAD, not cursor-derived CAD.
        part.Pose[12]=.537;double[] before=(double[])part.Pose.Clone();int frames=0;
        app.ActiveWindow.View.Capture=()=>frames++;
        object Request(string method,object args)=>HandleAi(JsonSerializer.SerializeToElement(new{method,args}));
        var args=new{outputPath=Path.Combine(folder,"form.avi"),startTime=0,endTime=1,fps=4,width=320,height=240,checkInterference=false};
        Request("export_video",args);string id=videoExport!.Id;
        try { Request("reset_values",new{trackNumber=1,value=9});throw new Exception("Export edit accepted");}catch(InvalidOperationException){}
        while(videoExport.Running)videoExport.Step();CancelVideoExport();
        assert(videoExport.State=="completed"&&frames==5&&time.Value==.5m&&live.Checked&&!timer.Enabled&&part.Pose.SequenceEqual(before),"form restore actual pose, time, live setting and paused state");
        Request("get_video_export",new{jobId=id});
        Request("export_video",args with {outputPath=Path.Combine(folder,"form-cancel.avi")});videoExport!.Step();Request("cancel_video_export",new{jobId=videoExport.Id});
        assert(videoExport.State=="cancelled"&&part.Pose.SequenceEqual(before)&&Controls.Cast<Control>().All(c=>c.Enabled),"form cancellation and editing restored");
        doc.ReadOnly=true;try{Request("export_video",args with {outputPath=Path.Combine(folder,"readonly.avi")});throw new Exception("Readonly accepted");}catch(InvalidOperationException){}
        assert(part.Pose.SequenceEqual(before),"readonly rejection leaves pose unchanged");
        doc.ReadOnly=false;live.Checked=false;bridge.Disconnect();
    }
}
