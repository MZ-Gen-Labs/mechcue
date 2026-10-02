using System.Text;
using System.Text.Json;
using System.Drawing.Imaging;
namespace MechCue;

// Single-stream Motion JPEG AVI. Frames and their offsets are streamed to disk;
// only the small index stays in memory. No shell, network or external encoder.
internal sealed class MjpegAviWriter : IDisposable
{
    readonly BinaryWriter output;
    readonly List<(uint Offset,uint Size)> index = [];
    readonly long riffSize, moviSize, moviBase, totalFrames, streamFrames;
    uint largest;
    bool finished;
    public MjpegAviWriter(string path, int width, int height, int fps)
    {
        output = new BinaryWriter(new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None),Encoding.ASCII);
        try {
            Four("RIFF"); riffSize=output.BaseStream.Position; output.Write(0u); Four("AVI ");
            long hdrl=List("hdrl"); Four("avih"); output.Write(56u);
            output.Write((uint)(1000000/fps)); output.Write(0u); output.Write(0u); output.Write(0x10u);
            totalFrames=output.BaseStream.Position; output.Write(0u); output.Write(0u); output.Write(1u); output.Write(0u);
            output.Write((uint)width); output.Write((uint)height); for(int i=0;i<4;i++)output.Write(0u);
            long strl=List("strl"); Four("strh"); output.Write(56u); Four("vids"); Four("MJPG");
            output.Write(0u); output.Write((ushort)0); output.Write((ushort)0); output.Write(0u);
            output.Write(1u); output.Write((uint)fps); output.Write(0u);
            streamFrames=output.BaseStream.Position; output.Write(0u); output.Write(0u); output.Write(uint.MaxValue); output.Write(0u);
            output.Write((short)0); output.Write((short)0); output.Write((short)width); output.Write((short)height);
            Four("strf"); output.Write(40u); output.Write(40u); output.Write(width); output.Write(height);
            output.Write((ushort)1); output.Write((ushort)24); Four("MJPG"); output.Write((uint)(width*height*3));
            for(int i=0;i<4;i++)output.Write(0u);
            EndList(strl); EndList(hdrl); moviSize=List("movi"); moviBase=moviSize+4;
        } catch { output.Dispose(); throw; }
    }
    void Four(string s)=>output.Write(Encoding.ASCII.GetBytes(s));
    long List(string name) { Four("LIST"); long at=output.BaseStream.Position;output.Write(0u);Four(name);return at; }
    void Patch(long at,uint value) { long end=output.BaseStream.Position;output.BaseStream.Position=at;output.Write(value);output.BaseStream.Position=end; }
    void EndList(long at)=>Patch(at,checked((uint)(output.BaseStream.Position-at-4)));
    public void Add(byte[] jpeg)
    {
        if(finished)throw new InvalidOperationException("Video is already finalized.");
        if(output.BaseStream.Position+jpeg.Length+index.Count*16L>1024L*1024*1024)throw new IOException("AVI exceeds the 1 GiB export limit. Reduce resolution or duration.");
        index.Add((checked((uint)(output.BaseStream.Position-moviBase)),(uint)jpeg.Length));
        Four("00dc");output.Write((uint)jpeg.Length);output.Write(jpeg);if((jpeg.Length&1)!=0)output.Write((byte)0);
        largest=Math.Max(largest,(uint)jpeg.Length);
    }
    public void Finish()
    {
        if(finished)return;
        EndList(moviSize);Four("idx1");output.Write(checked((uint)index.Count*16));
        foreach(var entry in index) { Four("00dc");output.Write(0x10u);output.Write(entry.Offset);output.Write(entry.Size); }
        Patch(totalFrames,(uint)index.Count);Patch(streamFrames,(uint)index.Count);
        Patch(totalFrames+12,largest);Patch(streamFrames+4,largest);EndList(riffSize);
        output.Flush();finished=true;
    }
    public void Dispose()=>output.Dispose();
}

internal sealed class VideoExportJob
{
    public string Id {get;}=Guid.NewGuid().ToString("N");
    public string OutputPath {get;}
    public string Document {get;}
    public string Pattern {get;}
    public double Start {get;}
    public double End {get;}
    public int Fps {get;}
    public int TotalFrames {get;}
    public int Frames {get;private set;}
    public string State {get;private set;}="running";
    public string? Error {get;private set;}
    public bool Running=>State=="running";
    readonly string partial;
    readonly MjpegAviWriter writer;
    readonly Func<double,byte[]> capture;
    readonly Action restore;
    public VideoExportJob(string path,string document,string pattern,double start,double end,int fps,int width,int height,Func<double,byte[]> capture,Action restore)
    {
        Validate(path,start,end,fps,width,height);
        OutputPath=Path.GetFullPath(path);Document=document;Pattern=pattern;Start=start;End=end;Fps=fps;
        TotalFrames=checked((int)Math.Ceiling((end-start)*fps)+1);
        partial=OutputPath+"."+Id+".partial";
        writer=new(partial,width,height,fps);this.capture=capture;this.restore=restore;
    }
    internal static void Validate(string path,double start,double end,int fps,int width,int height)
    {
        if(!Path.IsPathFullyQualified(path)||!string.Equals(Path.GetExtension(path),".avi",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("outputPath must be an absolute .avi file path.");
        if(!double.IsFinite(start)||!double.IsFinite(end)||start<0||end<=start||end-start>300||fps<1||fps>30||Math.Ceiling((end-start)*fps)+1>9001)throw new ArgumentException("Video range must be positive, at most 300 seconds, with 1–30 fps.");
        if(width<320||width>1920||height<240||height>1080)throw new ArgumentException("Video dimensions must be 320–1920 by 240–1080 pixels.");
        if(!Directory.Exists(Path.GetDirectoryName(path)))throw new DirectoryNotFoundException("Create the output folder before exporting video.");
        if(File.Exists(path)||Directory.Exists(path))throw new IOException("Output already exists; choose a new file name.");
    }
    public void Step()
    {
        if(!Running)return;
        try { writer.Add(capture(Math.Min(End,Start+(double)Frames/Fps)));Frames++;if(Frames==TotalFrames)Complete("completed"); }
        catch(Exception ex){if(Running)Complete("failed",(ex.InnerException??ex).Message);}
    }
    public void Cancel(){if(Running)Complete("cancelled");}
    void Complete(string state,string? error=null)
    {
        State="finishing";
        try { restore(); } catch(Exception ex) { state="failed";error=(error==null?"":error+"; ")+"Original pose could not be restored: "+(ex.InnerException??ex).Message; }
        try { if(state=="completed")writer.Finish(); }
        catch(Exception ex){state="failed";error=ex.Message;}
        finally { writer.Dispose(); }
        try { if(state=="completed")File.Move(partial,OutputPath,false);else File.Delete(partial); }
        catch(Exception ex){state="failed";error=(error==null?"":error+"; ")+ex.Message;}
        State=state;Error=error;
        if(error!=null)DiagnosticLog.Error("video-export",new IOException(error));
    }
    public object Status()=>new{jobId=Id,state=State,outputPath=OutputPath,document=Document,pattern=Pattern,startTime=Start,endTime=End,fps=Fps,frames=Frames,totalFrames=TotalFrames,progress=(double)Frames/TotalFrames,videoSeconds=(double)TotalFrames/Fps,error=Error};
}

public sealed partial class Bridge
{
    internal string VideoDocumentName => Convert.ToString(Get(Document,"FullName")) ?? "";
    internal Action CaptureVideoPose()
    {
        Check();EnsureWritableDocument();
        var originalDocument=doc;
        var concepts=ConceptSnapshot();
        var values=bindings.Values.Select(b=>(Binding:b,Value:b.Target.Property=="Matrix"?(object)Matrix(b.Target.Com):Get(b.Target.Com,b.Target.Property),Active:b.Active)).ToList();
        var grounds=bindings.Values.SelectMany(b=>b.Grounds).Select(g=>(g.Relation,Suppress:Convert.ToBoolean(Get(g.Relation,"Suppress")))).ToList();
        var occurrences=Get(doc!,"Occurrences");
        var poses=Enumerable.Range(1,Convert.ToInt32(Get(occurrences,"Count"))).Select(i=>{var part=GetItem(occurrences,i);return (Part:part,Pose:Matrix(part));}).ToList();
        return ()=>{
            if(!Equals(doc,originalDocument))throw new InvalidOperationException("Connected document changed during export.");
            // The original document may no longer be active, but must still exist.
            Check(false);
            RestoreConceptSnapshot(concepts);
            foreach(var value in values) { if(value.Binding.Target.Property=="Matrix")CadCallRef(value.Binding.Target.Com,"PutMatrix",[0],value.Value,true);else Set(value.Binding.Target.Com,value.Binding.Target.Property,value.Value);value.Binding.Active=value.Active; }
            foreach(var pose in poses)CadCallRef(pose.Part,"PutMatrix",[0],pose.Pose,true);
            foreach(var ground in grounds)Set(ground.Relation,"Suppress",ground.Suppress);
            if(ApplicationEventSink.SameDocument(Get(app!,"ActiveDocument"),doc!))Call(Get(Get(app!,"ActiveWindow"),"View"),"Update");
            foreach(var pose in poses)if(Matrix(pose.Part).Zip(pose.Pose).Any(p=>Math.Abs(p.First-p.Second)>1e-7))throw new InvalidOperationException("A part pose did not return to its original value.");
        };
    }
    internal byte[] CaptureVideoFrame(double position,string framePath,int width,int height,bool checkInterference)
    {
        Check();EnsureWritableDocument();
        if(checkInterference)ApplyChecked(position);else Apply(position);
        var view=Get(Get(app!,"ActiveWindow"),"View");
        // Resolution=1 means width/height are pixels; JPEG uses 24-bit color.
        Call(view,"SaveAsImage",framePath,width,height,Type.Missing,1,24,0,false);
        using var image=Image.FromFile(framePath);
        if(image.Width!=width||image.Height!=height)throw new IOException("Solid Edge returned unexpected video frame dimensions.");
        using var encoded=new MemoryStream();image.Save(encoded,ImageFormat.Jpeg);return encoded.ToArray();
    }
}

public partial class MainForm
{
    VideoExportJob? videoExport;
    System.Windows.Forms.Timer? videoTimer;
    readonly List<(Control Control,bool Enabled)> videoControls=[];
    void CancelVideoExport()
    {
        videoTimer?.Stop();videoExport?.Cancel();videoTimer?.Dispose();videoTimer=null;
        foreach(var item in videoControls)if(!item.Control.IsDisposed)item.Control.Enabled=item.Enabled;videoControls.Clear();
    }
    object StartVideoExport(JsonElement args)
    {
        McpAccessSettings.EnsureAllowed(true);
        if(!bridge.Connected||bridge.BindingCount==0)throw new InvalidOperationException("Connect MechCue to an assembly and assign chart targets before exporting video.");
        string path=args.GetProperty("outputPath").GetString()!;
        double start=args.GetProperty("startTime").GetDouble(),end=args.GetProperty("endTime").GetDouble();if(end<0)end=tracks.Max(t=>t.Points[^1].Time);
        int fps=args.GetProperty("fps").GetInt32(),width=args.GetProperty("width").GetInt32(),height=args.GetProperty("height").GetInt32();
        VideoExportJob.Validate(path,start,end,fps,width,height);
        if(end>tracks.Max(t=>t.Points[^1].Time))throw new ArgumentException("endTime exceeds the chart duration.");
        Commit();foreach(var track in tracks)track.Validate();
        Action restorePose=bridge.CaptureVideoPose();
        string document=bridge.VideoDocumentName;
        string folder=Path.Combine(Path.GetTempPath(),"MechCue-Video-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string framePath=Path.Combine(folder,"frame.jpg");
        decimal originalTime=time.Value;bool originalLive=live.Checked;bool interference=args.GetProperty("checkInterference").GetBoolean();
        Action restore=()=>{
            try { restorePose(); } finally {
                time.Value=originalTime;
                // Re-enabling reflection would move CAD to the chart cursor instead of restoring its captured pose.
                loading=true;try { live.Checked=originalLive; }finally{loading=false;}
                if(File.Exists(framePath))File.Delete(framePath);Directory.Delete(folder);
            }
        };
        try {
            PausePlayback();live.Checked=false;
            videoExport=new(path,document,patterns.FirstOrDefault(p=>p.Id==activePattern)?.Name??"",start,end,fps,width,height,
                position=>{McpAccessSettings.EnsureAllowed(true);time.Value=(decimal)position;return bridge.CaptureVideoFrame(position,framePath,width,height,interference);},restore);
        } catch { restore();throw; }
        foreach(Control control in Controls){videoControls.Add((control,control.Enabled));control.Enabled=false;}
        videoTimer=new(){Interval=15};videoTimer.Tick+=(_,_)=>{
            videoExport.Step();status.Text=$"Video: {videoExport.Frames}/{videoExport.TotalFrames} ({videoExport.State})";
            if(!videoExport.Running){CancelVideoExport();UpdateConnection();}
        };videoTimer.Start();DiagnosticLog.Write("video-export-start",videoExport.Status());return videoExport.Status();
    }
    object VideoStatus(JsonElement args,bool cancel)
    {
        string id=args.GetProperty("jobId").GetString()!;
        if(videoExport==null||videoExport.Id!=id)throw new ArgumentException("Unknown video jobId in this MechCue session.");
        if(cancel)CancelVideoExport();return videoExport.Status();
    }
}
