using System.Threading;

public abstract class StopableThread
{ 
    private readonly Thread _thread;
    protected bool Running { get; private set; }

    protected StopableThread()
    {
        _thread = new Thread(Run);
    }

    protected abstract void Run();

    public void Start()
    {
        Running = true;
        _thread.Start();
    }

    public void Stop()
    {
        Running = false;
        _thread.Join();
    }
}
