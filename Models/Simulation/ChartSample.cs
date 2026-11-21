namespace PidSimulator.Models.Simulation;

public sealed record ChartSample(
    double Time,
    double Setpoint,
    double ProcessValue,
    double Output);
