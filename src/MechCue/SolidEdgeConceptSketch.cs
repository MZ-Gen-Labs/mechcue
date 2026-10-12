using System.Globalization;
namespace MechCue;

public sealed partial class Bridge
{
    // Native variable names of a concept body, matching ConceptBody.SizeMm order.
    internal static string[] ConceptVariableNames(ConceptBody body) => body.Shape == "cylinder" ? ["Diameter", "Diameter", "Length"] : ["SizeX", "SizeY", "SizeZ"];

    // Builds one concept body as a fully driven, centred extrusion on the part's first reference plane:
    // - the global reference planes perpendicular to it are projected as datum lines;
    // - a box is a rectangle with horizontal/vertical relations, kept symmetric about construction axes
    //   that lie on those datum lines; a cylinder's centre lies on both datum lines;
    // - width/depth (or diameter) are driving dimensions and the extrusion is symmetric about the plane,
    //   so every size can later be changed through named variables without moving the body's centre.
    static void CadConceptBody(object document, ConceptBody body)
    {
        var plane = CadPlane(document, 1);
        double[] u = ConceptPlaneVector(plane, "GetReferenceDirection"), n = ConceptPlaneVector(plane, "GetNormal");
        double[] w = [n[1]*u[2]-n[2]*u[1], n[2]*u[0]-n[0]*u[2], n[0]*u[1]-n[1]*u[0]];
        object Datum(double[] normal) {
            var planes = Get(document, "RefPlanes");
            for (int i = 1; i <= Convert.ToInt32(Get(planes, "Count")); i++) {
                var candidate = GetItem(planes, i); var m = ConceptPlaneVector(candidate, "GetNormal");
                if (Math.Abs(m[0]*normal[0]+m[1]*normal[1]+m[2]*normal[2]) > .999999) return candidate;
            }
            throw new InvalidOperationException("The part template needs global reference planes perpendicular to its first plane.");
        }
        double sx = body.SizeMm[0] / 1000, sy = body.SizeMm[1] / 1000, sz = body.SizeMm[2] / 1000;
        object? profileSet = null; bool created = false;
        try {
            profileSet = Call(Get(document, "ProfileSets"), "Add"); var profile = Call(Get(profileSet, "Profiles"), "Add", plane);
            // Plane normal along profile X projects as the vertical datum line, and vice versa.
            var vertical = Call(profile, "ProjectRefPlane", Datum(u)); var horizontal = Call(profile, "ProjectRefPlane", Datum(w));
            var relations = Get(profile, "Relations2d"); var dimensions = Get(profile, "Dimensions"); var driving = new List<object>();
            if (body.Shape == "cylinder") {
                var circle = Call(Get(profile, "Circles2d"), "AddByCenterRadius", 0.0, 0.0, sx / 2);
                Call(relations, "AddPointOn", circle, 0, vertical, Type.Missing); Call(relations, "AddPointOn", circle, 0, horizontal, Type.Missing);
                driving.Add(Call(dimensions, "AddCircularDiameter", circle));
            } else {
                double[][] corners = [[-sx/2, -sy/2], [sx/2, -sy/2], [sx/2, sy/2], [-sx/2, sy/2]];
                var lines = Enumerable.Range(0, 4).Select(i => Call(Get(profile, "Lines2d"), "AddBy2Points", corners[i][0], corners[i][1], corners[(i+1)%4][0], corners[(i+1)%4][1])).ToArray();
                for (int i = 0; i < 4; i++) Call(relations, "AddKeypoint", lines[i], 1, lines[(i+1)%4], 0, Type.Missing);
                Call(relations, "AddHorizontal", lines[0], Type.Missing); Call(relations, "AddHorizontal", lines[2], Type.Missing);
                Call(relations, "AddVertical", lines[1], Type.Missing); Call(relations, "AddVertical", lines[3], Type.Missing);
                // Projected datum lines cannot be symmetry axes directly; construction axes lying on them can.
                var axisY = Call(Get(profile, "Lines2d"), "AddBy2Points", 0.0, -sy, 0.0, sy); var axisX = Call(Get(profile, "Lines2d"), "AddBy2Points", -sx, 0.0, sx, 0.0);
                foreach (var (axis, datum) in new[] { (axisY, vertical), (axisX, horizontal) }) {
                    Call(profile, "ToggleConstruction", axis);
                    Call(relations, "AddPointOn", axis, 1, datum, Type.Missing); Call(relations, "AddPointOn", axis, 2, datum, Type.Missing);
                }
                Call(relations, "AddSymmetric", axisY, lines[3], lines[1], Type.Missing); Call(relations, "AddSymmetric", axisX, lines[0], lines[2], Type.Missing);
                driving.Add(Call(dimensions, "AddLength", lines[0])); driving.Add(Call(dimensions, "AddLength", lines[3]));
            }
            foreach (var dimension in driving) Set(dimension, "Constraint", true);
            if (Convert.ToInt32(Call(profile, "End", 1)) != 0) throw new InvalidOperationException("Solid Edge rejected the constrained concept profile for " + body.Id);
            var model = CadCallRef(Get(document, "Models"), "AddFiniteExtrudedProtrusion", [1], 1, CadProfiles(profile), 3, sz, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
            created = true; var feature = GetItem(Get(model, "ExtrudedProtrusions"), 1);
            Set(profile, "Visible", false); Set(feature, "Name", body.Id);
            var variables = Get(document, "Variables");
            List<object> All() { var list = Call(variables, "Query", "*", Type.Missing, Type.Missing, Type.Missing); return Enumerable.Range(1, Convert.ToInt32(Get(list, "Count"))).Select(i => GetItem(list, i)).ToList(); }
            object BySystem(Func<string, bool> match) => All().Single(v => match(Convert.ToString(Call(variables, "GetSystemName", v))!));
            var names = ConceptVariableNames(body); var targets = new List<(string Name, double Mm)>();
            for (int i = 0; i < driving.Count; i++) {
                string system = Convert.ToString(Get(driving[i], "SystemName"))!;
                Call(variables, "PutName", BySystem(s => s == system), names[i]); targets.Add((names[i], body.SizeMm[i]));
            }
            Call(variables, "PutName", BySystem(s => s.EndsWith("_FiniteDepth", StringComparison.Ordinal)), names[2]); targets.Add((names[2], body.SizeMm[2]));
            // Re-apply the intended sizes through the variables, then verify the solved solid.
            foreach (var (name, mm) in targets) Call(variables, "Edit", name, mm.ToString("R", CultureInfo.InvariantCulture) + " mm");
            int status = CadFeatureStatus(feature);
            if (status != 1216476310) throw new InvalidOperationException($"Concept feature {body.Id} has non-OK status {status}.");
            CadConceptVerifyBody(document, body);
        } catch {
            if (!created && profileSet != null) try { Call(profileSet, "Delete"); } catch { /* Keep the original error. */ }
            throw;
        }
    }
    // The body must be centred on the part origin with the manifest size along plane X, Y and normal.
    static void CadConceptVerifyBody(object document, ConceptBody body)
    {
        var solid = Get(GetItem(Get(document, "Models"), 1), "Body"); object[] range = [new double[3], new double[3]]; CadCallRef(solid, "GetRange", [0, 1], range);
        double[] lo = ((Array)range[0]).Cast<object>().Select(Convert.ToDouble).ToArray(), hi = ((Array)range[1]).Cast<object>().Select(Convert.ToDouble).ToArray();
        var m = body.GeometryMatrix;
        for (int axis = 0; axis < 3; axis++) {
            var values = new List<double>();
            for (int k = 0; k < 8; k++) { double[] p = [(k&1)==0?lo[0]:hi[0], (k&2)==0?lo[1]:hi[1], (k&4)==0?lo[2]:hi[2]]; values.Add(m[axis]*p[0]+m[4+axis]*p[1]+m[8+axis]*p[2]+m[12+axis]); }
            if (Math.Abs((values.Max() - values.Min()) * 1000 - body.SizeMm[axis]) > .01 || Math.Abs(values.Max() + values.Min()) * 1000 > .01)
                throw new InvalidOperationException($"Concept body {body.Id} is not the intended centred size along axis {"XYZ"[axis]}.");
        }
    }
}
