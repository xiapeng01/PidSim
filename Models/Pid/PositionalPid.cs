using PidSimulator.Models;

namespace PidSimulator.Models.Pid;

public sealed class PositionalPid : IPidController
{
    public PidType Type => PidType.Positional;

    public double Kp { get; private set; }
    public double Ki { get; private set; }
    public double Kd { get; private set; }

    public double OutputMin { get; private set; }
    public double OutputMax { get; private set; }
    public double IntegralMax { get; private set; }
    public double SampleTime { get; private set; }

    public double Integral { get; private set; }
    public double IntegralTerm { get; private set; }
    public double UnsaturatedOutput { get; private set; }
    public bool IsSaturated { get; private set; }
    public double Output { get; private set; }

    private double _lastError;

    public PositionalPid(PidParameters parameters)
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
        IntegralMax = parameters.IntegralMax;
        SampleTime = parameters.SampleTime;

        Integral = MathUtil.Clamp(Integral, -IntegralMax, IntegralMax);
        IntegralTerm = Ki * SampleTime * Integral;
        Output = MathUtil.Clamp(Output, OutputMin, OutputMax);
    }

    public void Reset()
    {
        Integral = 0;
        IntegralTerm = 0;
        UnsaturatedOutput = 0;
        IsSaturated = false;
        Output = 0;
        _lastError = 0;
    }

    public double Compute(double setpoint, double feedback)
    {
        if (double.IsNaN(setpoint) || double.IsNaN(feedback))
            throw new ArgumentException("setpoint 和 feedback 不能为 NaN");

        double error = setpoint - feedback;

        double p = Kp * error;
        double d = Kd / SampleTime * (error - _lastError);

        double nextIntegral = MathUtil.Clamp(Integral + error, -IntegralMax, IntegralMax);
        double i = Ki * SampleTime * nextIntegral;

        double uUnsat = p + i + d;
        double u = MathUtil.Clamp(uUnsat, OutputMin, OutputMax);

        // 抗积分饱和：误差仍把输出往同一饱和方向推时，冻结积分。
        bool pushingFurther =
            (uUnsat > OutputMax && error > 0) ||
            (uUnsat < OutputMin && error < 0);

        if (pushingFurther)
        {
            nextIntegral = Integral;
            i = Ki * SampleTime * nextIntegral;
            uUnsat = p + i + d;
            u = MathUtil.Clamp(uUnsat, OutputMin, OutputMax);
        }

        Integral = nextIntegral;
        IntegralTerm = i;
        _lastError = error;

        UnsaturatedOutput = uUnsat;
        Output = u;
        IsSaturated = uUnsat > OutputMax || uUnsat < OutputMin;

        return Output;
    }

    private static void Validate(PidParameters p)
    {
        if (p.OutputMin > p.OutputMax)
            throw new ArgumentException("输出下限不能大于输出上限");
        if (p.IntegralMax < 0 || double.IsNaN(p.IntegralMax))
            throw new ArgumentException("IntegralMax 必须是非负有效数值");
        if (p.SampleTime <= 0 || double.IsNaN(p.SampleTime))
            throw new ArgumentException("SampleTime 必须大于 0");
    }
}
