using PidSimulator.Models;

namespace PidSimulator.Models.Simulation;

/// <summary>
/// 水泵/对象仿真模型：控制量线性映射后接一阶惯性环节。
/// </summary>
public static class PumpModel
{
    /// <summary>
    /// 对应前面提供的 PumpSim 方法，增加了可配置量程、双向范围和真实采样周期。
    /// </summary>
    public static double PumpSim(
        double inValue,
        ref double currentSystemValue,
        double dt,
        double minCommand = -3000,
        double maxCommand = 3000,
        double minOutput = -10,
        double maxOutput = 10,
        double timeConstant = 0.45)
    {
        if (maxCommand <= minCommand)
            throw new ArgumentException("MaxCommand 必须大于 MinCommand");
        if (maxOutput < minOutput)
            throw new ArgumentException("MaxOutput 不能小于 MinOutput");
        if (timeConstant <= 0 || double.IsNaN(timeConstant))
            throw new ArgumentException("TimeConstant 必须大于 0");
        if (dt <= 0 || double.IsNaN(dt))
            throw new ArgumentException("dt 必须大于 0");
        if (double.IsNaN(inValue) || double.IsNaN(currentSystemValue))
            throw new ArgumentException("inValue 和 currentSystemValue 不能为 NaN");

        double command = MathUtil.Clamp(inValue, minCommand, maxCommand);

        double target = minOutput
            + (command - minCommand) / (maxCommand - minCommand)
            * (maxOutput - minOutput);

        double alpha = 1.0 - Math.Exp(-dt / timeConstant);

        currentSystemValue = MathUtil.Clamp(currentSystemValue, minOutput, maxOutput);
        currentSystemValue += alpha * (target - currentSystemValue);

        return currentSystemValue;
    }
}
