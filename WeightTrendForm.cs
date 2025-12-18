using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using Krypton.Toolkit;
using MassaKWin.Core;
using MassaKWin.Ui;
using Timer = System.Windows.Forms.Timer;

namespace MassaKWin
{
    public partial class WeightTrendForm : KryptonForm
    {
        private readonly Scale _scale;
        private readonly WeightHistoryManager _historyManager;
        private Chart _chart = null!;
        private KryptonComboBox _intervalComboBox = null!;
        private KryptonCheckBox _autoUpdateCheckBox = null!;
        private Timer _timer = null!;

        public WeightTrendForm(Scale scale, WeightHistoryManager historyManager)
        {
            _scale = scale ?? throw new ArgumentNullException(nameof(scale));
            _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));

            InitializeComponent();
            ThemeManager.Apply(this);
            Text = $"Тренд: {_scale.Name}";
        }

        private void InitializeComponent()
        {
            _chart = new Chart();
            _intervalComboBox = new KryptonComboBox();
            _autoUpdateCheckBox = new KryptonCheckBox();
            _timer = new Timer();

            SuspendLayout();

            Width = 900;
            Height = 600;
            Padding = new Padding(8);

            var controlsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 50,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(10, 10, 10, 10)
            };

            _intervalComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _intervalComboBox.Items.AddRange(new object[] { "5 мин", "10 мин", "30 мин", "Вся история" });
            _intervalComboBox.SelectedIndexChanged += IntervalComboBox_SelectedIndexChanged;
            _intervalComboBox.Width = 120;
            controlsPanel.Controls.Add(_intervalComboBox);

            _autoUpdateCheckBox.Text = "Автообновление";
            _autoUpdateCheckBox.AutoSize = true;
            _autoUpdateCheckBox.CheckedChanged += AutoUpdateCheckBox_CheckedChanged;
            _autoUpdateCheckBox.Margin = new Padding(12, 12, 0, 0);
            controlsPanel.Controls.Add(_autoUpdateCheckBox);

            var chartArea = new ChartArea("Default");
            chartArea.AxisX.LabelStyle.Format = "HH:mm:ss";
            chartArea.AxisX.IntervalType = DateTimeIntervalType.Auto;
            chartArea.AxisX.Title = "Время";
            chartArea.AxisY.Title = "Масса, кг";
            chartArea.AxisY2.Enabled = AxisEnabled.True;
            chartArea.AxisY2.Minimum = 0;
            chartArea.AxisY2.Maximum = 1.1;
            chartArea.AxisY2.Title = "Стабильно";
            _chart.ChartAreas.Add(chartArea);
            _chart.Dock = DockStyle.Fill;
            _chart.Legends.Add(new Legend());

            var netSeries = new Series("Net")
            {
                ChartType = SeriesChartType.Line,
                XValueType = ChartValueType.DateTime,
                YValueType = ChartValueType.Double,
                ChartArea = "Default"
            };
            var tareSeries = new Series("Tare")
            {
                ChartType = SeriesChartType.Line,
                XValueType = ChartValueType.DateTime,
                YValueType = ChartValueType.Double,
                ChartArea = "Default"
            };
            var stableSeries = new Series("Stable")
            {
                ChartType = SeriesChartType.Line,
                XValueType = ChartValueType.DateTime,
                YValueType = ChartValueType.Double,
                ChartArea = "Default",
                YAxisType = AxisType.Secondary
            };

            _chart.Series.Add(netSeries);
            _chart.Series.Add(tareSeries);
            _chart.Series.Add(stableSeries);

            _timer.Interval = 1000;
            _timer.Tick += Timer_Tick;

            Controls.Add(_chart);
            Controls.Add(controlsPanel);

            Load += WeightTrendForm_Load;

            ResumeLayout(false);
        }

        private void WeightTrendForm_Load(object? sender, EventArgs e)
        {
            _intervalComboBox.SelectedItem = "10 мин";
            _autoUpdateCheckBox.Checked = true;
            UpdateChart();
            _timer.Start();
        }

        private void IntervalComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateChart();
        }

        private void AutoUpdateCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            if (_autoUpdateCheckBox.Checked)
            {
                UpdateChart();
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_autoUpdateCheckBox.Checked)
            {
                UpdateChart();
            }
        }

        private TimeSpan? GetSelectedWindow()
        {
            return _intervalComboBox.SelectedItem?.ToString() switch
            {
                "5 мин" => TimeSpan.FromMinutes(5),
                "10 мин" => TimeSpan.FromMinutes(10),
                "30 мин" => TimeSpan.FromMinutes(30),
                "Вся история" => null,
                _ => TimeSpan.FromMinutes(10)
            };
        }

        private void UpdateChart()
        {
            var window = GetSelectedWindow();
            var samples = window.HasValue
                ? _historyManager.GetSamples(_scale.Id, window.Value)
                : _historyManager.GetSamples(_scale.Id, _historyManager.HistoryDepth);

            var ordered = samples.OrderBy(s => s.TimestampUtc).ToList();

            var netSeries = _chart.Series["Net"];
            var tareSeries = _chart.Series["Tare"];
            var stableSeries = _chart.Series["Stable"];

            netSeries.Points.Clear();
            tareSeries.Points.Clear();
            stableSeries.Points.Clear();

            foreach (var sample in ordered)
            {
                var time = sample.TimestampUtc.ToLocalTime();
                netSeries.Points.AddXY(time, sample.NetGrams / 1000.0);
                tareSeries.Points.AddXY(time, sample.TareGrams / 1000.0);
                stableSeries.Points.AddXY(time, sample.Stable ? 1 : 0);
            }
        }
    }
}
