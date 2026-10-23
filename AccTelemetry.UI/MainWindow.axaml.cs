using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AccTelemetry.Domain;
using AccTelemetry.Domain.Models;
using AccTelemetry.Domain.Repositories;
using AccTelemetry.IO.Motec;
using AccTelemetry.IO.Persistence;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AccTelemetry.UI;

public partial class MainWindow : Window
{
    private readonly ITelemetryRepository _repository;
    private readonly string _motecFolder;

    private LapTelemetry? _mainTelemetry;
    private LapTelemetry? _refTelemetry;
    private double[] _timeDelta = [];

    private double[] _trackX = [];
    private double[] _trackY = [];
    private Marker? _carMarker;

    private VerticalLine? _cursorDelta;
    private VerticalLine? _cursorSpeed;
    private VerticalLine? _cursorPedals;
    private VerticalLine? _cursorGear;

    public MainWindow()
    {
        InitializeComponent();

        _motecFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Assetto Corsa Competizione",
            "MoTeC"
        );

        string dbPath = ResolveDatabasePath();
        _repository = new SqliteTelemetryRepository(dbPath);

        SetupPlotStyles();
        ConfigureSharedAxes();
        HookCursorEvents();

        Loaded += async (_, _) =>
        {
            await _repository.InitializeAsync();
            TxtFolderLocation.Text = $"Pasta: {_motecFolder}";
            ScanMotecFolder();
        };
    }

    private void ScanMotecFolder()
    {
        if (!Directory.Exists(_motecFolder))
        {
            TxtFolderLocation.Text = $"Pasta do MoTeC não encontrada em: {_motecFolder}";
            return;
        }

        var files = new DirectoryInfo(_motecFolder)
            .GetFiles("*.ldx")
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f =>
            {
                var parts = f.Name.Replace(".ldx", "").Split('-');
                string track = parts.Length > 0 ? parts[0].ToUpper() : "PISTA";
                string car = parts.Length > 1 ? parts[1].Replace("_", " ").ToUpper() : "CARRO";

                return new MotecFileCardItem(
                    f.Name, f.FullName, track, car,
                    $"Gravado em: {f.LastWriteTime:dd/MM/yyyy HH:mm} | Tamanho: {f.Length / 1024.0:F1} KB"
                );
            }).ToList();

        LstMotecFiles.ItemsSource = files;
    }

    private void OnRefreshFilesClicked(object? sender, RoutedEventArgs e) => ScanMotecFolder();

    private async void OnOpenSessionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string ldxPath) return;

        var ldPath = Path.ChangeExtension(ldxPath, ".ld");
        if (!File.Exists(ldPath)) return;

        var ldxParser = new LdxParser();
        var beacons = ldxParser.ParseBeacons(ldxPath);
        var ldFile = LdParser.Parse(ldPath);

        var session = SessionNormalizer.BuildSession(ldFile, beacons);
        await _repository.SaveSessionAsync(session);

        ViewMenu.IsVisible = false;
        ViewPlots.IsVisible = true;

        var lapItems = session.Laps.Select(l => new LapItem(
            l.Id,
            l.IsOutLap ? $"Volta {l.LapNumber} [OUT-LAP]" : $"Volta {l.LapNumber} - {l.LapTime:mm\\:ss\\.fff}",
            $"{l.LapDistanceMeters:F0}m | {(l.IsValidLap ? "Válida" : l.InvalidationReason)}"
        )).ToList();

        LstLaps.ItemsSource = lapItems;

        var refItems = new List<LapItem> { new LapItem(Guid.Empty, "Nenhuma Referência", "") };
        refItems.AddRange(lapItems);
        CmbRefLap.ItemsSource = refItems;
        CmbRefLap.DisplayMemberBinding = new Avalonia.Data.Binding("Title");

        var validLaps = session.Laps.Where(l => l.IsValidLap).OrderBy(l => l.LapTime).ToList();

        if (validLaps.Count >= 2)
        {
            CmbRefLap.SelectedIndex = refItems.FindIndex(r => r.Id == validLaps[0].Id);
            LstLaps.SelectedIndex = lapItems.FindIndex(r => r.Id == validLaps[1].Id);
        }
        else if (validLaps.Count == 1)
        {
            CmbRefLap.SelectedIndex = 0;
            LstLaps.SelectedIndex = lapItems.FindIndex(r => r.Id == validLaps[0].Id);
        }
        else
        {
            CmbRefLap.SelectedIndex = 0;
            if (lapItems.Count > 0) LstLaps.SelectedIndex = 0;
        }
    }

    private void OnBackToMenuClicked(object? sender, RoutedEventArgs e)
    {
        ViewPlots.IsVisible = false;
        ViewMenu.IsVisible = true;
    }

    private async void OnLapSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (LstLaps.SelectedItem is not LapItem selectedLap) return;
        _mainTelemetry = await _repository.GetLapTelemetryAsync(selectedLap.Id);
        RefreshAllPlots();
    }

    private async void OnRefLapSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (CmbRefLap.SelectedItem is not LapItem selectedRef) return;

        if (selectedRef.Id == Guid.Empty)
            _refTelemetry = null;
        else
            _refTelemetry = await _repository.GetLapTelemetryAsync(selectedRef.Id);

        RefreshAllPlots();
    }

    private void OnPlotVisibilityChanged(object? sender, RoutedEventArgs e)
    {
        if (BorderDelta == null || BorderSpeed == null || BorderPedals == null || BorderGear == null || PlotsGrid == null)
            return;

        bool showDelta = ChkShowDelta.IsChecked == true;
        bool showSpeed = ChkShowSpeed.IsChecked == true;
        bool showPedals = ChkShowPedals.IsChecked == true;
        bool showGear = ChkShowGear.IsChecked == true;

        BorderDelta.IsVisible = showDelta;
        BorderSpeed.IsVisible = showSpeed;
        BorderPedals.IsVisible = showPedals;
        BorderGear.IsVisible = showGear;

        string r1 = showDelta ? "12*" : "0";
        string r2 = showSpeed ? "20*" : "0";
        string r3 = showPedals ? "15*" : "0";
        string r4 = showGear ? "10*" : "0";

        PlotsGrid.RowDefinitions = RowDefinitions.Parse($"{r1},{r2},{r3},{r4}");
    }

    private void OnPedalsFilterChanged(object? sender, RoutedEventArgs e)
    {
        RefreshAllPlots();
    }

    private void CalculateDeltaTime()
    {
        if (_mainTelemetry == null) return;
        if (_refTelemetry == null || _refTelemetry.SampleCount == 0)
        {
            _timeDelta = new double[_mainTelemetry.SampleCount];
            return;
        }

        _timeDelta = new double[_mainTelemetry.SampleCount];
        for (int i = 0; i < _mainTelemetry.SampleCount; i++)
        {
            double dist = _mainTelemetry.Distance[i];
            double refTime = InterpolateTime(_refTelemetry.Distance, _refTelemetry.Time, dist);
            _timeDelta[i] = _mainTelemetry.Time[i] - refTime;
        }
    }

    private static double InterpolateTime(double[] refDist, double[] refTime, double targetDist)
    {
        int idx = Array.BinarySearch(refDist, targetDist);
        if (idx >= 0) return refTime[idx];

        idx = ~idx;
        if (idx == 0) return refTime[0];
        if (idx >= refDist.Length) return refTime[^1];

        double d0 = refDist[idx - 1], d1 = refDist[idx];
        double t0 = refTime[idx - 1], t1 = refTime[idx];

        if (d1 == d0) return t0;
        return t0 + (t1 - t0) * ((targetDist - d0) / (d1 - d0));
    }

    private void StyleLegend(Plot plot)
    {
        var legend = plot.ShowLegend(Alignment.UpperRight);
        legend.BackgroundColor = Color.FromHex("#18181b").WithAlpha(0.8);
        legend.FontColor = Colors.White;
        legend.OutlineColor = Color.FromHex("#3f3f46");
    }

    private void SetupPlotStyles()
    {
        var plots = new[] { PlotDelta, PlotSpeed, PlotPedals, PlotGear };
        foreach (var p in plots)
        {
            var plot = p.Plot;
            plot.FigureBackground.Color = Color.FromHex("#121214");
            plot.DataBackground.Color = Color.FromHex("#18181b");
            plot.Axes.Color(Color.FromHex("#71717a"));
            plot.Grid.MajorLineColor = Color.FromHex("#27272a");
            StyleLegend(plot);
        }

        PlotDelta.Plot.Axes.Left.Label.Text = "Delta (s)";
        PlotDelta.Plot.Axes.Left.Label.ForeColor = Colors.White;

        PlotSpeed.Plot.Axes.Left.Label.Text = "Velocidade (km/h)";
        PlotSpeed.Plot.Axes.Left.Label.ForeColor = Colors.White;

        PlotPedals.Plot.Axes.Left.Label.Text = "Pedais (%)";
        PlotPedals.Plot.Axes.Left.Label.ForeColor = Colors.White;
        PlotPedals.Plot.Axes.SetLimitsY(-2, 102);
        PlotPedals.Plot.Axes.Rules.Add(new ScottPlot.AxisRules.LockedVertical(PlotPedals.Plot.Axes.Left, -2, 102));

        PlotGear.Plot.Axes.Left.Label.Text = "Marcha";
        PlotGear.Plot.Axes.Left.Label.ForeColor = Colors.White;
        PlotGear.Plot.Axes.Bottom.Label.Text = "Distância da Pista (metros)";
        PlotGear.Plot.Axes.Bottom.Label.ForeColor = Colors.White;
        PlotGear.Plot.Axes.SetLimitsY(0, 7);
        PlotGear.Plot.Axes.Rules.Add(new ScottPlot.AxisRules.LockedVertical(PlotGear.Plot.Axes.Left, 0, 7));

        PlotTrackMap.Plot.FigureBackground.Color = Color.FromHex("#121214");
        PlotTrackMap.Plot.DataBackground.Color = Color.FromHex("#121214");
        PlotTrackMap.Plot.Axes.Frameless();
        PlotTrackMap.Plot.HideGrid();
        PlotTrackMap.Plot.Axes.SquareUnits();
    }

    private void ConfigureSharedAxes()
    {
        void SyncHorizontalAxes(AvaPlot source, params AvaPlot[] targets)
        {
            var sourceLimits = source.Plot.Axes.GetLimits();
            double left = sourceLimits.Left;
            double right = sourceLimits.Right;

            if (left < 0)
            {
                right -= left;
                left = 0;
                source.Plot.Axes.SetLimitsX(left, right);
            }

            foreach (var target in targets)
            {
                var targetLimits = target.Plot.Axes.GetLimits();
                bool isDifferent = Math.Abs(targetLimits.Left - left) > 0.01 ||
                                   Math.Abs(targetLimits.Right - right) > 0.01;

                if (isDifferent)
                {
                    target.Plot.Axes.SetLimitsX(left, right);
                    target.Refresh();
                }
            }
        }

        PlotDelta.Plot.RenderManager.RenderFinished += (_, _) => SyncHorizontalAxes(PlotDelta, PlotSpeed, PlotPedals, PlotGear);
        PlotSpeed.Plot.RenderManager.RenderFinished += (_, _) => SyncHorizontalAxes(PlotSpeed, PlotDelta, PlotPedals, PlotGear);
        PlotPedals.Plot.RenderManager.RenderFinished += (_, _) => SyncHorizontalAxes(PlotPedals, PlotDelta, PlotSpeed, PlotGear);
        PlotGear.Plot.RenderManager.RenderFinished += (_, _) => SyncHorizontalAxes(PlotGear, PlotDelta, PlotSpeed, PlotPedals);
    }

    private void RefreshAllPlots()
    {
        if (_mainTelemetry == null) return;

        PlotDelta.Plot.Clear();
        PlotSpeed.Plot.Clear();
        PlotPedals.Plot.Clear();
        PlotGear.Plot.Clear();
        PlotTrackMap.Plot.Clear();

        CalculateDeltaTime();

        bool showThrottle = ChkShowThrottle?.IsChecked == true;
        bool showBrake = ChkShowBrake?.IsChecked == true;

        if (_refTelemetry != null)
        {
            var refSpd = PlotSpeed.Plot.Add.ScatterLine(_refTelemetry.Distance, _refTelemetry.Speed);
            refSpd.Color = Color.FromHex("#fb923c").WithAlpha(0.5f);
            refSpd.LineWidth = 2;
            refSpd.LegendText = "Velocidade (Ref)";

            if (showThrottle)
            {
                var refTh = PlotPedals.Plot.Add.ScatterLine(_refTelemetry.Distance, _refTelemetry.Throttle);
                refTh.Color = Color.FromHex("#fb923c").WithAlpha(0.5f);
                refTh.LineWidth = 2;
                refTh.LegendText = "Acelerador (Ref)";
            }

            if (showBrake)
            {
                var refBrk = PlotPedals.Plot.Add.ScatterLine(_refTelemetry.Distance, _refTelemetry.Brake);
                refBrk.Color = Color.FromHex("#a1a1aa").WithAlpha(0.5f);
                refBrk.LineWidth = 2;
                refBrk.LegendText = "Freio (Ref)";
            }

            var refGear = PlotGear.Plot.Add.ScatterLine(_refTelemetry.Distance, _refTelemetry.Gear);
            refGear.ConnectStyle = ConnectStyle.StepHorizontal;
            refGear.Color = Color.FromHex("#fb923c").WithAlpha(0.5f);
            refGear.LineWidth = 2;
            refGear.LegendText = "Marcha (Ref)";
        }

        var zeroLine = PlotDelta.Plot.Add.HorizontalLine(0);
        zeroLine.Color = Color.FromHex("#52525b");
        zeroLine.LineWidth = 1;
        zeroLine.LinePattern = LinePattern.Dotted;

        var deltaLine = PlotDelta.Plot.Add.ScatterLine(_mainTelemetry.Distance, _timeDelta);
        deltaLine.Color = Color.FromHex("#a8a29e");
        deltaLine.LineWidth = 2;
        deltaLine.LegendText = "Time Delta (s)";

        var spdLine = PlotSpeed.Plot.Add.ScatterLine(_mainTelemetry.Distance, _mainTelemetry.Speed);
        spdLine.Color = Color.FromHex("#38bdf8");
        spdLine.LineWidth = 2;
        spdLine.LegendText = "Velocidade (Atual)";

        if (showThrottle)
        {
            var thLine = PlotPedals.Plot.Add.ScatterLine(_mainTelemetry.Distance, _mainTelemetry.Throttle);
            thLine.Color = Color.FromHex("#4ade80");
            thLine.LineWidth = 2;
            thLine.LegendText = "Acelerador (Atual)";
        }

        if (showBrake)
        {
            var brkLine = PlotPedals.Plot.Add.ScatterLine(_mainTelemetry.Distance, _mainTelemetry.Brake);
            brkLine.Color = Color.FromHex("#f87171");
            brkLine.LineWidth = 2;
            brkLine.LegendText = "Freio (Atual)";
        }

        var gearLine = PlotGear.Plot.Add.ScatterLine(_mainTelemetry.Distance, _mainTelemetry.Gear);
        gearLine.ConnectStyle = ConnectStyle.StepHorizontal;
        gearLine.Color = Color.FromHex("#fbbf24");
        gearLine.LineWidth = 2;
        gearLine.LegendText = "Marcha (Atual)";

        _cursorDelta = PlotDelta.Plot.Add.VerticalLine(0);
        _cursorSpeed = PlotSpeed.Plot.Add.VerticalLine(0);
        _cursorPedals = PlotPedals.Plot.Add.VerticalLine(0);
        _cursorGear = PlotGear.Plot.Add.VerticalLine(0);

        var cursorColor = Color.FromHex("#e4e4e7").WithAlpha(0.6f);
        foreach (var c in new[] { _cursorDelta, _cursorSpeed, _cursorPedals, _cursorGear })
        {
            c.Color = cursorColor;
            c.LineWidth = 1.5f;
            c.LinePattern = LinePattern.Dashed;
        }

        if (_mainTelemetry.YawRate.Length > 0)
        {
            (_trackX, _trackY) = TrackMapGenerator.Generate2D(_mainTelemetry.Speed, _mainTelemetry.YawRate, _mainTelemetry.Time);

            if (_trackX.Length > 0)
            {
                var trackLine = PlotTrackMap.Plot.Add.ScatterLine(_trackX, _trackY);
                trackLine.Color = Color.FromHex("#3f3f46");
                trackLine.LineWidth = 3;

                var startMarker = PlotTrackMap.Plot.Add.Marker(_trackX[0], _trackY[0]);
                startMarker.Color = Color.FromHex("#22c55e");
                startMarker.Size = 7;
                startMarker.Shape = MarkerShape.FilledCircle;

                _carMarker = PlotTrackMap.Plot.Add.Marker(_trackX[0], _trackY[0]);
                _carMarker.Color = Color.FromHex("#ef4444");
                _carMarker.Size = 13;
                _carMarker.Shape = MarkerShape.FilledCircle;

                PlotTrackMap.Plot.Axes.AutoScale();
                PlotTrackMap.Refresh();
            }
        }

        double maxDist = _mainTelemetry.Distance[^1];
        if (_refTelemetry != null) maxDist = Math.Max(maxDist, _refTelemetry.Distance[^1]);

        double topSpeed = _mainTelemetry.Speed.Length > 0 ? _mainTelemetry.Speed.Max() : 250;

        PlotDelta.Plot.Axes.SetLimits(0, maxDist, -1.5, 1.5);
        PlotSpeed.Plot.Axes.SetLimits(0, maxDist, 0, Math.Ceiling(topSpeed * 1.05));
        PlotPedals.Plot.Axes.SetLimits(0, maxDist, -2, 102);
        PlotGear.Plot.Axes.SetLimits(0, maxDist, 0, 7);

        PlotDelta.Refresh();
        PlotSpeed.Refresh();
        PlotPedals.Refresh();
        PlotGear.Refresh();
    }

    private void HookCursorEvents()
    {
        foreach (var p in new[] { PlotDelta, PlotSpeed, PlotPedals, PlotGear })
        {
            p.PointerMoved += OnPlotPointerMoved;
            p.PointerExited += (_, _) => TooltipCard.IsVisible = false;
            p.PointerEntered += (_, _) => TooltipCard.IsVisible = true;
            p.AddHandler(InputElement.PointerWheelChangedEvent, OnPlotPointerWheelChanged, RoutingStrategies.Tunnel);
        }
    }

    private void OnPlotPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not AvaPlot activePlot || _mainTelemetry is null) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;

            double zoomFactor = e.Delta.Y > 0 ? 1.25 : 0.80;

            var point = e.GetCurrentPoint(activePlot);
            var pixel = new Pixel((float)point.Position.X, (float)point.Position.Y);
            var coords = activePlot.Plot.GetCoordinates(pixel);

            var limits = activePlot.Plot.Axes.GetLimits();
            double currentSpanX = limits.Right - limits.Left;
            double newSpanX = currentSpanX / zoomFactor;

            double mouseFraction = (coords.X - limits.Left) / currentSpanX;
            double newLeft = coords.X - (mouseFraction * newSpanX);
            double newRight = newLeft + newSpanX;

            double maxTrackDist = _mainTelemetry.Distance[^1];
            if (newLeft < 0) { newRight -= newLeft; newLeft = 0; }
            if (newRight > maxTrackDist) newRight = maxTrackDist;

            activePlot.Plot.Axes.SetLimitsX(newLeft, newRight);
            activePlot.Refresh();
        }
    }

    private void OnPlotPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_mainTelemetry is null || _cursorSpeed is null || sender is not AvaPlot activePlot)
            return;

        var point = e.GetCurrentPoint(activePlot);

        if (point.Properties.IsLeftButtonPressed || point.Properties.IsRightButtonPressed)
        {
            TooltipCard.IsVisible = false;
            return;
        }

        TooltipCard.IsVisible = true;

        var pixel = new Pixel((float)point.Position.X, (float)point.Position.Y);
        var coords = activePlot.Plot.GetCoordinates(pixel);

        double targetDist = Math.Clamp(coords.X, 0, _mainTelemetry.Distance[^1]);

        _cursorDelta!.X = targetDist;
        _cursorSpeed.X = targetDist;
        _cursorPedals!.X = targetDist;
        _cursorGear!.X = targetDist;

        // --- VALORES DA VOLTA ATUAL (AZUL) ---
        int idx = Array.BinarySearch(_mainTelemetry.Distance, targetDist);
        if (idx < 0) idx = ~idx;
        idx = Math.Clamp(idx, 0, _mainTelemetry.SampleCount - 1);

        TxtHoverSpeed.Text = $"{_mainTelemetry.Speed[idx]:F0} km/h";
        TxtHoverGear.Text = $"{_mainTelemetry.Gear[idx]:F0}";
        TxtHoverThrottle.Text = $"{_mainTelemetry.Throttle[idx]:F0}%";
        TxtHoverBrake.Text = $"{_mainTelemetry.Brake[idx]:F0}%";

        // --- VALORES DA VOLTA DE REFERÊNCIA (LARANJA) ---
        if (_refTelemetry != null)
        {
            int refIdx = Array.BinarySearch(_refTelemetry.Distance, targetDist);
            if (refIdx < 0) refIdx = ~refIdx;
            refIdx = Math.Clamp(refIdx, 0, _refTelemetry.SampleCount - 1);

            TxtHoverSpeedRef.Text = $"({_refTelemetry.Speed[refIdx]:F0} km/h)";
            TxtHoverGearRef.Text = $"({_refTelemetry.Gear[refIdx]:F0})";
            TxtHoverThrottleRef.Text = $"({_refTelemetry.Throttle[refIdx]:F0}%)";
            TxtHoverBrakeRef.Text = $"({_refTelemetry.Brake[refIdx]:F0}%)";

            TxtHoverSpeedRef.IsVisible = true;
            TxtHoverGearRef.IsVisible = true;
            TxtHoverThrottleRef.IsVisible = true;
            TxtHoverBrakeRef.IsVisible = true;
        }
        else
        {
            TxtHoverSpeedRef.IsVisible = false;
            TxtHoverGearRef.IsVisible = false;
            TxtHoverThrottleRef.IsVisible = false;
            TxtHoverBrakeRef.IsVisible = false;
        }

        if (_trackX.Length > idx && _trackY.Length > idx && _carMarker != null)
        {
            _carMarker.X = _trackX[idx];
            _carMarker.Y = _trackY[idx];
            PlotTrackMap.Refresh();
        }

        double deltaValue = _timeDelta.Length > idx ? _timeDelta[idx] : 0;
        string deltaSignal = deltaValue > 0 ? "+" : "";

        TxtHoverDist.Text = $"{targetDist:F0} m";
        TxtHoverDelta.Text = $"Delta: {deltaSignal}{deltaValue:F2}s";
        TxtHoverDelta.Foreground = deltaValue < 0 ? Avalonia.Media.Brushes.LightGreen : (deltaValue > 0 ? Avalonia.Media.Brushes.LightCoral : Avalonia.Media.Brushes.Gray);

        var posInContainer = e.GetPosition(PlotsContainer);
        double xOffset = 20;
        double yOffset = 20;

        double targetX = posInContainer.X + xOffset;
        double targetY = posInContainer.Y + yOffset;

        if (targetX + TooltipCard.Bounds.Width > PlotsContainer.Bounds.Width)
            targetX = posInContainer.X - TooltipCard.Bounds.Width - 10;

        if (targetY + TooltipCard.Bounds.Height > PlotsContainer.Bounds.Height)
            targetY = posInContainer.Y - TooltipCard.Bounds.Height - 10;

        Canvas.SetLeft(TooltipCard, targetX);
        Canvas.SetTop(TooltipCard, targetY);

        PlotDelta.Refresh();
        PlotSpeed.Refresh();
        PlotPedals.Refresh();
        PlotGear.Refresh();
    }

    private static string ResolveDatabasePath()
    {
        string localPath = Path.Combine(AppContext.BaseDirectory, "telemetry.db");
        if (File.Exists(localPath)) return localPath;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var found = Directory.GetFiles(dir.FullName, "telemetry.db", SearchOption.AllDirectories).FirstOrDefault();
            if (found != null) return found;
            dir = dir.Parent;
        }
        return localPath;
    }
}

public record MotecFileCardItem(string FileName, string FullPath, string Track, string Car, string Details);
public record LapItem(Guid Id, string Title, string Subtitle);