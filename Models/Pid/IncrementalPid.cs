using PidSimulator.Models;

namespace PidSimulator.Models.Pid;

public sealed class IncrementalPid : IPidController
{
    public PidType Type => PidType.Incremental;

    public double Kp { get; private set; }
    public double Ki { get; private set; }
    public double Kd { get; private set; }

    public double OutputMin { get; private set; }
    public double OutputMax { get; private set; }
    public double DeltaMax { get; private set; }
    public double SampleTime { get; private set; }

    /// <summary>限幅前的原始增量。</summary>
    public double RawDelta { get; private set; }

    /// <summary>限幅并输出裁剪后，实际施加的增量。</summary>
    public double LastDelta { get; private set; }

    public double Output { get; private set; }

    private double _e1;
    private double _e2;

    public IncrementalPid(PidParameters parameters)
    {
        Apply(parameters);
        Reset();
    }

    public void Apply(PidParameters parameters)
    {
        Validate(parameters);

        Kp = parameters.Kp;
        Ki = parameters.Ki;
        Kd = parameters.Kd;
        OutputMin = parameters.OutputMin;
        OutputMax = parameters.OutputMax;
        DeltaMax = parameters.DeltaMax;
        SampleTime = parameters.SampleTime;

        Output = MathUtil.Clamp(Output, OutputMin, OutputMax);
    }

    public void Reset()
    {
        _e1 = 0;
        _e2 = 0;
        RawDelta = 0;
        LastDelta = 0;
        Output = 0;
    }

    public double Compute(double setpoint, double feedback)
    {
        if (double.IsNaN(setpoint) || double.IsNaN(feedback))
            throw new ArgumentException("setpoint 和 feedback 不能为 NaN");

        double e0 = setpoint - feedback;

        double rawDelta =
              Kp * (e0 - _e1)
            + Ki * SampleTime * e0
            + Kd / SampleTime * (e0 - 2.0 * _e1 + _e2);

        RawDelta = rawDelta;

        double delta = MathUtil.Clamp(rawDelta, -DeltaMax, DeltaMax);

        double previousOutput = Output;
        double u = MathUtil.Clamp(previousOutput + delta, OutputMin, OutputMax);

        LastDelta = u - previousOutput;
        Output = u;

        _e2 = _e1;
        _e1 = e0;

        return Output;
    }

    private static void Validate(PidParameters p)
    {
        if (p.OutputMin > p.OutputMax)
            throw new ArgumentException("输出下限不能大于输出上限");
        if (p.DeltaMax < 0 || double.IsNaN(p.DeltaMax))
            throw new ArgumentException("DeltaMax 必须是非负有效数值");
        if (p.SampleTime <= 0 || double.IsNaN(p.SampleTime))
            throw new ArgumentException("SampleTime 必须大于 0");
    }
}
