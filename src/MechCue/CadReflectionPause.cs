namespace MechCue;

public partial class MainForm
{
    int cadReflectionPauseDepth;
    IDisposable PauseCadReflection() => new CadReflectionPause(this);

    // Keep the user's Apply selection while commands update the chart or save
    // a reference pose. Nested/reentrant commands must not resume CAD writes early.
    sealed class CadReflectionPause : IDisposable
    {
        MainForm? owner;
        public CadReflectionPause(MainForm owner)
        {
            this.owner = owner;
            owner.cadReflectionPauseDepth++;
        }
        public void Dispose()
        {
            if (owner == null) return;
            owner.cadReflectionPauseDepth--;
            owner = null;
        }
    }
}
