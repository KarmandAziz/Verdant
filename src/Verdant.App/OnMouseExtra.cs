namespace Verdant.App;

internal interface IOnMousePlayback : IDisposable
{
    bool Armed { get; }
    void Arm();
    void Stop();
    void CancelPlayback();
}
// Serializes HID work away from the low-level input hook. Runtime remains the
// owner of the mouse hold; this service owns only the firmware key sequence.
internal sealed class OnMouseExtra : IDisposable
{
    internal sealed record State(Binding? Binding,bool CanArm,int Generation,long Activation,bool Active);
    private readonly Func<State> read;
    private readonly Action<string> error;
    private readonly Func<Binding,IOnMousePlayback> create;
    private readonly Action<Binding,bool> release;
    private readonly CancellationTokenSource stop=new();
    private readonly Task worker;
    private IOnMousePlayback? session;
    private Binding? binding;
    private bool running;
    private int generation,failedGeneration=int.MinValue;
    private long activation;
    private volatile bool ready,stopped=true;
    internal bool Ready=>ready;
    internal bool Stopped=>stopped;
    public OnMouseExtra(Func<State> read,Action<string> error,
        Func<Binding,IOnMousePlayback>? create=null,Action<Binding,bool>? release=null)
    {
        this.read=read; this.error=error;
        this.create=create??(b=>BasiliskPlayback.CreateContinuous(b));
        this.release=release??((b,mouse)=>{ foreach(var key in b.ExtraKeys) Native.Key(key,false); if(mouse) Native.Mouse(4,false); });
        worker=Task.Run(Work);
    }
    private void ReleaseKeys(bool mouse=false)
    {
        if(binding==null) return;
        release(binding,mouse);
    }
    private void Close()
    {
        ready=false;
        var current=session; session=null;
        // An armed but unused macro owns no Windows input. In particular,
        // switching to the UAC desktop must not send unnecessary releases and
        // turn a successfully restored idle session into an injection failure.
        var needsRelease=running || current is { Armed:true } && read().Activation!=activation;
        try { current?.Dispose(); }
        finally {
            if(current!=null && needsRelease && !read().Active) ReleaseKeys(true);
            running=false; binding=null;
        }
        stopped=true;
    }
    private async Task Work()
    {
        try
        {
            while(!stop.IsCancellationRequested)
            {
                var state=read();
                try
                {
                    if(session!=null && (!state.CanArm || state.Generation!=generation || !ReferenceEquals(state.Binding,binding))) Close();
                    if(!state.CanArm || state.Binding==null) failedGeneration=int.MinValue;
                    else if(session==null && !state.Active && failedGeneration!=state.Generation)
                    {
                        binding=state.Binding; generation=state.Generation;
                        stopped=false;
                        session=create(binding);
                        // Allocation can take time. Do not replace a mapping if the user
                        // started a normal hold or changed profile while it was prepared.
                        var fresh=read();
                        if(!fresh.CanArm || fresh.Active || fresh.Generation!=generation || !ReferenceEquals(fresh.Binding,binding)) Close();
                        else { activation=fresh.Activation; session.Arm(); ready=true; }
                    }
                    if(session!=null)
                    {
                        state=read();
                        if(session.Armed && state.Activation!=activation)
                        {
                            activation=state.Activation;
                            ready=false;
                            session.Stop(); // Restore standard Mouse5 for the real OFF click.
                            running=true;
                        }
                        if(running && !state.Active)
                        {
                            session.CancelPlayback(); ReleaseKeys(!read().Active);
                            running=false;
                        }
                        var fresh=read();
                        if(!running && !session.Armed && !fresh.Active && fresh.CanArm &&
                            fresh.Generation==generation && ReferenceEquals(fresh.Binding,binding))
                        { activation=fresh.Activation; session.Arm(); ready=true; }
                    }
                }
                catch(Exception ex)
                {
                    failedGeneration=state.Generation;
                    try { Close(); } catch(Exception cleanup) { ex=new AggregateException(ex,cleanup); }
                    error("Hardware extra key: "+ex.Message);
                }
                await Task.Delay(10,stop.Token);
            }
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
        finally { try { Close(); } catch(Exception ex) { error("Hardware cleanup: "+ex.Message); } }
    }
    public void Dispose() { stop.Cancel(); worker.GetAwaiter().GetResult(); stop.Dispose(); }
}
