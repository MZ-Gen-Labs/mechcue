namespace MechCue;
public partial class MainForm
{
    internal void VerifyConceptUi(string manifest)
    {
        ConnectDocument();int before=tracks.Count;
        var request=System.Text.Json.JsonSerializer.SerializeToElement(new{method="import_concept",args=new{manifestPath=manifest}});
        HandleAi(request);if(tracks.Count!=before||!bridge.IsConcept(Current)||kind.Enabled||axis.Enabled||live.Checked)throw new Exception("Concept import UI state mismatch");
        double baseline=Current.Points[0].Value;
        HandleAi(System.Text.Json.JsonSerializer.SerializeToElement(new{method="set_keyframe",args=new{trackId=Current.Id.ToString(),time=4,value=baseline+20}}));
        live.Checked=true;time.Value=4;
        if(Math.Abs(bridge.ConceptCurrentValue(Current)-baseline-20)>1e-6)throw new Exception("UI chart drive failed");
        live.Checked=false;PausePlayback();SaveToDocument();bridge.Disconnect();documentReady=false;documentSettingsDirty=false;
    }
}
