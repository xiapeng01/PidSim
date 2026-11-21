namespace PidSimulator.Models.Pid;

public static class PidControllerFactory
{
    public static IPidController Create(PidType type, PidParameters parameters)
    {
        return type switch
        {
            PidType.Positional => new PositionalPid(parameters),
            PidType.Incremental => new IncrementalPid(parameters),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "不支持的 PID 类型")
        };
    }
}
