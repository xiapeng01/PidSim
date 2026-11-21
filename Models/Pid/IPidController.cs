namespace PidSimulator.Models.Pid;

public interface IPidController
{
    PidType Type { get; }

    void Apply(PidParameters parameters);

    double Compute(double setpoint, double feedback);

    void Reset();
}
