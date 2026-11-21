namespace PidSimulator.Models.Configuration;

/// <summary>
/// .pidsim 参数文件格式：只保存可配置参数，不保存运行数据或曲线。
/// </summary>
public sealed class PidSimParameterFile
{
    public int FormatVersion { get; set; } = 1;

    public string SavedAt { get; set; } = string.Empty;

    public string SimulationModel { get; set; } = "水泵流量计";

    public string PidType { get; set; } = "位置式";

    public double Setpoint { get; set; } = 5;

    public double InitialValue { get; set; }

    public double Kp { get; set; } = 100;

    public double Ki { get; set; } = 200;

    public double Kd { get; set; }

    public double OutputMin { get; set; } = -3000;

    public double OutputMax { get; set; } = 3000;

    public double DeltaMax { get; set; } = 1000;

    public double IntegralMax { get; set; } = 200;

    public double SampleTime { get; set; } = 0.1;

    public double PumpMinCommand { get; set; } = -3000;

    public double PumpMaxCommand { get; set; } = 3000;

    public double PumpMinOutput { get; set; } = -10;

    public double PumpMaxOutput { get; set; } = 10;

    public double PumpTimeConstant { get; set; } = 0.45;

    public double PumpDelayTime { get; set; }

    public double AmbientTemperature { get; set; } = 25;

    public double MaxTemperatureDelta { get; set; } = 60;

    public double MaxTemperatureCommand { get; set; } = 3000;

    public double TemperatureTimeConstant { get; set; } = 30;

    public double TemperatureDelayTime { get; set; }

    public bool MonitorOnlyInput { get; set; }

    public bool ShareModbusConnection { get; set; }

    public string InputInterfaceName { get; set; } = "ModbusTcp";

    public string InputIp { get; set; } = "127.0.0.1";

    public int InputPort { get; set; } = 502;

    public string InputSerialPort { get; set; } = "COM1";

    public int InputBaudRate { get; set; } = 9600;

    public int InputDataBits { get; set; } = 8;

    public string InputParityName { get; set; } = "None";

    public string InputStopBitsName { get; set; } = "One";

    public string InputEndianName { get; set; } = "CDAB(低字在前,默认)";

    public int InputStation { get; set; } = 1;

    public int InputRegisterAddress { get; set; }

    public string InputRegisterAreaName { get; set; } = "保持寄存器(可读写)";

    public string InputValueTypeName { get; set; } = "Float";

    public int InputPeriodMs { get; set; } = 100;

    public string OutputInterfaceName { get; set; } = "ModbusTcp";

    public string OutputIp { get; set; } = "127.0.0.1";

    public int OutputPort { get; set; } = 502;

    public string OutputSerialPort { get; set; } = "COM1";

    public int OutputBaudRate { get; set; } = 9600;

    public int OutputDataBits { get; set; } = 8;

    public string OutputParityName { get; set; } = "None";

    public string OutputStopBitsName { get; set; } = "One";

    public string OutputEndianName { get; set; } = "CDAB(低字在前,默认)";

    public int OutputStation { get; set; } = 1;

    public int OutputRegisterAddress { get; set; } = 1;

    public string OutputRegisterAreaName { get; set; } = "保持寄存器(可读写)";

    public string OutputValueTypeName { get; set; } = "Float";

    public int OutputPeriodMs { get; set; } = 100;
}
