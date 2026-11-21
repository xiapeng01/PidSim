using PidSimulator.Models;

namespace PidSimulator.Models.Simulation;

/// <summary>
/// 温控对象：指令换算成加热/制冷功率，向目标温度一阶惯性逼近。
/// </summary>
public abstract class TemperaturePlant : SimulationPlant
{
    /// <summary>环境温度（指令为 0 时的稳态温度），单位 °C。</summary>
    public double AmbientTemperature { get; set; } = 25;

    /// <summary>满功率时相对环境温度的最大温差，单位 °C。</summary>
    public double MaxDeltaTemperature { get; set; } = 60;

    /// <summary>满功率对应的指令值。</summary>
    public double MaxCommand { get; set; } = 3000;

    /// <summary>热惯性时间常数，单位秒。</summary>
    public double TimeConstant { get; set; } = 30;

    /// <summary>作用方向：+1 加热，-1 制冷。</summary>
    protected abstract double Direction { get; }

    private double _value;

    public TemperaturePlant(double initialValue)
    {
        Reset(initialValue);
    }

    protected override void ResetCore(double initialValue)
    {
        _value = initialValue;
    }

    protected override double ComputeOutput(double command, double dt)
    {
        double drive = Direction * command;
        double duty = MathUtil.Clamp(drive / MaxCommand, 0.0, 1.0);

        double target = AmbientTemperature + Direction * duty * MaxDeltaTemperature;
        double alpha = 1.0 - Math.Exp(-dt / TimeConstant);

        _value += alpha * (target - _value);
        return _value;
    }
}

/// <summary>加热模型：正指令升温，环境温度为下限。</summary>
public sealed class HeatingPlant : TemperaturePlant
{
    public HeatingPlant(double initialValue) : base(initialValue) { }

    protected override double Direction => 1;
}

/// <summary>制冷模型：负指令降温，环境温度为上限。</summary>
public sealed class CoolingPlant : TemperaturePlant
{
    public CoolingPlant(double initialValue) : base(initialValue) { }

    protected override double Direction => -1;
}
