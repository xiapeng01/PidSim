using PidSimulator.Models;

namespace PidSimulator.Models.Simulation;

/// <summary>
/// 仿真对象基类：子类实现单步运算，基类负责把结果按 10ms 存入滞后缓存。
/// </summary>
public abstract class SimulationPlant
{
    /// <summary>仿真运算周期（秒）。</summary>
    public const double TickSeconds = 0.01;

    /// <summary>历史输出上限，避免滞后时间过大导致内存无限增长。</summary>
    public const int MaxHistoryLength = 100_000;

    /// <summary>纯滞后时间（秒，可为小数），0 表示不启用。</summary>
    public double DelayTime { get; set; }

    /// <summary>滞后对应的历史下标：每 10ms 一个点，0.01s → 1，1s → 100。</summary>
    public int DelayIndex
    {
        get
        {
            if (DelayTime <= 0 || double.IsNaN(DelayTime))
                return 0;

            double index = Math.Round(DelayTime / TickSeconds, MidpointRounding.AwayFromZero);
            if (index >= int.MaxValue)
                return int.MaxValue;
            return (int)index;
        }
    }

    public int HistoryCount
    {
        get { lock (Gate) return _history.Count; }
    }

    private readonly List<double> _history = new();
    protected readonly object Gate = new();

    public void Reset(double initialValue)
    {
        lock (Gate)
        {
            _history.Clear();
            ResetCore(initialValue);
        }
    }

    public void ClampCurrentValue()
    {
        lock (Gate)
        {
            ClampValueCore();
        }
    }

    /// <summary>
    /// 执行一次运算（dt 固定为 10ms），把输出插到下标 0，超出滞后长度的旧数据从尾部丢弃。
    /// I/O 型子类可重写本方法，在不持有 Gate 的情况下完成运算后再调用 StoreOutput。
    /// </summary>
    public virtual double Step(double command, double dt)
    {
        lock (Gate)
        {
            double output = ComputeOutput(command, dt);
            StoreOutputCore(output);
            return output;
        }
    }

    /// <summary>只把已算出的输出写入历史缓存，供重写 Step 的子类使用。</summary>
    protected void StoreOutput(double output)
    {
        lock (Gate)
            StoreOutputCore(output);
    }

    private void StoreOutputCore(double output)
    {
        int capacity = (int)Math.Min((long)DelayIndex + 1, MaxHistoryLength);
        _history.Insert(0, output);
        if (_history.Count > capacity)
            _history.RemoveRange(capacity, _history.Count - capacity);
    }

    /// <summary>按滞后时间取历史输出（流量/温度读数）；历史长度不足时输出 0。</summary>
    public double ReadDelayed()
    {
        lock (Gate)
        {
            int index = DelayIndex;
            return index < _history.Count ? _history[index] : 0.0;
        }
    }

    /// <summary>清空运算数据（不给计时器复位），之后一段时间读数为 0。</summary>
    public void ClearHistory()
    {
        lock (Gate)
        {
            _history.Clear();
        }
    }

    /// <summary>执行一次模型运算，返回未滞后的输出。</summary>
    protected abstract double ComputeOutput(double command, double dt);

    /// <summary>把对象状态重置到初始值（历史缓存由基类清空）。</summary>
    protected abstract void ResetCore(double initialValue);

    /// <summary>参数变化后把状态限制在有效范围内。</summary>
    protected virtual void ClampValueCore()
    {
    }
}
