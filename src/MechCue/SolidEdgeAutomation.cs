namespace MechCue;

public sealed partial class Bridge
{
    public static object CadGetAutomationSettings()
    {
        var app=CadApplication();
        return new {displayAlerts=Convert.ToBoolean(Get(app,"DisplayAlerts")),scope="Solid Edge application",note="False suppresses native alerts; some solver/license dialogs may still appear."};
    }
    public static object CadSetDisplayAlerts(bool displayAlerts)
    {
        var app=CadApplication();bool previous=Convert.ToBoolean(Get(app,"DisplayAlerts"));
        Set(app,"DisplayAlerts",displayAlerts);
        return new {previousDisplayAlerts=previous,displayAlerts=Convert.ToBoolean(Get(app,"DisplayAlerts")),scope="Solid Edge application",note="Applies to the whole running application until changed again. Restore the previous value after automation."};
    }
    static object CadWithSuppressedAlerts(bool suppressAlerts,Func<object> action)
    {
        if(!suppressAlerts)return action();
        var app=CadApplication();bool previous=Convert.ToBoolean(Get(app,"DisplayAlerts"));
        try {Set(app,"DisplayAlerts",false);return action();}
        finally {Set(app,"DisplayAlerts",previous);}
    }
}
