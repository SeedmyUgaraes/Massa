using System;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using MassaKWin.Core;
using Timer = System.Windows.Forms.Timer;

namespace MassaKWin
{
    public partial class WeightTrendForm : Form
    {
        private readonly Scale _scale;
        private readonly WeightHistoryManager _historyManager;
        private Chart _chart;
        private ComboBox _intervalComboBox;
        private CheckBox _autoUpdateCheckBox;
        private Timer _timer;

        public WeightTrendForm(Scale scale, WeightHistoryManager historyManager)
        {
            _scale = scale ?? throw new ArgumentNullException(nameof(scale));
            _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            _chart = new Chart();
            _intervalComboBox = new ComboBox();
            _autoUpdateCheckBox = new CheckBox();
            _timer = new Timer();

            SuspendLayout();

            Text = "Тренд веса";
            Width = 800;
            Height = 600;

            var controlsPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
            };

            _intervalComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _intervalComboBox.Items.AddRange(new object[] { "5 минут", "10 минут", "30 минут", "60 минут" });
            _intervalComboBox.SelectedIndex = 1;
            _intervalComboBox.Width = 120;
            _intervalComboBox.Location = new System.Drawing.Point(10, 10);
            _intervalComboBox.SelectedIndexChanged += IntervalComboBox_SelectedIndexChanged;

            _autoUpdateCheckBox.Text = "Автообновление";
            _autoUpdateCheckBox.AutoSize = true;
            _autoUpdateCheckBox.Location = new System.Drawing.Point(150, 12);
            _autoUpdateCheckBox.CheckedChanged += AutoUpdateCheckBox_CheckedChanged;

            controlsPanel.Controls.Add(_intervalComboBox);
            controlsPanel.Controls.Add(_autoUpdateCheckBox);

            var chartArea = new ChartArea("Default");
            chartArea.AxisX.LabelStyle.Format = "HH:mm:ss";
            chartArea.AxisX.IntervalType = DateTimeIntervalType.Auto;
            chartArea.AxisX.Title = "Время";
            chartArea.AxisY.Title = "Масса, кг";
            _chart.ChartAreas.Add(chartArea);
            _chart.Dock = DockStyle.Fill;

            var netSeries = new Series("Net")
            {
                ChartType = SeriesChartType.Line,
                XValueType = ChartValueType.DateTime,
                YValueType = ChartValueType.Double,
            };
            var tareSeries = new Series("Tare")
            {
                ChartType = SeriesChartType.Line,
                XValueType = ChartValueType.DateTime,
                YValueType = ChartValueType.Double,
            };

            _chart.Series.Add(netSeries);
            _chart.Series.Add(tareSeries);

            _timer.Interval = 1000;
            _timer.Tick += Timer_Tick;

            Controls.Add(_chart);
            Controls.Add(controlsPanel);

            Load += WeightTrendForm_Load;

            ResumeLayout(false);
        }

        private void WeightTrendForm_Load(object? sender, EventArgs e)
        {
            _autoUpdateCheckBox.Checked = true;
            RefreshChart();
            _timer.Enabled = _autoUpdateCheckBox.Checked;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_autoUpdateCheckBox.Checked)
            {
                RefreshChart();
            }
        }

        private void IntervalComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            RefreshChart();
        }

        private void AutoUpdateCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            _timer.Enabled = _autoUpdateCheckBox.Checked;
            if (_timer.Enabled)
            {
                RefreshChart();
            }
        }

        private TimeSpan GetSelectedWindow()
        {
            return _intervalComboBox.SelectedItem?.ToString() switch
            {
                "5 минут" => TimeSpan.FromMinutes(5),
                "10 минут" => TimeSpan.FromMinutes(10),
                "30 минут" => TimeSpan.FromMinutes(30),
                "60 минут" => TimeSpan.FromMinutes(60),
                _ => TimeSpan.FromMinutes(10),
            };
        }

        private void RefreshChart()
        {
            var window = GetSelectedWindow();
            var samples = _historyManager.GetSamples(_scale.Id, window).OrderBy(s => s.TimestampUtc).ToList();

            var netSeries = _chart.Series["Net"];
            var tareSeries = _chart.Series["Tare"];
            netSeries.Points.Clear();
            tareSeries.Points.Clear();

            foreach (var sample in samples)
            {
                var time = sample.TimestampUtc.ToLocalTime();
                netSeries.Points.AddXY(time, sample.NetGrams / 1000.0);
                tareSeries.Points.AddXY(time, sample.TareGrams / 1000.0);
            }
        }
    }
}
