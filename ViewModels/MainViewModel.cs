using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
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

    public IReadOnlyList<string> SimulationModelNames { get; } = new[] { "水泵流量计", "加热", "制冷" };

    public string SelectedSimulationModelName
    {
        get => SelectedSimulationModel switch
        {
            SimulationModel.Heating => "加热",
            SimulationModel.Cooling => "制冷",
            _ => "水泵流量计"
        };
        set
        {
            SelectedSimulationModel = value switch
            {
                "加热" => SimulationModel.Heating,
                "制冷" => SimulationModel.Cooling,
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
            OnPropertyChanged(nameof(PlantParameterHeader));
            OnPropertyChanged(nameof(SetpointMinimum));
            OnPropertyChanged(nameof(SetpointMaximum));

            SwitchPlant();
        }
    }

    public bool IsFlowModel => SelectedSimulationModel == SimulationModel.PumpFlow;

    public bool IsTemperatureModel => SelectedSimulationModel != SimulationModel.PumpFlow;

    public string PlantParameterHeader =>
        SelectedSimulationModel == SimulationModel.Cooling ? "制冷模型参数" : "加热模型参数";

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
        else
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
        }

        plant.ClampCurrentValue();
    }

    /// <summary>停止状态下切换仿真对象：新建对象、复位状态并调整设定值到有效范围。</summary>
    private void SwitchPlant()
    {
        double initial = SelectedSimulationModel == SimulationModel.PumpFlow
            ? 0
            : AmbientTemperature;

        SimulationPlant plant = SelectedSimulationModel switch
        {
            SimulationModel.Heating => new HeatingPlant(initial),
            SimulationModel.Cooling => new CoolingPlant(initial),
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

    private void UpdateReadouts()
    {
        ProcessValue = _engine.ProcessValue;
        PidOutput = _engine.PidOutput;
        Error = _engine.Error;
        ElapsedSeconds = _engine.Time;

        DiagnosticText = _engine.Controller switch
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
