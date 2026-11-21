using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using NModbus.Extensions;
using PidSimulator.Models.Configuration;
using PidSimulator.Models.Pid;
using PidSimulator.Models.Simulation;
using PidSimulator.Services;
using PidSimulator.ViewModels.Base;

namespace PidSimulator.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxSamples = 3000;

    private readonly PidSimulationEngine _engine = new();
    private readonly PlantRunner _plantRunner;
    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<ChartSample> _samples = new();

    private bool _isInitializing = true;
    private bool _needsControllerRecreate;
    private double _chartOrigin;
    private bool _isRunning;
    private AppTheme _selectedTheme = AppTheme.Light;

    private PidType _selectedPidType = PidType.Positional;
    private double _kp = 100;
    private double _ki = 200;
    private double _kd = 0;
    private double _outputMin = -3000;
    private double _outputMax = 3000;
    private double _deltaMax = 1000;
    private double _integralMax = 200;
    private double _sampleTime = 0.1;
    private double _setpoint = 5;
    private double _initialValue;

    private double _pumpMinCommand = -3000;
    private double _pumpMaxCommand = 3000;
    private double _pumpMinOutput = -10;
    private double _pumpMaxOutput = 10;
    private double _pumpTimeConstant = 0.45;
    private double _pumpDelayTime;

    private SimulationModel _selectedSimulationModel = SimulationModel.PumpFlow;
    private double _ambientTemperature = 25;
    private double _maxTemperatureDelta = 60;
    private double _maxTemperatureCommand = 3000;
    private double _temperatureTimeConstant = 30;
    private double _temperatureDelayTime;

    private string _inputInterfaceName = "ModbusTcp";
    private string _inputIp = "127.0.0.1";
    private int _inputPort = 502;
    private string _inputSerialPort = "COM1";
    private int _inputBaudRate = 115200;
    private int _inputDataBits = 8;
    private string _inputParityName = "None";
    private string _inputStopBitsName = "One";
    private string _inputEndianName = "CDAB(低字在前,默认)";
    private int _inputStation = 1;
    private int _inputRegisterAddress;
    private string _inputRegisterAddressText = "0";
    private bool _inputAddressHex;
    private string _inputRegisterAreaName = "保持寄存器(可读写)";
    private string _inputValueTypeName = "Float";
    private int _inputPeriodMs = 100;

    private string _outputInterfaceName = "ModbusTcp";
    private string _outputIp = "127.0.0.1";
    private int _outputPort = 502;
    private string _outputSerialPort = "COM1";
    private int _outputBaudRate = 115200;
    private int _outputDataBits = 8;
    private string _outputParityName = "None";
    private string _outputStopBitsName = "One";
    private string _outputEndianName = "CDAB(低字在前,默认)";
    private int _outputStation = 1;
    private int _outputRegisterAddress = 1;
    private string _outputRegisterAddressText = "1";
    private bool _outputAddressHex;
    private string _outputRegisterAreaName = "保持寄存器(可读写)";
    private string _outputValueTypeName = "Float";
    private int _outputPeriodMs = 100;
    private bool _shareModbusConnection;
    private bool _monitorOnlyInput;

    private string _status = "就绪";
    private double _processValue;
    private double _pidOutput;
    private double _error;
    private double _elapsedSeconds;
    private string _diagnosticText = "未启动";

    public MainViewModel()
    {
        ThemeService.Instance.Apply(SelectedTheme);

        StartCommand = new RelayCommand(_ => Start(), _ => !IsRunning);
        StopCommand = new RelayCommand(_ => Stop(), _ => IsRunning);
        StepOnceCommand = new RelayCommand(_ => StepOnce(), _ => !IsRunning);
        ResetSimulationCommand = new RelayCommand(_ => ResetSimulation());
        ClearChartCommand = new RelayCommand(_ => ClearChart());
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        ExportParametersCommand = new RelayCommand(_ => ExportParameters());
        ImportParametersCommand = new RelayCommand(_ => ImportParameters(), _ => !IsRunning);

        RefreshSerialPorts(true);
        RefreshSerialPorts(false);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SampleTime) };
        _timer.Tick += OnTimerTick;

        _plantRunner = new PlantRunner(() => _engine.Plant, () => _engine.PidOutput);

        ApplyPlantConfig();
        _engine.Setpoint = Setpoint;
        UpdateReadouts();
        ClearChartWithoutStatus();

        _isInitializing = false;
        RefreshCommandStates();
        Status = "就绪：可修改参数后点击“开始模拟”";
    }

    public IReadOnlyList<PidType> PidTypes { get; } = Enum.GetValues<PidType>();

    public IReadOnlyList<string> PidTypeNames { get; } = new[] { "位置式", "增量式" };

    public string SelectedPidTypeName
    {
        get => SelectedPidType == PidType.Positional ? "位置式" : "增量式";
        set
        {
            if (IsRunning)
            {
                OnPropertyChanged(nameof(SelectedPidTypeName));
                return;
            }

            PidType type = value == "增量式" ? PidType.Incremental : PidType.Positional;
            if (SelectedPidType != type)
                SelectedPidType = type;
            else
                OnPropertyChanged(nameof(SelectedPidTypeName));
        }
    }

    public IReadOnlyList<string> SimulationModelNames { get; } = new[] { "水泵流量计", "加热", "制冷", "Modbus通信" };

    public string SelectedSimulationModelName
    {
        get => SelectedSimulationModel switch
        {
            SimulationModel.Heating => "加热",
            SimulationModel.Cooling => "制冷",
            SimulationModel.Modbus => "Modbus通信",
            _ => "水泵流量计"
        };
        set
        {
            SelectedSimulationModel = value switch
            {
                "加热" => SimulationModel.Heating,
                "制冷" => SimulationModel.Cooling,
                "Modbus通信" => SimulationModel.Modbus,
                _ => SimulationModel.PumpFlow
            };
        }
    }

    public SimulationModel SelectedSimulationModel
    {
        get => _selectedSimulationModel;
        set
        {
            if (_selectedSimulationModel == value)
            {
                OnPropertyChanged(nameof(SelectedSimulationModelName));
                return;
            }

            if (IsRunning)
            {
                OnPropertyChanged(nameof(SelectedSimulationModelName));
                return;
            }

            _selectedSimulationModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSimulationModelName));
            OnPropertyChanged(nameof(IsFlowModel));
            OnPropertyChanged(nameof(IsTemperatureModel));
            OnPropertyChanged(nameof(IsModbusModel));
            OnPropertyChanged(nameof(PlantParameterHeader));
            OnPropertyChanged(nameof(SetpointMinimum));
            OnPropertyChanged(nameof(SetpointMaximum));

            SwitchPlant();
        }
    }

    public bool IsFlowModel => SelectedSimulationModel == SimulationModel.PumpFlow;

    public bool IsTemperatureModel => SelectedSimulationModel is SimulationModel.Heating or SimulationModel.Cooling;

    public bool IsModbusModel => SelectedSimulationModel == SimulationModel.Modbus;

    public string PlantParameterHeader => SelectedSimulationModel switch
    {
        SimulationModel.Cooling => "制冷模型参数",
        SimulationModel.Modbus => "Modbus 通信参数",
        _ => "加热模型参数"
    };

    public bool CanSwitchStructure => !IsRunning;

    public double SetpointMinimum => SelectedSimulationModel switch
    {
        SimulationModel.Heating => 0,
        SimulationModel.Cooling => AmbientTemperature - MaxTemperatureDelta,
        _ => -10
    };

    public double SetpointMaximum => SelectedSimulationModel switch
    {
        SimulationModel.Heating => AmbientTemperature + MaxTemperatureDelta,
        SimulationModel.Cooling => AmbientTemperature,
        _ => 10
    };

    public ObservableCollection<ChartSample> Samples => _samples;

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand StepOnceCommand { get; }
    public RelayCommand ResetSimulationCommand { get; }
    public RelayCommand ClearChartCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand ExportParametersCommand { get; }
    public RelayCommand ImportParametersCommand { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanSwitchStructure));
                RefreshCommandStates();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private void RefreshCommandStates()
    {
        StartCommand?.RaiseCanExecuteChanged();
        StopCommand?.RaiseCanExecuteChanged();
        StepOnceCommand?.RaiseCanExecuteChanged();
        ImportParametersCommand?.RaiseCanExecuteChanged();
    }

    private void ToggleTheme()
    {
        SelectedTheme = IsDarkTheme ? AppTheme.Light : AppTheme.Dark;
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string DiagnosticText
    {
        get => _diagnosticText;
        private set => SetProperty(ref _diagnosticText, value);
    }

    public double ProcessValue
    {
        get => _processValue;
        private set => SetProperty(ref _processValue, value);
    }

    public double PidOutput
    {
        get => _pidOutput;
        private set => SetProperty(ref _pidOutput, value);
    }

    public double Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public double ElapsedSeconds
    {
        get => _elapsedSeconds;
        private set => SetProperty(ref _elapsedSeconds, value);
    }

    public IReadOnlyList<string> ThemeNames { get; } = new[] { "暗色主题", "亮色主题" };

    public string SelectedThemeName
    {
        get => SelectedTheme == AppTheme.Dark ? "暗色主题" : "亮色主题";
        set
        {
            AppTheme theme = value == "亮色主题" ? AppTheme.Light : AppTheme.Dark;
            if (SelectedTheme != theme)
                SelectedTheme = theme;
            else
                OnPropertyChanged(nameof(SelectedThemeName));
        }
    }

    public AppTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                ThemeService.Instance.Apply(value);
                OnPropertyChanged(nameof(SelectedThemeName));
                OnPropertyChanged(nameof(IsDarkTheme));
                OnPropertyChanged(nameof(ThemeGlyph));
                OnPropertyChanged(nameof(ThemeToggleToolTip));
            }
        }
    }

    public bool IsDarkTheme => SelectedTheme == AppTheme.Dark;

    /// <summary>太阳表示当前亮色，月亮表示当前暗色。</summary>
    public string ThemeGlyph => IsDarkTheme ? "☾" : "☀";

    public string ThemeToggleToolTip => IsDarkTheme ? "切换到亮色主题" : "切换到暗色主题";

    public bool IsPositional => SelectedPidType == PidType.Positional;

    public bool IsIncremental => SelectedPidType == PidType.Incremental;

    public string PidParameterHeader =>
        $"PID 参数 - {(IsPositional ? "位置式" : "增量式")}";

    public PidType SelectedPidType
    {
        get => _selectedPidType;
        set
        {
            if (SetProperty(ref _selectedPidType, value))
            {
                _needsControllerRecreate = true;
                OnPropertyChanged(nameof(IsPositional));
                OnPropertyChanged(nameof(IsIncremental));
                OnPropertyChanged(nameof(PidParameterHeader));
                OnPropertyChanged(nameof(SelectedPidTypeName));
                OnParameterChanged();
            }
        }
    }

    public double Kp
    {
        get => _kp;
        set { if (SetProperty(ref _kp, value)) OnParameterChanged(); }
    }

    public double Ki
    {
        get => _ki;
        set { if (SetProperty(ref _ki, value)) OnParameterChanged(); }
    }

    public double Kd
    {
        get => _kd;
        set { if (SetProperty(ref _kd, value)) OnParameterChanged(); }
    }

    public double OutputMin
    {
        get => _outputMin;
        set { if (SetProperty(ref _outputMin, value)) OnParameterChanged(); }
    }

    public double OutputMax
    {
        get => _outputMax;
        set { if (SetProperty(ref _outputMax, value)) OnParameterChanged(); }
    }

    public double DeltaMax
    {
        get => _deltaMax;
        set { if (SetProperty(ref _deltaMax, value)) OnParameterChanged(); }
    }

    public double IntegralMax
    {
        get => _integralMax;
        set { if (SetProperty(ref _integralMax, value)) OnParameterChanged(); }
    }

    public double SampleTime
    {
        get => _sampleTime;
        set { if (SetProperty(ref _sampleTime, value)) OnParameterChanged(); }
    }

    public double Setpoint
    {
        get => _setpoint;
        set
        {
            if (SetProperty(ref _setpoint, value))
            {
                _engine.Setpoint = value;
                OnParameterChanged();
            }
        }
    }

    public double InitialValue
    {
        get => _initialValue;
        set { if (SetProperty(ref _initialValue, value)) OnParameterChanged(); }
    }

    public double PumpMinCommand
    {
        get => _pumpMinCommand;
        set { if (SetProperty(ref _pumpMinCommand, value)) OnParameterChanged(); }
    }

    public double PumpMaxCommand
    {
        get => _pumpMaxCommand;
        set { if (SetProperty(ref _pumpMaxCommand, value)) OnParameterChanged(); }
    }

    public double PumpMinOutput
    {
        get => _pumpMinOutput;
        set { if (SetProperty(ref _pumpMinOutput, value)) OnParameterChanged(); }
    }

    public double PumpMaxOutput
    {
        get => _pumpMaxOutput;
        set { if (SetProperty(ref _pumpMaxOutput, value)) OnParameterChanged(); }
    }

    public double PumpTimeConstant
    {
        get => _pumpTimeConstant;
        set { if (SetProperty(ref _pumpTimeConstant, value)) OnParameterChanged(); }
    }

    /// <summary>水泵纯滞后时间（秒），0 表示不启用。</summary>
    public double PumpDelayTime
    {
        get => _pumpDelayTime;
        set { if (SetProperty(ref _pumpDelayTime, value)) OnParameterChanged(); }
    }

    /// <summary>环境温度（°C），指令为 0 时的稳态温度。</summary>
    public double AmbientTemperature
    {
        get => _ambientTemperature;
        set
        {
            if (SetProperty(ref _ambientTemperature, value))
            {
                OnPropertyChanged(nameof(SetpointMinimum));
                OnPropertyChanged(nameof(SetpointMaximum));
                OnParameterChanged();
            }
        }
    }

    /// <summary>满功率时相对环境温度的最大温差（°C）。</summary>
    public double MaxTemperatureDelta
    {
        get => _maxTemperatureDelta;
        set
        {
            if (SetProperty(ref _maxTemperatureDelta, value))
            {
                OnPropertyChanged(nameof(SetpointMinimum));
                OnPropertyChanged(nameof(SetpointMaximum));
                OnParameterChanged();
            }
        }
    }

    /// <summary>满功率对应的指令值。</summary>
    public double MaxTemperatureCommand
    {
        get => _maxTemperatureCommand;
        set { if (SetProperty(ref _maxTemperatureCommand, value)) OnParameterChanged(); }
    }

    /// <summary>热惯性时间常数（秒）。</summary>
    public double TemperatureTimeConstant
    {
        get => _temperatureTimeConstant;
        set { if (SetProperty(ref _temperatureTimeConstant, value)) OnParameterChanged(); }
    }

    /// <summary>温控模型纯滞后时间（秒），0 表示不启用。</summary>
    public double TemperatureDelayTime
    {
        get => _temperatureDelayTime;
        set { if (SetProperty(ref _temperatureDelayTime, value)) OnParameterChanged(); }
    }

    public IReadOnlyList<string> ModbusInterfaceNames { get; } =
        new[] { "ModbusRtu", "ModbusTcp", "ModbusRtuOverTcp" };

    public IReadOnlyList<string> AddressModeNames { get; } =
        new[] { "十进制", "十六进制" };

    public ObservableCollection<string> InputSerialPortNames { get; } = new();

    public ObservableCollection<string> OutputSerialPortNames { get; } = new();

    public IReadOnlyList<int> BaudRates { get; } =
        new[] { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };

    public IReadOnlyList<int> DataBitsOptions { get; } = new[] { 5, 6, 7, 8 };

    public IReadOnlyList<string> ParityNames { get; } =
        new[] { "None", "Odd", "Even", "Mark", "Space" };

    public IReadOnlyList<string> StopBitsNames { get; } =
        new[] { "One", "OnePointFive", "Two" };

    public IReadOnlyList<string> EndianNames { get; } =
        new[] { "ABCD(大端)", "BADC(字内交换)", "CDAB(低字在前,默认)", "DCBA(小端)" };

    public IReadOnlyList<string> ModbusValueTypeNames { get; } =
        new[] { "Int16", "UInt16", "Int32", "UInt32", "Float" };

    public IReadOnlyList<string> InputRegisterAreaNames { get; } =
        new[] { "输入寄存器(只读)", "保持寄存器(可读写)" };

    public IReadOnlyList<string> OutputRegisterAreaNames { get; } =
        new[] { "保持寄存器(可读写)" };

    public bool MonitorOnlyInput
    {
        get => _monitorOnlyInput;
        set
        {
            if (SetProperty(ref _monitorOnlyInput, value))
            {
                OnPropertyChanged(nameof(IsOutputConfigEnabled));
                OnParameterChanged();
            }
        }
    }

    public bool IsOutputConfigEnabled => !MonitorOnlyInput;

    public bool ShareModbusConnection
    {
        get => _shareModbusConnection;
        set
        {
            if (SetProperty(ref _shareModbusConnection, value))
            {
                OnPropertyChanged(nameof(IsOutputLinkSeparate));
                OnParameterChanged();
            }
        }
    }

    public bool IsOutputLinkSeparate => !ShareModbusConnection;

    public string InputInterfaceName
    {
        get => _inputInterfaceName;
        set
        {
            if (SetProperty(ref _inputInterfaceName, value))
                NotifyModbusInterfaceChanged(true);
        }
    }

    public bool IsInputRtu => InputInterfaceName == "ModbusRtu";

    public bool IsInputNetwork => !IsInputRtu;

    public string InputIp
    {
        get => _inputIp;
        set { if (SetProperty(ref _inputIp, value)) OnParameterChanged(); }
    }

    public int InputPort
    {
        get => _inputPort;
        set { if (SetProperty(ref _inputPort, value)) OnParameterChanged(); }
    }

    public string InputSerialPort
    {
        get => _inputSerialPort;
        set { if (SetProperty(ref _inputSerialPort, value)) OnParameterChanged(); }
    }

    public int InputBaudRate
    {
        get => _inputBaudRate;
        set { if (SetProperty(ref _inputBaudRate, value)) OnParameterChanged(); }
    }

    public int InputDataBits
    {
        get => _inputDataBits;
        set { if (SetProperty(ref _inputDataBits, value)) OnParameterChanged(); }
    }

    public string InputParityName
    {
        get => _inputParityName;
        set { if (SetProperty(ref _inputParityName, value)) OnParameterChanged(); }
    }

    public string InputStopBitsName
    {
        get => _inputStopBitsName;
        set { if (SetProperty(ref _inputStopBitsName, value)) OnParameterChanged(); }
    }

    public string InputEndianName
    {
        get => _inputEndianName;
        set { if (SetProperty(ref _inputEndianName, value)) OnParameterChanged(); }
    }

    public int InputStation
    {
        get => _inputStation;
        set { if (SetProperty(ref _inputStation, value)) OnParameterChanged(); }
    }

    public int InputRegisterAddress
    {
        get => _inputRegisterAddress;
        set => ApplyInputRegisterAddress(value);
    }

    public string InputRegisterAddressText
    {
        get => _inputRegisterAddressText;
        set
        {
            if (!SetProperty(ref _inputRegisterAddressText, value))
                return;

            if (TryParseAddress(value, _inputAddressHex, out int address))
            {
                if (_inputRegisterAddress != address)
                {
                    _inputRegisterAddress = address;
                    OnPropertyChanged(nameof(InputRegisterAddress));
                }
                OnParameterChanged();
            }
            else
            {
                Status = "输入寄存器地址格式无效";
            }
        }
    }

    public string InputAddressModeName
    {
        get => _inputAddressHex ? AddressModeNames[1] : AddressModeNames[0];
        set
        {
            bool hex = value == AddressModeNames[1];
            if (_inputAddressHex == hex)
                return;

            _inputAddressHex = hex;
            OnPropertyChanged(nameof(InputAddressModeName));
            OnPropertyChanged(nameof(InputAddressHex));
            SetProperty(ref _inputRegisterAddressText, FormatAddress(_inputRegisterAddress, hex), nameof(InputRegisterAddressText));
            OnParameterChanged();
        }
    }

    public bool InputAddressHex => _inputAddressHex;

    public string InputRegisterAreaName
    {
        get => _inputRegisterAreaName;
        set { if (SetProperty(ref _inputRegisterAreaName, value)) OnParameterChanged(); }
    }

    public string InputValueTypeName
    {
        get => _inputValueTypeName;
        set { if (SetProperty(ref _inputValueTypeName, value)) OnParameterChanged(); }
    }

    public int InputPeriodMs
    {
        get => _inputPeriodMs;
        set { if (SetProperty(ref _inputPeriodMs, value)) OnParameterChanged(); }
    }

    public string OutputInterfaceName
    {
        get => _outputInterfaceName;
        set
        {
            if (SetProperty(ref _outputInterfaceName, value))
                NotifyModbusInterfaceChanged(false);
        }
    }

    public bool IsOutputRtu => OutputInterfaceName == "ModbusRtu";

    public bool IsOutputNetwork => !IsOutputRtu;

    public string OutputIp
    {
        get => _outputIp;
        set { if (SetProperty(ref _outputIp, value)) OnParameterChanged(); }
    }

    public int OutputPort
    {
        get => _outputPort;
        set { if (SetProperty(ref _outputPort, value)) OnParameterChanged(); }
    }

    public string OutputSerialPort
    {
        get => _outputSerialPort;
        set { if (SetProperty(ref _outputSerialPort, value)) OnParameterChanged(); }
    }

    public int OutputBaudRate
    {
        get => _outputBaudRate;
        set { if (SetProperty(ref _outputBaudRate, value)) OnParameterChanged(); }
    }

    public int OutputDataBits
    {
        get => _outputDataBits;
        set { if (SetProperty(ref _outputDataBits, value)) OnParameterChanged(); }
    }

    public string OutputParityName
    {
        get => _outputParityName;
        set { if (SetProperty(ref _outputParityName, value)) OnParameterChanged(); }
    }

    public string OutputStopBitsName
    {
        get => _outputStopBitsName;
        set { if (SetProperty(ref _outputStopBitsName, value)) OnParameterChanged(); }
    }

    public string OutputEndianName
    {
        get => _outputEndianName;
        set { if (SetProperty(ref _outputEndianName, value)) OnParameterChanged(); }
    }

    public int OutputStation
    {
        get => _outputStation;
        set { if (SetProperty(ref _outputStation, value)) OnParameterChanged(); }
    }

    public int OutputRegisterAddress
    {
        get => _outputRegisterAddress;
        set => ApplyOutputRegisterAddress(value);
    }

    public string OutputRegisterAddressText
    {
        get => _outputRegisterAddressText;
        set
        {
            if (!SetProperty(ref _outputRegisterAddressText, value))
                return;

            if (TryParseAddress(value, _outputAddressHex, out int address))
            {
                if (_outputRegisterAddress != address)
                {
                    _outputRegisterAddress = address;
                    OnPropertyChanged(nameof(OutputRegisterAddress));
                }
                OnParameterChanged();
            }
            else
            {
                Status = "输出寄存器地址格式无效";
            }
        }
    }

    public string OutputAddressModeName
    {
        get => _outputAddressHex ? AddressModeNames[1] : AddressModeNames[0];
        set
        {
            bool hex = value == AddressModeNames[1];
            if (_outputAddressHex == hex)
                return;

            _outputAddressHex = hex;
            OnPropertyChanged(nameof(OutputAddressModeName));
            OnPropertyChanged(nameof(OutputAddressHex));
            SetProperty(ref _outputRegisterAddressText, FormatAddress(_outputRegisterAddress, hex), nameof(OutputRegisterAddressText));
            OnParameterChanged();
        }
    }

    public bool OutputAddressHex => _outputAddressHex;

    public string OutputRegisterAreaName
    {
        get => _outputRegisterAreaName;
        set { if (SetProperty(ref _outputRegisterAreaName, value)) OnParameterChanged(); }
    }

    public string OutputValueTypeName
    {
        get => _outputValueTypeName;
        set { if (SetProperty(ref _outputValueTypeName, value)) OnParameterChanged(); }
    }

    public int OutputPeriodMs
    {
        get => _outputPeriodMs;
        set { if (SetProperty(ref _outputPeriodMs, value)) OnParameterChanged(); }
    }

    private void NotifyModbusInterfaceChanged(bool input)
    {
        if (input)
        {
            OnPropertyChanged(nameof(IsInputRtu));
            OnPropertyChanged(nameof(IsInputNetwork));
        }
        else
        {
            OnPropertyChanged(nameof(IsOutputRtu));
            OnPropertyChanged(nameof(IsOutputNetwork));
        }

        OnParameterChanged();
    }

    private void OnParameterChanged()
    {
        if (_isInitializing)
            return;

        try
        {
            ApplyCurrentParameters();
        }
        catch (Exception ex)
        {
            Status = $"参数错误：{ex.Message}";
        }
    }

    private void ApplyCurrentParameters()
    {
        PidParameters p = BuildParameters();
        Validate(p);

        _engine.MonitorOnly = IsModbusModel && MonitorOnlyInput;
        ApplyPlantConfig();

        _engine.Setpoint = Setpoint;
        _timer.Interval = TimeSpan.FromSeconds(SampleTime);

        if (_engine.Controller is null)
            return;

        string note = "参数已更新";

        if (_needsControllerRecreate || _engine.PidType != SelectedPidType)
        {
            // 切换算法时保留对象当前值，避免过程量跳变。
            _engine.Configure(SelectedPidType, p, _engine.ProcessValue, _engine.Time);
            _needsControllerRecreate = false;
            note = $"已切换到 {SelectedPidType}";
        }
        else
        {
            _engine.ApplyParameters(p);
        }

        Status = IsRunning ? "运行中" : note;
    }

    private PidParameters BuildParameters()
    {
        return new PidParameters
        {
            Kp = Kp,
            Ki = Ki,
            Kd = Kd,
            OutputMin = OutputMin,
            OutputMax = OutputMax,
            DeltaMax = DeltaMax,
            IntegralMax = IntegralMax,
            SampleTime = SampleTime
        };
    }

    private void Validate(PidParameters p)
    {
        if (p.OutputMin > p.OutputMax)
            throw new ArgumentException("PID 输出下限不能大于上限");
        if (p.SampleTime <= 0 || double.IsNaN(p.SampleTime))
            throw new ArgumentException("采样周期必须大于 0");
        if (p.DeltaMax < 0 || double.IsNaN(p.DeltaMax))
            throw new ArgumentException("增量限幅必须为非负数");
        if (p.IntegralMax < 0 || double.IsNaN(p.IntegralMax))
            throw new ArgumentException("积分限幅必须为非负数");
        if (IsFlowModel)
        {
            if (PumpMaxCommand <= PumpMinCommand)
                throw new ArgumentException("水泵指令上限必须大于下限");
            if (PumpMaxOutput < PumpMinOutput)
                throw new ArgumentException("水泵输出上限不能小于下限");
            if (PumpTimeConstant <= 0 || double.IsNaN(PumpTimeConstant))
                throw new ArgumentException("水泵时间常数必须大于 0");
            if (PumpDelayTime < 0 || double.IsNaN(PumpDelayTime))
                throw new ArgumentException("水泵滞后时间必须为非负数");
        }
        else if (IsTemperatureModel)
        {
            if (MaxTemperatureCommand <= 0 || double.IsNaN(MaxTemperatureCommand))
                throw new ArgumentException("满功率指令必须大于 0");
            if (MaxTemperatureDelta <= 0 || double.IsNaN(MaxTemperatureDelta))
                throw new ArgumentException("满功率温差必须大于 0");
            if (TemperatureTimeConstant <= 0 || double.IsNaN(TemperatureTimeConstant))
                throw new ArgumentException("温控时间常数必须大于 0");
            if (TemperatureDelayTime < 0 || double.IsNaN(TemperatureDelayTime))
                throw new ArgumentException("温控滞后时间必须为非负数");
        }
        else if (IsModbusModel)
        {
            ValidateModbusChannel("输入", InputInterfaceName, InputIp, InputPort,
                InputSerialPort, InputBaudRate, InputDataBits, InputEndianName, InputStation, InputRegisterAddress,
                InputRegisterAreaName, InputValueTypeName, InputPeriodMs, true, false);

            if (!MonitorOnlyInput)
            {
                string outEndian = ShareModbusConnection ? InputEndianName : OutputEndianName;
                ValidateModbusChannel("输出", OutputInterfaceName, OutputIp, OutputPort,
                    OutputSerialPort, OutputBaudRate, OutputDataBits, outEndian, OutputStation, OutputRegisterAddress,
                    OutputRegisterAreaName, OutputValueTypeName, OutputPeriodMs, !ShareModbusConnection, true);
            }
        }
    }

    private void ApplyPlantConfig()
    {
        ApplyPlantConfig(_engine.Plant);
    }

    private void ApplyPlantConfig(SimulationPlant plant)
    {
        switch (plant)
        {
            case PumpSimulator pump:
                pump.MinCommand = PumpMinCommand;
                pump.MaxCommand = PumpMaxCommand;
                pump.MinOutput = PumpMinOutput;
                pump.MaxOutput = PumpMaxOutput;
                pump.TimeConstant = PumpTimeConstant;
                pump.DelayTime = PumpDelayTime;
                break;

            case TemperaturePlant temperature:
                temperature.AmbientTemperature = AmbientTemperature;
                temperature.MaxDeltaTemperature = MaxTemperatureDelta;
                temperature.MaxCommand = MaxTemperatureCommand;
                temperature.TimeConstant = TemperatureTimeConstant;
                temperature.DelayTime = TemperatureDelayTime;
                break;

            case ModbusPlant modbus:
                string outInterface = ShareModbusConnection ? InputInterfaceName : OutputInterfaceName;
                string outIp = ShareModbusConnection ? InputIp : OutputIp;
                int outPort = ShareModbusConnection ? InputPort : OutputPort;
                string outSerialPort = ShareModbusConnection ? InputSerialPort : OutputSerialPort;
                int outBaudRate = ShareModbusConnection ? InputBaudRate : OutputBaudRate;
                int outDataBits = ShareModbusConnection ? InputDataBits : OutputDataBits;
                string outParity = ShareModbusConnection ? InputParityName : OutputParityName;
                string outStopBits = ShareModbusConnection ? InputStopBitsName : OutputStopBitsName;
                string outEndian = ShareModbusConnection ? InputEndianName : OutputEndianName;

                modbus.Configure(new ModbusPlantConfig
                {
                    Input = BuildChannelConfig(InputInterfaceName, InputIp, InputPort,
                        InputSerialPort, InputBaudRate, InputDataBits, InputParityName, InputStopBitsName, InputEndianName,
                        InputStation, InputRegisterAddress, InputRegisterAreaName, InputValueTypeName, InputPeriodMs),
                    Output = BuildChannelConfig(outInterface, outIp, outPort,
                        outSerialPort, outBaudRate, outDataBits, outParity, outStopBits, outEndian,
                        OutputStation, OutputRegisterAddress, OutputRegisterAreaName, OutputValueTypeName, OutputPeriodMs),
                    WriteOutput = !MonitorOnlyInput,
                    ShareConnection = ShareModbusConnection
                });
                break;
        }

        plant.ClampCurrentValue();
    }

    private static ModbusChannelConfig BuildChannelConfig(
        string interfaceName, string ip, int port, string serialPort, int baudRate, int dataBits,
        string parityName, string stopBitsName, string endianName, int station, int registerAddress,
        string registerAreaName, string valueTypeName, int periodMs)
    {
        return new ModbusChannelConfig
        {
            Interface = ParseModbusInterface(interfaceName),
            Ip = ip,
            Port = port,
            SerialPort = serialPort,
            BaudRate = baudRate,
            DataBits = dataBits,
            Parity = ParseEnum<Parity>(parityName, "校验位"),
            StopBits = ParseEnum<StopBits>(stopBitsName, "停止位"),
            Endian = ParseEndian(endianName),
            Station = (byte)station,
            RegisterAddress = (ushort)registerAddress,
            RegisterArea = ParseRegisterArea(registerAreaName),
            ValueType = ParseEnum<ModbusValueType>(valueTypeName, "数据类型"),
            PeriodMs = periodMs
        };
    }

    private static void ValidateModbusChannel(
        string label, string interfaceName, string ip, int port, string serialPort,
        int baudRate, int dataBits, string endianName, int station, int registerAddress,
        string registerAreaName, string valueTypeName, int periodMs,
        bool validateLink, bool requireHolding)
    {
        ParseEndian(endianName);
        ModbusRegisterArea area = ParseRegisterArea(registerAreaName);
        ModbusValueType valueType = ParseEnum<ModbusValueType>(valueTypeName, "数据类型");
        int registerCount = valueType is ModbusValueType.Int32 or ModbusValueType.UInt32 or ModbusValueType.Float ? 2 : 1;

        if (station is < 1 or > 247)
            throw new ArgumentException($"{label}站号必须在 1~247 之间");
        if (registerAddress < 0 || registerAddress + registerCount > 65536)
            throw new ArgumentException($"{label}寄存器地址超出有效范围");
        if (periodMs <= 0)
            throw new ArgumentException($"{label}读写周期必须大于 0");
        if (requireHolding && area != ModbusRegisterArea.HoldingRegisters)
            throw new ArgumentException("输出通道只能使用保持寄存器");

        if (!validateLink)
            return;

        ModbusInterface modbusInterface = ParseModbusInterface(interfaceName);
        if (modbusInterface == ModbusInterface.ModbusRtu)
        {
            if (string.IsNullOrWhiteSpace(serialPort))
                throw new ArgumentException($"{label}串口名不能为空");
            if (baudRate <= 0)
                throw new ArgumentException($"{label}波特率必须大于 0");
            if (dataBits is < 5 or > 8)
                throw new ArgumentException($"{label}数据位必须在 5~8 之间");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(ip))
                throw new ArgumentException($"{label}IP 地址不能为空");
            if (port is < 1 or > 65535)
                throw new ArgumentException($"{label}端口必须在 1~65535 之间");
        }
    }

    private void ApplyInputRegisterAddress(int value)
    {
        if (SetProperty(ref _inputRegisterAddress, value))
        {
            SetProperty(ref _inputRegisterAddressText, FormatAddress(value, _inputAddressHex), nameof(InputRegisterAddressText));
            OnParameterChanged();
        }
    }

    private void ApplyOutputRegisterAddress(int value)
    {
        if (SetProperty(ref _outputRegisterAddress, value))
        {
            SetProperty(ref _outputRegisterAddressText, FormatAddress(value, _outputAddressHex), nameof(OutputRegisterAddressText));
            OnParameterChanged();
        }
    }

    private static string FormatAddress(int address, bool hex)
    {
        return hex ? $"0x{address:X4}" : address.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryParseAddress(string text, bool hex, out int address)
    {
        string value = (text ?? string.Empty).Trim();
        if (hex)
        {
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = value[2..];
            return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out address);
    }

    /// <summary>刷新指定通道的串口列表；只更新该通道，避免另一通道的选中项被集合重置清掉。</summary>
    public void RefreshSerialPorts(bool input)
    {
        ObservableCollection<string> target = input ? InputSerialPortNames : OutputSerialPortNames;
        string? selected = input ? InputSerialPort : OutputSerialPort;

        var ports = SerialPort.GetPortNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        if (!string.IsNullOrWhiteSpace(selected) && !ports.Contains(selected, StringComparer.OrdinalIgnoreCase))
            ports.Add(selected);

        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!ports.Contains(target[i], StringComparer.OrdinalIgnoreCase))
                target.RemoveAt(i);
        }

        foreach (string port in ports)
        {
            if (!target.Contains(port, StringComparer.OrdinalIgnoreCase))
                target.Add(port);
        }
    }

    private static ModbusInterface ParseModbusInterface(string name)
    {
        return name switch
        {
            "ModbusRtu" => ModbusInterface.ModbusRtu,
            "ModbusTcp" => ModbusInterface.ModbusTcp,
            "ModbusRtuOverTcp" => ModbusInterface.ModbusRtuOverTcp,
            _ => throw new ArgumentException($"不支持的 Modbus 接口类型：{name}")
        };
    }

    private static ModbusRegisterArea ParseRegisterArea(string name)
    {
        return name.StartsWith("输入", StringComparison.Ordinal)
            ? ModbusRegisterArea.InputRegisters
            : ModbusRegisterArea.HoldingRegisters;
    }

    private static EnumEndian ParseEndian(string name)
    {
        string key = name.Split('(')[0];
        return ParseEnum<EnumEndian>(key, "字节序");
    }

    private static T ParseEnum<T>(string name, string label) where T : struct, Enum
    {
        if (Enum.TryParse<T>(name, true, out T value))
            return value;
        throw new ArgumentException($"{label}取值无效：{name}");
    }

    private void CloseModbusConnection()
    {
        if (_engine.Plant is ModbusPlant modbus)
            modbus.Close();
    }

    /// <summary>停止状态下切换仿真对象：新建对象、复位状态并调整设定值到有效范围。</summary>
    private void SwitchPlant()
    {
        CloseModbusConnection();

        double initial = SelectedSimulationModel switch
        {
            SimulationModel.Heating or SimulationModel.Cooling => AmbientTemperature,
            _ => 0
        };

        SimulationPlant plant = SelectedSimulationModel switch
        {
            SimulationModel.Heating => new HeatingPlant(initial),
            SimulationModel.Cooling => new CoolingPlant(initial),
            SimulationModel.Modbus => new ModbusPlant(),
            _ => new PumpSimulator(initial)
        };

        ApplyPlantConfig(plant);

        _initialValue = initial;
        OnPropertyChanged(nameof(InitialValue));

        double setpoint = Math.Clamp(Setpoint, SetpointMinimum, SetpointMaximum);
        if (SelectedSimulationModel == SimulationModel.Heating && setpoint <= AmbientTemperature)
            setpoint = AmbientTemperature + MaxTemperatureDelta * 0.5;
        else if (SelectedSimulationModel == SimulationModel.Cooling && setpoint >= AmbientTemperature)
            setpoint = AmbientTemperature - MaxTemperatureDelta * 0.5;

        _setpoint = setpoint;
        OnPropertyChanged(nameof(Setpoint));

        _engine.Setpoint = setpoint;
        _engine.SwitchPlant(plant, initial);

        _samples.Clear();
        _chartOrigin = _engine.Time;
        AppendLocalSample();
        UpdateReadouts();

        Status = IsRunning ? "运行中" : $"已切换到{SelectedSimulationModelName}模型";
    }

    private void EnsureReady()
    {
        ApplyCurrentParameters();

        if (_engine.Controller is null)
        {
            _engine.Configure(SelectedPidType, BuildParameters(), InitialValue, 0);
            _needsControllerRecreate = false;
        }

        _engine.Setpoint = Setpoint;
        UpdateReadouts();
    }

    private void Start()
    {
        if (IsRunning)
            return;

        try
        {
            EnsureReady();

            _timer.Interval = TimeSpan.FromSeconds(SampleTime);
            _plantRunner.Start();
            _timer.Start();
            IsRunning = true;
            Status = "运行中";
        }
        catch (Exception ex)
        {
            Status = $"启动失败：{ex.Message}";
        }
    }

    private void Stop()
    {
        _timer.Stop();
        _plantRunner.Stop();
        CloseModbusConnection();
        IsRunning = false;
        Status = "已停止";
    }

    private void StepOnce()
    {
        if (IsRunning)
            return;

        try
        {
            EnsureReady();

            // 未运行时水泵线程不工作，单步时手动推进一个采样周期（按 10ms 一次运算）。
            int ticks = (int)Math.Round(SampleTime / SimulationPlant.TickSeconds, MidpointRounding.AwayFromZero);
            for (int i = 0; i < ticks; i++)
                _engine.Plant.Step(_engine.PidOutput, SimulationPlant.TickSeconds);

            ChartSample sample = _engine.Step();
            AppendSample(sample);
            UpdateReadouts();
            Status = "已执行单步";
        }
        catch (Exception ex)
        {
            Status = $"单步失败：{ex.Message}";
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_plantRunner.LastError is { } commError)
        {
            _timer.Stop();
            IsRunning = false;
            CloseModbusConnection();
            Status = $"通信异常：{commError.Message}";
            return;
        }

        try
        {
            ChartSample sample = _engine.Step();
            AppendSample(sample);
            UpdateReadouts();
        }
        catch (Exception ex)
        {
            _timer.Stop();
            IsRunning = false;
            CloseModbusConnection();
            Status = $"仿真异常：{ex.Message}";
        }
    }

    private void ResetSimulation()
    {
        try
        {
            EnsureReady();
            _engine.Reset(InitialValue, 0);
            _chartOrigin = 0;
            _samples.Clear();
            UpdateReadouts();
            AppendLocalSample();

            Status = IsRunning ? "运行中：状态已复位" : "仿真状态已复位";
        }
        catch (Exception ex)
        {
            Status = $"复位失败：{ex.Message}";
        }
    }

    private void ClearChart()
    {
        ClearChartWithoutStatus();
        Status = "曲线记录已清空";
    }

    private void ClearChartWithoutStatus()
    {
        _chartOrigin = _engine.Time;
        _samples.Clear();
        _engine.Plant.ClearHistory();
        AppendLocalSample();
    }

    private void AppendSample(ChartSample sample)
    {
        _samples.Add(new ChartSample(
            sample.Time - _chartOrigin,
            sample.Setpoint,
            sample.ProcessValue,
            sample.Output));

        while (_samples.Count > MaxSamples)
            _samples.RemoveAt(0);
    }

    private void AppendLocalSample()
    {
        _samples.Add(new ChartSample(
            0,
            Setpoint,
            _engine.ProcessValue,
            _engine.PidOutput));
    }

    private void ExportParameters()
    {
        const string filter = "PID 仿真参数 (*.pidsim)|*.pidsim";

        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = ".pidsim",
                AddExtension = true,
                FileName = $"PidSim-{DateTime.Now:yyyyMMdd-HHmmss}.pidsim"
            };

            if (dialog.ShowDialog() != true)
                return;

            string path = dialog.FileName;
            if (!string.Equals(Path.GetExtension(path), ".pidsim", StringComparison.OrdinalIgnoreCase))
                path += ".pidsim";

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(BuildParameterFile(), options), Encoding.UTF8);
            Status = $"参数已导出：{Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            Status = $"导出失败：{ex.Message}";
        }
    }

    private void ImportParameters()
    {
        const string filter = "PID 仿真参数 (*.pidsim)|*.pidsim";

        if (IsRunning)
            return;

        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter,
                DefaultExt = ".pidsim",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
                return;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            PidSimParameterFile? file = JsonSerializer.Deserialize<PidSimParameterFile>(
                File.ReadAllText(dialog.FileName, Encoding.UTF8), options);

            if (file is null)
                throw new InvalidDataException("参数文件内容为空");
            if (file.FormatVersion != 1)
                throw new InvalidDataException($"不支持的参数文件格式版本：{file.FormatVersion}");

            ApplyParameterFile(file);
            Status = $"参数已导入：{Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            Status = $"导入失败：{ex.Message}";
        }
    }

    private PidSimParameterFile BuildParameterFile()
    {
        return new PidSimParameterFile
        {
            SavedAt = DateTime.Now.ToString("O"),
            SimulationModel = SelectedSimulationModelName,
            PidType = SelectedPidTypeName,
            Setpoint = Setpoint,
            InitialValue = InitialValue,
            Kp = Kp,
            Ki = Ki,
            Kd = Kd,
            OutputMin = OutputMin,
            OutputMax = OutputMax,
            DeltaMax = DeltaMax,
            IntegralMax = IntegralMax,
            SampleTime = SampleTime,
            PumpMinCommand = PumpMinCommand,
            PumpMaxCommand = PumpMaxCommand,
            PumpMinOutput = PumpMinOutput,
            PumpMaxOutput = PumpMaxOutput,
            PumpTimeConstant = PumpTimeConstant,
            PumpDelayTime = PumpDelayTime,
            AmbientTemperature = AmbientTemperature,
            MaxTemperatureDelta = MaxTemperatureDelta,
            MaxTemperatureCommand = MaxTemperatureCommand,
            TemperatureTimeConstant = TemperatureTimeConstant,
            TemperatureDelayTime = TemperatureDelayTime,
            MonitorOnlyInput = MonitorOnlyInput,
            ShareModbusConnection = ShareModbusConnection,
            InputInterfaceName = InputInterfaceName,
            InputIp = InputIp,
            InputPort = InputPort,
            InputSerialPort = InputSerialPort,
            InputBaudRate = InputBaudRate,
            InputDataBits = InputDataBits,
            InputParityName = InputParityName,
            InputStopBitsName = InputStopBitsName,
            InputEndianName = InputEndianName,
            InputStation = InputStation,
            InputRegisterAddress = InputRegisterAddress,
            InputAddressHex = InputAddressHex,
            InputRegisterAreaName = InputRegisterAreaName,
            InputValueTypeName = InputValueTypeName,
            InputPeriodMs = InputPeriodMs,
            OutputInterfaceName = OutputInterfaceName,
            OutputIp = OutputIp,
            OutputPort = OutputPort,
            OutputSerialPort = OutputSerialPort,
            OutputBaudRate = OutputBaudRate,
            OutputDataBits = OutputDataBits,
            OutputParityName = OutputParityName,
            OutputStopBitsName = OutputStopBitsName,
            OutputEndianName = OutputEndianName,
            OutputStation = OutputStation,
            OutputRegisterAddress = OutputRegisterAddress,
            OutputAddressHex = OutputAddressHex,
            OutputRegisterAreaName = OutputRegisterAreaName,
            OutputValueTypeName = OutputValueTypeName,
            OutputPeriodMs = OutputPeriodMs
        };
    }

    private void ApplyParameterFile(PidSimParameterFile file)
    {
        _isInitializing = true;
        try
        {
            _needsControllerRecreate = true;

            SelectedPidTypeName = file.PidType;
            Setpoint = file.Setpoint;
            InitialValue = file.InitialValue;
            Kp = file.Kp;
            Ki = file.Ki;
            Kd = file.Kd;
            OutputMin = file.OutputMin;
            OutputMax = file.OutputMax;
            DeltaMax = file.DeltaMax;
            IntegralMax = file.IntegralMax;
            SampleTime = file.SampleTime;

            PumpMinCommand = file.PumpMinCommand;
            PumpMaxCommand = file.PumpMaxCommand;
            PumpMinOutput = file.PumpMinOutput;
            PumpMaxOutput = file.PumpMaxOutput;
            PumpTimeConstant = file.PumpTimeConstant;
            PumpDelayTime = file.PumpDelayTime;

            AmbientTemperature = file.AmbientTemperature;
            MaxTemperatureDelta = file.MaxTemperatureDelta;
            MaxTemperatureCommand = file.MaxTemperatureCommand;
            TemperatureTimeConstant = file.TemperatureTimeConstant;
            TemperatureDelayTime = file.TemperatureDelayTime;

            MonitorOnlyInput = file.MonitorOnlyInput;
            ShareModbusConnection = file.ShareModbusConnection;

            InputInterfaceName = file.InputInterfaceName;
            InputIp = file.InputIp;
            InputPort = file.InputPort;
            InputSerialPort = file.InputSerialPort;
            InputBaudRate = file.InputBaudRate;
            InputDataBits = file.InputDataBits;
            InputParityName = file.InputParityName;
            InputStopBitsName = file.InputStopBitsName;
            InputEndianName = file.InputEndianName;
            InputStation = file.InputStation;
            InputAddressModeName = file.InputAddressHex ? AddressModeNames[1] : AddressModeNames[0];
            InputRegisterAddress = file.InputRegisterAddress;
            InputRegisterAreaName = file.InputRegisterAreaName;
            InputValueTypeName = file.InputValueTypeName;
            InputPeriodMs = file.InputPeriodMs;

            OutputInterfaceName = file.OutputInterfaceName;
            OutputIp = file.OutputIp;
            OutputPort = file.OutputPort;
            OutputSerialPort = file.OutputSerialPort;
            OutputBaudRate = file.OutputBaudRate;
            OutputDataBits = file.OutputDataBits;
            OutputParityName = file.OutputParityName;
            OutputStopBitsName = file.OutputStopBitsName;
            OutputEndianName = file.OutputEndianName;
            OutputStation = file.OutputStation;
            OutputAddressModeName = file.OutputAddressHex ? AddressModeNames[1] : AddressModeNames[0];
            OutputRegisterAddress = file.OutputRegisterAddress;
            OutputRegisterAreaName = file.OutputRegisterAreaName;
            OutputValueTypeName = file.OutputValueTypeName;
            OutputPeriodMs = file.OutputPeriodMs;

            SelectedSimulationModelName = file.SimulationModel;
        }
        finally
        {
            _isInitializing = false;
        }

        ApplyCurrentParameters();
        _samples.Clear();
        _chartOrigin = _engine.Time;
        AppendLocalSample();
        UpdateReadouts();
    }

    private void UpdateReadouts()
    {
        ProcessValue = _engine.ProcessValue;
        PidOutput = _engine.PidOutput;
        Error = _engine.Error;
        ElapsedSeconds = _engine.Time;

        DiagnosticText = _engine.MonitorOnly
            ? "仅监控输入：不执行 PID，也不写入输出通道"
            : _engine.Controller switch
            {
                IncrementalPid inc =>
                    $"原始Δ {inc.RawDelta:F2}    实际Δ {inc.LastDelta:F2}",
                PositionalPid pos =>
                    $"积分项 {pos.IntegralTerm:F2}    Σe {pos.Integral:F2}" +
                    (pos.IsSaturated ? "    [输出饱和]" : string.Empty),
                _ => "未启动"
            };
    }
}
