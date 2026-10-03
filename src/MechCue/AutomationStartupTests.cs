namespace MechCue;

public partial class MainForm
{
    internal void VerifyAutomationStartup(string expectedDocument)
    {
        // OpenForAutomation can connect before WinForms delivers Shown. Keep
        // Guard errors in the test rather than displaying a modal message box.
        aiExecuting = true; aiError = null;
        try
        {
            OpenForAutomation(expectedDocument);
            Application.DoEvents();
            if (aiError != null) throw new Exception("Automation startup attempted a second connection", aiError);
            if (!documentReady || !bridge.Connected)
                throw new Exception("Automation startup must retain a save-ready document connection");
            SaveToDocument();
        }
        finally { aiExecuting = false; aiError = null; }
    }
}

public static partial class SelfTest
{
    static void TestAutomationStartup()
    {
        var document = new FakeDocument();
        var app = new FakeApplication { ActiveDocument = document };
        app.OpenDocuments.Items.Add(document);
        using var form = new MainForm(hostedApplication: app);
        try { form.VerifyAutomationStartup(document.FullName); }
        finally { form.ShutdownFromHost(); }
    }
}
