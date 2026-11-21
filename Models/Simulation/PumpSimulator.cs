using PidSimulator.Models;

namespace PidSimulator.Models.Simulation;

public sealed class PumpSimulator : SimulationPlant
{
    public double MinCommand { get; set; } = -3000;
    public double MaxCommand { get; set; } = 3000;
    public double MinOutput { get; set; } = -10;
    public double MaxOutput { get; set; } = 10;
    public double TimeConstant { get; set; } = 0.45;

    private double _value;

    public PumpSimulator(double initialValue = 0)
    {
        Reset(initialValue);
    }

    protected override void ResetCore(double initialValue)
    {
        _value = MathUtil.Clamp(initialValue, MinOutput, MaxOutput);
    }

    protected override void ClampValueCore()
    {
        _value = MathUtil.Clamp(_value, MinOutput, MaxOutput);
    }

    protected override double ComputeOutput(double command, double dt)
    {
        _value = PumpModel.PumpSim(
            command,
            ref _value,
            dt,
            MinCommand,
            MaxCommand,
            MinOutput,
            MaxOutput,
            TimeConstant);

        return _value;
    }
}
