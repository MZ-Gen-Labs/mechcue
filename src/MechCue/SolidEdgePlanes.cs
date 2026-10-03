namespace MechCue;

public sealed partial class Bridge
{
    public static object CadCreateOffsetPlane(string expectedDocument, int planeNumber, double offsetMm)
    {
        CadFinite(offsetMm, nameof(offsetMm));
        if (offsetMm == 0) throw new ArgumentException("Nonzero offsetMm required; reuse the existing plane for zero offset");
        var doc = CadDocument(CadApplication(), expectedDocument, ".par");
        if (Convert.ToInt32(Get(doc, "ModelingMode")) != 2 || Convert.ToInt32(Get(Get(doc, "Models"), "Count")) > 1)
            throw new InvalidOperationException("Empty or single-model Ordered .par required");
        DetailHealthy(doc);
        var source = CadPlane(doc, planeNumber);
        var origin = ConceptPlaneVector(source, "GetRootPoint");
        var normal = ConceptPlaneVector(source, "GetNormal");
        if (origin.Any(v => !double.IsFinite(v)) || normal.Any(v => !double.IsFinite(v)) || Math.Abs(normal.Sum(v => v*v)-1) > 1e-6)
            throw new InvalidOperationException("Source plane frame is invalid");
        var planes = Get(doc, "RefPlanes"); int before = Convert.ToInt32(Get(planes, "Count"));
        try {
            var plane = Call(planes, "AddParallelByDistance", source, Math.Abs(offsetMm)/1000, offsetMm > 0 ? 2 : 1,
                Type.Missing, Type.Missing, false, Type.Missing); // Local=false: retain a standalone global plane on save
            var actualOrigin = ConceptPlaneVector(plane, "GetRootPoint");
            var actualNormal = ConceptPlaneVector(plane, "GetNormal");
            if (!Convert.ToBoolean(Get(plane, "Global")) || Convert.ToInt32(Get(planes, "Count")) != before+1 || Enumerable.Range(0,3).Any(i =>
                !double.IsFinite(actualOrigin[i]) || !double.IsFinite(actualNormal[i]) ||
                Math.Abs(actualOrigin[i]-origin[i]-normal[i]*offsetMm/1000) > 1e-7 || Math.Abs(actualNormal[i]-normal[i]) > 1e-6))
                throw new InvalidOperationException("Requested offset plane position/orientation was not retained");
            Call(doc, "Recompute"); DetailHealthy(doc);
            return new {document=expectedDocument, planeNumber=before+1, sourcePlaneNumber=planeNumber, offsetMm,
                global=true, originMm=actualOrigin.Select(v=>v*1000).ToArray(), normal=actualNormal,
                coordinateFrame="Part document coordinates; offset follows the source plane normal", saved=false};
        } catch (Exception error) {
            try { MachiningRemoveAdded(planes, before); Call(doc, "Recompute"); DetailHealthy(doc); }
            catch (Exception rollback) { throw new InvalidOperationException("Offset plane failed: "+(error.InnerException??error).Message+"; ROLLBACK FAILED: "+(rollback.InnerException??rollback).Message, error); }
            throw new InvalidOperationException("Offset plane failed; newly added planes removed, document may be dirty: "+(error.InnerException??error).Message, error);
        }
    }
}
