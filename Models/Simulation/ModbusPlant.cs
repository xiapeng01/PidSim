using System.Diagnostics;
using NModbus;
using NModbus.Extensions;

namespace PidSimulator.Models.Simulation;

/// <summary>
/// Modbus 通信设备：按周期从输入通道读取过程值，按周期把 PID 输出写入输出通道。
/// 输入与输出可以使用不同的链路类型和参数。
/// </summary>
public sealed class ModbusPlant : SimulationPlant, IDisposable
{
    private readonly object _commGate = new();
    private readonly Stopwatch _inputWatch = Stopwatch.StartNew();
    private readonly Stopwatch _outputWatch = Stopwatch.StartNew();

    private ModbusPlantConfig _config = new();
    private IModbusMaster? _inputMaster;
    private IModbusMaster? _outputMaster;
    private double _inputValue;
    private bool _isOpen;
    private int _configChanged;
    private int _resetPending;
    private double _pendingInitialValue;

    /// <summary>当前已成功读取到的通信输入值。</summary>
    public double InputValue
    {
        get { lock (_commGate) return _inputValue; }
    }

    /// <summary>只替换配置引用，重连由后台运算线程完成，UI 线程不等待通信锁。</summary>
    public void Configure(ModbusPlantConfig config)
    {
        if (Volatile.Read(ref _config).SameAs(config))
            return;

        Volatile.Write(ref _config, config);
        Interlocked.Exchange(ref _configChanged, 1);
    }

    public void Open()
    {
        lock (_commGate)
            OpenInternal();
    }

    public void Close()
    {
        lock (_commGate)
            CloseInternal();
    }

    /// <summary>
    /// 通信读写不持有 SimulationPlant.Gate，避免阻塞 UI 的 ReadDelayed；
    /// 只在通信完成后把结果写入历史缓存。
    /// </summary>
    public override double Step(double command, double dt)
    {
        double output = ComputeModbusOutput(command);
        StoreOutput(output);
        return output;
    }

    protected override double ComputeOutput(double command, double dt) => ComputeModbusOutput(command);

    private double ComputeModbusOutput(double command)
    {
        if (Interlocked.Exchange(ref _configChanged, 0) == 1)
        {
            lock (_commGate)
                CloseInternal();
        }

        if (Interlocked.Exchange(ref _resetPending, 0) == 1)
        {
            double initial = Volatile.Read(ref _pendingInitialValue);
            lock (_commGate)
            {
                _inputValue = initial;
                _inputWatch.Restart();
                _outputWatch.Restart();
            }
        }

        ModbusPlantConfig config = Volatile.Read(ref _config);

        lock (_commGate)
        {
            OpenInternal();

            if (_inputWatch.Elapsed.TotalMilliseconds >= config.Input.PeriodMs)
            {
                _inputValue = ReadValue(_inputMaster!, config.Input);
                _inputWatch.Restart();
            }

            if (config.WriteOutput
                && _outputMaster is not null
                && _outputWatch.Elapsed.TotalMilliseconds >= config.Output.PeriodMs)
            {
                WriteValue(_outputMaster, config.Output, command);
                _outputWatch.Restart();
            }

            return _inputValue;
        }
    }

    protected override void ResetCore(double initialValue)
    {
        Volatile.Write(ref _pendingInitialValue, initialValue);
        Interlocked.Exchange(ref _resetPending, 1);
    }

    private static double ReadValue(IModbusMaster master, ModbusChannelConfig config)
    {
        int station = config.Station;
        int address = config.RegisterAddress;

        if (config.RegisterArea == ModbusRegisterArea.InputRegisters)
        {
            return config.ValueType switch
            {
                ModbusValueType.Int16 => master.ReadInt16R(station, address),
                ModbusValueType.UInt16 => master.ReadUInt16R(station, address),
                ModbusValueType.Int32 => master.ReadInt32R(station, address),
                ModbusValueType.UInt32 => master.ReadUInt32R(station, address),
                ModbusValueType.Float => master.ReadFloatR(station, address),
                _ => throw new ArgumentOutOfRangeException(nameof(config), config.ValueType, "不支持的数据类型")
            };
        }

        return config.ValueType switch
        {
            ModbusValueType.Int16 => master.ReadInt16RW(station, address),
            ModbusValueType.UInt16 => master.ReadUInt16RW(station, address),
            ModbusValueType.Int32 => master.ReadInt32RW(station, address),
            ModbusValueType.UInt32 => master.ReadUInt32RW(station, address),
            ModbusValueType.Float => master.ReadFloatRW(station, address),
            _ => throw new ArgumentOutOfRangeException(nameof(config), config.ValueType, "不支持的数据类型")
        };
    }

    private static void WriteValue(IModbusMaster master, ModbusChannelConfig config, double value)
    {
        if (config.RegisterArea != ModbusRegisterArea.HoldingRegisters)
            throw new InvalidOperationException("输出通道只能写入保持寄存器");

        int station = config.Station;
        int address = config.RegisterAddress;

        switch (config.ValueType)
        {
            case ModbusValueType.Int16:
                master.WriteInt16RW(station, address, (short)Math.Clamp(value, short.MinValue, short.MaxValue));
                break;
            case ModbusValueType.UInt16:
                master.WriteUInt16RW(station, address, (ushort)Math.Clamp(value, ushort.MinValue, ushort.MaxValue));
                break;
            case ModbusValueType.Int32:
                master.WriteInt32RW(station, address, (int)Math.Clamp(value, int.MinValue, int.MaxValue));
                break;
            case ModbusValueType.UInt32:
                master.WriteUInt32RW(station, address, (uint)Math.Clamp(value, uint.MinValue, uint.MaxValue));
                break;
            case ModbusValueType.Float:
                master.WriteFloatRW(station, address, (float)value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(config), config.ValueType, "不支持的数据类型");
        }
    }

    private void OpenInternal()
    {
        if (_isOpen)
            return;

        try
        {
            ModbusPlantConfig config = Volatile.Read(ref _config);
            _inputMaster = CreateMaster(config.Input);
            _outputMaster = config.WriteOutput
                ? config.ShareConnection ? _inputMaster : CreateMaster(config.Output)
                : null;
            _inputWatch.Restart();
            _outputWatch.Restart();
            _isOpen = true;
        }
        catch
        {
            CloseInternal();
            throw;
        }
    }

    private void CloseInternal()
    {
        (_inputMaster as IDisposable)?.Dispose();
        if (!ReferenceEquals(_outputMaster, _inputMaster))
            (_outputMaster as IDisposable)?.Dispose();
        _inputMaster = null;
        _outputMaster = null;
        _isOpen = false;
    }

    private static IModbusMaster CreateMaster(ModbusChannelConfig config)
    {
        IModbusMaster master = config.Interface switch
        {
            ModbusInterface.ModbusRtu => Tools.CreateRtuMaster(
                config.SerialPort,
                config.BaudRate,
                config.DataBits,
                config.Parity,
                config.StopBits,
                config.TimeoutMs),
            ModbusInterface.ModbusTcp => Tools.CreateTcpMaster(
                config.Ip,
                config.Port,
                config.TimeoutMs),
            ModbusInterface.ModbusRtuOverTcp => Tools.CreateRtuOverTcpMaster(
                config.Ip,
                config.Port,
                config.TimeoutMs),
            _ => throw new ArgumentOutOfRangeException(nameof(config), config.Interface, "不支持的 Modbus 接口类型")
        };

        master.SetEndian(config.Endian);
        return master;
    }

    public void Dispose() => Close();
}
