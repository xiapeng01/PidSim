using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PidSimulator.Models;
using PidSimulator.Models.Simulation;

namespace PidSimulator.Controls;

/// <summary>
/// 无第三方依赖的双 Y 轴曲线控件：左轴为设定值/过程值，右轴为 PID 输出。
/// 颜色通过依赖属性绑定主题资源，切换主题时自动重绘。
/// </summary>
public sealed class ChartCanvas : Canvas
{
    private static readonly Brush DefaultGridBrush = CreateFrozenBrush(0x33, 0x3B, 0x4D, 0x6B);
    private static readonly Brush DefaultBorderBrush = CreateFrozenBrush(0x99, 0x3B, 0x4D, 0x6B);
    private static readonly Brush DefaultTextBrush = CreateFrozenBrush(0xFF, 0x9A, 0xA9, 0xC0);
    private static readonly Brush DefaultMutedBrush = CreateFrozenBrush(0xFF, 0x6E, 0x7F, 0x99);
    private static readonly Brush DefaultProcessBrush = CreateFrozenBrush(0xFF, 0x38, 0xBD, 0xF8);
    private static readonly Brush DefaultSetpointBrush = CreateFrozenBrush(0xFF, 0x34, 0xD3, 0x99);
    private static readonly Brush DefaultOutputBrush = CreateFrozenBrush(0xFF, 0xA7, 0x8B, 0xFA);

    private bool _suspendRendering;

    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples),
        typeof(ObservableCollection<ChartSample>),
        typeof(ChartCanvas),
        new PropertyMetadata(null, OnSamplesChanged));

    public static readonly DependencyProperty GridBrushProperty =
        RegisterBrush(nameof(GridBrush), DefaultGridBrush);

    public static readonly DependencyProperty PlotBorderBrushProperty =
        RegisterBrush(nameof(PlotBorderBrush), DefaultBorderBrush);

    public static readonly DependencyProperty TextBrushProperty =
        RegisterBrush(nameof(TextBrush), DefaultTextBrush);

    public static readonly DependencyProperty MutedBrushProperty =
        RegisterBrush(nameof(MutedBrush), DefaultMutedBrush);

    public static readonly DependencyProperty ProcessBrushProperty =
        RegisterBrush(nameof(ProcessBrush), DefaultProcessBrush);

    public static readonly DependencyProperty SetpointBrushProperty =
        RegisterBrush(nameof(SetpointBrush), DefaultSetpointBrush);

    public static readonly DependencyProperty OutputBrushProperty =
        RegisterBrush(nameof(OutputBrush), DefaultOutputBrush);

    public static readonly DependencyProperty PlotBackgroundBrushProperty =
        RegisterBrush(nameof(PlotBackgroundBrush), Brushes.Transparent);

    static ChartCanvas()
    {
        ClipToBoundsProperty.OverrideMetadata(
            typeof(ChartCanvas),
            new FrameworkPropertyMetadata(true));
    }

    public ObservableCollection<ChartSample>? Samples
    {
        get => (ObservableCollection<ChartSample>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    /// <summary>窗口拖动等交互期间暂停重绘，交互结束后恢复并刷新一次。</summary>
    public bool SuspendRendering
    {
        get => _suspendRendering;
        set
        {
            if (_suspendRendering == value)
                return;

            _suspendRendering = value;
            if (!value)
                InvalidateVisual();
        }
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush PlotBorderBrush
    {
        get => (Brush)GetValue(PlotBorderBrushProperty);
        set => SetValue(PlotBorderBrushProperty, value);
    }

    public Brush TextBrush
    {
        get => (Brush)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public Brush MutedBrush
    {
        get => (Brush)GetValue(MutedBrushProperty);
        set => SetValue(MutedBrushProperty, value);
    }

    public Brush ProcessBrush
    {
        get => (Brush)GetValue(ProcessBrushProperty);
        set => SetValue(ProcessBrushProperty, value);
    }

    public Brush SetpointBrush
    {
        get => (Brush)GetValue(SetpointBrushProperty);
        set => SetValue(SetpointBrushProperty, value);
    }

    public Brush OutputBrush
    {
        get => (Brush)GetValue(OutputBrushProperty);
        set => SetValue(OutputBrushProperty, value);
    }

    public Brush PlotBackgroundBrush
    {
        get => (Brush)GetValue(PlotBackgroundBrushProperty);
        set => SetValue(PlotBackgroundBrushProperty, value);
    }

    private static DependencyProperty RegisterBrush(string name, Brush defaultValue)
    {
        return DependencyProperty.Register(
            name,
            typeof(Brush),
            typeof(ChartCanvas),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
    }

    private static Brush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        SolidColorBrush brush = new(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ChartCanvas canvas = (ChartCanvas)d;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= canvas.OnSamplesCollectionChanged;

        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += canvas.OnSamplesCollectionChanged;

        if (!canvas._suspendRendering)
            canvas.InvalidateVisual();
    }

    private void OnSamplesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_suspendRendering)
            InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        const double left = 54;
        const double right = 60;
        const double top = 18;
        const double bottom = 30;

        double plotWidth = width - left - right;
        double plotHeight = height - top - bottom;
        if (plotWidth <= 10 || plotHeight <= 10)
            return;

        ObservableCollection<ChartSample>? samples = Samples;

        double processMin = 0;
        double processMax = 0;
        double outputMin = 0;
        double outputMax = 0;

        if (samples is { Count: > 0 })
        {
            foreach (ChartSample s in samples)
            {
                processMin = Math.Min(processMin, Math.Min(s.Setpoint, s.ProcessValue));
                processMax = Math.Max(processMax, Math.Max(s.Setpoint, s.ProcessValue));
                outputMin = Math.Min(outputMin, s.Output);
                outputMax = Math.Max(outputMax, s.Output);
            }
        }

        (processMin, processMax) = MathUtil.ExpandRange(processMin, processMax);
        (outputMin, outputMax) = MathUtil.ExpandRange(outputMin, outputMax);

        double timeMin = samples is { Count: > 0 } ? samples[0].Time : 0;
        double timeMax = samples is { Count: > 0 } ? samples[^1].Time : 1;
        if (timeMax - timeMin < 1e-9)
            timeMax = timeMin + 1;

        Brush textBrush = TextBrush ?? Brushes.Transparent;
        Brush mutedBrush = MutedBrush ?? textBrush;
        Pen gridPen = new(GridBrush ?? Brushes.Transparent, 1);
        Pen borderPen = new(PlotBorderBrush ?? Brushes.Transparent, 1);

        if (PlotBackgroundBrush is not null)
            dc.DrawRectangle(PlotBackgroundBrush, null, new Rect(left, top, plotWidth, plotHeight));

        for (int i = 0; i <= 4; i++)
        {
            double y = top + plotHeight * i / 4.0;
            dc.DrawLine(gridPen, new Point(left, y), new Point(left + plotWidth, y));

            double pvValue = processMax - (processMax - processMin) * i / 4.0;
            double outValue = outputMax - (outputMax - outputMin) * i / 4.0;

            DrawText(dc, pvValue.ToString("F2", CultureInfo.InvariantCulture),
                left - 8, y, textBrush, TextAlignment.Right);
            DrawText(dc, outValue.ToString("F0", CultureInfo.InvariantCulture),
                left + plotWidth + 8, y, textBrush, TextAlignment.Left);
        }

        for (int i = 0; i <= 5; i++)
        {
            double x = left + plotWidth * i / 5.0;
            dc.DrawLine(gridPen, new Point(x, top), new Point(x, top + plotHeight));

            double t = timeMin + (timeMax - timeMin) * i / 5.0;
            DrawText(dc, $"{t:F1}s", x, top + plotHeight + 15, mutedBrush, TextAlignment.Center);
        }

        dc.DrawRectangle(null, borderPen, new Rect(left, top, plotWidth, plotHeight));

        DrawText(dc, "PV / SP", left - 4, 6, textBrush, TextAlignment.Left, 10);
        DrawText(dc, "PID OUT", left + plotWidth + 4, 6, textBrush, TextAlignment.Right, 10);

        if (samples is not { Count: > 0 })
            return;

        Pen outputPen = new(OutputBrush ?? Brushes.Transparent, 1.6);
        Pen setpointPen = new(SetpointBrush ?? Brushes.Transparent, 1.4)
        {
            DashStyle = DashStyles.Dash
        };
        Pen processPen = new(ProcessBrush ?? Brushes.Transparent, 2.2);

        DrawSeries(dc, samples, outputPen,
            left, top, plotWidth, plotHeight,
            timeMin, timeMax, outputMin, outputMax,
            s => s.Output);

        DrawSeries(dc, samples, setpointPen,
            left, top, plotWidth, plotHeight,
            timeMin, timeMax, processMin, processMax,
            s => s.Setpoint);

        DrawSeries(dc, samples, processPen,
            left, top, plotWidth, plotHeight,
            timeMin, timeMax, processMin, processMax,
            s => s.ProcessValue);
    }

    private static void DrawSeries(
        DrawingContext dc,
        IReadOnlyList<ChartSample> samples,
        Pen pen,
        double left,
        double top,
        double plotWidth,
        double plotHeight,
        double timeMin,
        double timeMax,
        double valueMin,
        double valueMax,
        Func<ChartSample, double> valueSelector)
    {
        StreamGeometry geometry = new();

        using (StreamGeometryContext context = geometry.Open())
        {
            bool started = false;

            foreach (ChartSample sample in samples)
            {
                double x = left + (sample.Time - timeMin) / (timeMax - timeMin) * plotWidth;
                double value = valueSelector(sample);
                double y = top + (1.0 - (value - valueMin) / (valueMax - valueMin)) * plotHeight;

                Point point = new(x, y);

                if (!started)
                {
                    context.BeginFigure(point, false, false);
                    started = true;
                }
                else
                {
                    context.LineTo(point, true, true);
                }
            }
        }

        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawText(
        DrawingContext dc,
        string text,
        double x,
        double y,
        Brush brush,
        TextAlignment alignment,
        double fontSize = 10)
    {
        FormattedText formatted = new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            brush,
            1.25);

        double textX = alignment switch
        {
            TextAlignment.Center => x - formatted.Width / 2.0,
            TextAlignment.Right => x - formatted.Width,
            _ => x
        };

        dc.DrawText(formatted, new Point(textX, y - formatted.Height / 2.0));
    }
}
