namespace PidSimulator.Models.Pid;

public sealed class PidParameters
{
    public double Kp { get; init; } = 300;
    public double Ki { get; init; } = 30;
    public double Kd { get; init; } = 20;

    public double OutputMin { get; init; } = -3000;
    public double OutputMax { get; init; } = 3000;

    /// <summary>增量式 PID 的单步输出变化限幅。</summary>
    public double DeltaMax { get; init; } = 1000;

    /// <summary>位置式 PID 对误差累加和 Σe 的限幅。</summary>
    public double IntegralMax { get; init; } = 200;

    /// <summary>采样周期，单位秒。</summary>
    public double SampleTime { get; init; } = 0.1;
}
