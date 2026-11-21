using System.IO.Ports;
using NModbus.Extensions;

namespace PidSimulator.Models.Simulation;

/// <summary>Modbus 链路类型。</summary>
public enum ModbusInterface
{
    ModbusRtu,
    ModbusTcp,
    ModbusRtuOverTcp
}

/// <summary>寄存器区域。</summary>
public enum ModbusRegisterArea
{
    /// <summary>输入寄存器（只读，功能码 4）。</summary>
    InputRegisters,

    /// <summary>保持寄存器（可读写，功能码 3/6/16）。</summary>
    HoldingRegisters
}

/// <summary>寄存器数据类型。</summary>
public enum ModbusValueType
{
    Int16,
    UInt16,
    Int32,
    UInt32,
    Float
}

/// <summary>单条 Modbus 通道参数：输入读取与 PID 输出写入各自独立配置。</summary>
public sealed class ModbusChannelConfig
{
    public ModbusInterface Interface { get; set; } = ModbusInterface.ModbusTcp;

    public string Ip { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 502;

    public string SerialPort { get; set; } = "COM1";

    public int BaudRate { get; set; } = 9600;

    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.None;

    public StopBits StopBits { get; set; } = StopBits.One;

    public byte Station { get; set; } = 1;

    /// <summary>寄存器起始地址。</summary>
    public ushort RegisterAddress { get; set; }

    public ModbusRegisterArea RegisterArea { get; set; } = ModbusRegisterArea.HoldingRegisters;

    public ModbusValueType ValueType { get; set; } = ModbusValueType.Float;

    /// <summary>32 位值与 16 位值的字节序，默认 CDAB。</summary>
    public EnumEndian Endian { get; set; } = EnumEndian.CDAB;

    /// <summary>当前数据类型占用的寄存器个数。</summary>
    public int RegisterCount => ValueType is ModbusValueType.Int32 or ModbusValueType.UInt32 or ModbusValueType.Float ? 2 : 1;

    /// <summary>读写周期（毫秒）。</summary>
    public int PeriodMs { get; set; } = 100;

    /// <summary>连接超时（毫秒）。</summary>
    public int TimeoutMs { get; set; } = 3000;

    public ModbusChannelConfig Clone() => (ModbusChannelConfig)MemberwiseClone();

    public bool SameAs(ModbusChannelConfig other)
    {
        return Interface == other.Interface
            && Ip == other.Ip
            && Port == other.Port
            && SerialPort == other.SerialPort
            && BaudRate == other.BaudRate
            && DataBits == other.DataBits
            && Parity == other.Parity
            && StopBits == other.StopBits
            && Station == other.Station
            && RegisterAddress == other.RegisterAddress
            && RegisterArea == other.RegisterArea
            && ValueType == other.ValueType
            && Endian == other.Endian
            && PeriodMs == other.PeriodMs
            && TimeoutMs == other.TimeoutMs;
    }
}

/// <summary>Modbus 仿真对象配置。</summary>
public sealed class ModbusPlantConfig
{
    public ModbusChannelConfig Input { get; set; } = new();

    public ModbusChannelConfig Output { get; set; } = new();

    /// <summary>是否把 PID 输出按周期写入通信；仅监控输入时为 false。</summary>
    public bool WriteOutput { get; set; } = true;

    /// <summary>输入输出是否共用同一条通信链路。</summary>
    public bool ShareConnection { get; set; }

    public bool SameAs(ModbusPlantConfig other)
    {
        return WriteOutput == other.WriteOutput
            && ShareConnection == other.ShareConnection
            && Input.SameAs(other.Input)
            && Output.SameAs(other.Output);
    }
}
