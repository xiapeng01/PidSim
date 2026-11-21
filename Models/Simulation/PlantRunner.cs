using System.Diagnostics;

namespace PidSimulator.Models.Simulation;

/// <summary>
/// 在独立线程上以固定 10ms 周期驱动仿真对象；开始仿真时启动，停止时退出。
/// </summary>
public sealed class PlantRunner : IDisposable
{
    private readonly Func<SimulationPlant> _plantProvider;
    private readonly Func<double> _commandProvider;
    private readonly object _gate = new();

    private Thread? _thread;
    private volatile bool _running;

    public PlantRunner(Func<SimulationPlant> plantProvider, Func<double> commandProvider)
    {
        _plantProvider = plantProvider;
        _commandProvider = commandProvider;
    }

    public bool IsRunning => _running;

    public Exception? LastError { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (_running)
                return;

            LastError = null;
            _running = true;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "PlantSimulationLoop"
            };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            _running = false;
            thread = _thread;
            _thread = null;
        }

        thread?.Join(500);
    }

    private void Loop()
    {
        const double tickMs = SimulationPlant.TickSeconds * 1000.0;
        var watch = Stopwatch.StartNew();
        double next = 0;

        try
        {
            while (_running)
            {
                double now = watch.Elapsed.TotalMilliseconds;
                if (now < next)
                {
                    Thread.Sleep(1);
                    continue;
                }

                _plantProvider().Step(_commandProvider(), SimulationPlant.TickSeconds);

                next += tickMs;
                if (next < now - tickMs * 5)
                    next = now + tickMs; // 线程被长时间挂起后不追赶补算
            }
        }
        catch (Exception ex)
        {
            LastError = ex;
            _running = false;
        }
    }

    public void Dispose() => Stop();
}
