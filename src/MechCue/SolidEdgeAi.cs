namespace MechCue;
public sealed partial class Bridge
{
    public static object ReadSolidEdge(string method,int partNumber=0,string expectedDocument="")
    {
        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(CLSIDFromProgID("SolidEdge.Application",out var id));GetActiveObject(ref id,IntPtr.Zero,out var application);
        var document=Get(application,"ActiveDocument");string name=Convert.ToString(Get(document,"Name")) ?? "";
        string fullName = CadName(document);
        if(!string.IsNullOrEmpty(expectedDocument) && !string.Equals(fullName,expectedDocument,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Active document differs from expectedDocument. Read the active document again.");
        if(method=="document")return new {name,fullName,readOnly=Convert.ToBoolean(Get(document,"ReadOnly")),dirty=Convert.ToBoolean(Get(document,"Dirty"))};
        if(method is "parts" or "select_part")
        {
            var occurrences=Get(document,"Occurrences");int count=Convert.ToInt32(Get(occurrences,"Count"));
            if(method=="select_part")
            {
                if(string.IsNullOrEmpty(expectedDocument))throw new ArgumentException("expectedDocument is required for selection");
                if(partNumber<1 || partNumber>count)throw new ArgumentOutOfRangeException(nameof(partNumber));
                var part=GetItem(occurrences,partNumber);var selection=Get(document,"SelectSet");Call(selection,"RemoveAll");Call(selection,"Add",part);
                return new {number=partNumber,name=Convert.ToString(Get(part,"Name")),fullName};
            }
            return Enumerable.Range(1,count).Select(i=>{var part=GetItem(occurrences,i);var matrix=Matrix(part);return new {number=i,name=Convert.ToString(Get(part,"Name")),x_mm=matrix[12]*1000,y_mm=matrix[13]*1000,z_mm=matrix[14]*1000};}).ToArray();
        }
        if(method=="variables")
        {
            var variables=Get(document,"Variables");var list=Call(variables,"Query","*",Type.Missing,Type.Missing,Type.Missing);int count=Convert.ToInt32(Get(list,"Count"));
            var results=new List<object>();
            for(int i=1;i<=count;i++){
                var variable=GetItem(list,i);string? error=null;string variableName="";double? value=null;int? units=null;string? formula=null;
                try{variableName=Convert.ToString(Get(variable,"Name"))??"";value=Convert.ToDouble(Get(variable,"Value"));units=Convert.ToInt32(Get(variable,"UnitsType"));formula=Convert.ToString(Get(variable,"Formula"));}catch(Exception ex){error=(ex.InnerException ?? ex).Message;}
                results.Add(new {name=variableName,value,unitsType=units,formula,error});
            }
            return new {fullName,valuesUse="Solid Edge internal units (metres/radians for distance/angle); unitsType is the native unit enum",variables=results};
        }
        throw new InvalidOperationException("Unknown Solid Edge command");
    }
}