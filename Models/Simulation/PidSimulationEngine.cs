using PidSimulator.Models.Pid;

namespace PidSimulator.Models.Simulation;

public sealed class PidSimulationEngine
{
    public PidType PidType { get; private set; }
    public IPidController? Controller { get; private set; }

    /// <summary>当前仿真对象（水泵流量计 / 加热 / 制冷），仅允许在停止状态切换。</summary>
    public SimulationPlant Plant { get; private set; } = new PumpSimulator();

    public double Setpoint { get; set; }
    public double SampleTime { get; private set; } = 0.1;
    public double PidOutput => Volatile.Read(ref _pidOutput);
    public double ProcessValue => Plant.ReadDelayed();
    public double Error => Setpoint - ProcessValue;
    public double Time { get; private set; }

    private double _pidOutput;

    public void Configure(PidType type, PidParameters parameters, double initialValue, double time)
    {
        PidType = type;
        Controller = PidControllerFactory.Create(type, parameters);
        SampleTime = parameters.SampleTime;
        Reset(initialValue, time);
    }

    public void ApplyParameters(PidParameters parameters)
    {
        SampleTime = parameters.SampleTime;
        Controller?.Apply(parameters);
    }

    /// <summary>切换仿真对象并复位状态，保持当前时间轴。</summary>
    public void SwitchPlant(SimulationPlant plant, double initialValue)
    {
        Plant = plant;
        Reset(initialValue, Time);
    }

    public void Reset(double initialValue, double time)
    {
        Controller?.Reset();
        Plant.Reset(initialValue);
        Volatile.Write(ref _pidOutput, 0);
        Time = time;
    }

    /// <summary>
    /// 计算一次 PID 输出；仿真对象的推进由独立线程按 10ms 周期完成。
    /// </summary>
    public ChartSample Step()
    {
        if (Controller is null)
            throw new InvalidOperationException("仿真尚未配置 PID 控制器");

        double output = Controller.Compute(Setpoint, ProcessValue);
        Volatile.Write(ref _pidOutput, output);

        Time += SampleTime;

        return new ChartSample(Time, Setpoint, ProcessValue, output);
    }
}
