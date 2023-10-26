using DevExpress.XtraPrinting.Native;
using ScottPlot;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Drawing;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ScottPlot.Control;
using DevExpress.Data.Linq.Helpers;
using System.Collections;

namespace TimeDelaySignals
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private GenerateSignals _gS;
        private ModulationType _modulationType;
        private readonly BackgroundWorker _bgResearch;
        private Dictionary<ModulationType, List<PointD>> _snrResearch;
        private Tuple<ModulationType, int, int, int, int, int> signalParam;
        private Dictionary<string,object> modParam;
        private HashSet<object> researchParam;
        public MainWindow()
        {
            InitializeComponent();
            _bgResearch = (BackgroundWorker)FindResource("BackgroundWorkerConductResearch");
        }
        #region #Radio Button#
        private void RbIsAsk_Checked(object sender, RoutedEventArgs e)
        {
            _modulationType = ModulationType.ASK;
            GbAskParams.IsEnabled = true;
            GbFskParams.IsEnabled = false;
        }

        private void RbIsFsk_Checked(object sender, RoutedEventArgs e)
        {
            _modulationType = ModulationType.FSK;
            GbAskParams.IsEnabled = false;
            GbFskParams.IsEnabled = true;
        }
        private void RbIsPsk_Checked(object sender, RoutedEventArgs e)
        {
            _modulationType = ModulationType.PSK;
            GbAskParams.IsEnabled = false;
            GbFskParams.IsEnabled = false;
        }
        #endregion
        #region #Buttons Methods#
        private void GenerateSignals_Click(object sender, RoutedEventArgs e)
        {
            var countBits = BitsCount.Value;
            var baudRate = BaudRate.Value;
            var carrierFreq = double.Round((double)CarrierFreq.Value, 1) * 1000;
            var samplingFreq = double.Round((double)SamplingFreq.Value, 1) * 1000;
            var delay = Delay.Value;
            var snr = SNR.Value;

            var a0 = A0.Value;
            var a1 = A1.Value;
            var f1 = F1.Value;
            var f0 = F0.Value;

            int MaxIndex = 0;
            //Параметры сигнала для модуляции
            signalParam = new
                (_modulationType, (int)countBits, (int)baudRate, (int)carrierFreq, (int)samplingFreq, (int)delay);

            //Параметры модуляции
            modParam = new Dictionary<string, object>
            {
                ["a0"] = a0,
                ["a1"] = a1,
                ["f0"] = f0,
                ["f1"] = f1
            };

            //Рассчет модуляции, затем корреляции и нахождение max
            _gS = new GenerateSignals(signalParam);
            _gS.ModulateSignals(modParam);
            _gS.MakeNoise((double)snr);
            _gS.CrossCorrelate(out MaxIndex);
            //_gS.CalculateCorrelation(out MaxIndex);

            var yMax = _gS.desiredSignal.Max(p => double.Abs(p.Y));

            ChartReferenceSignal.Plot.Clear();
            ChartReferenceSignal.Plot.AddSignalXY(_gS.desiredSignal.Select(p => p.X).ToArray(),
                _gS.desiredSignal.Select(p => p.Y).ToArray(), color: Color.Blue);
            ChartReferenceSignal.Refresh();

            ChartResearchedSignal.Plot.Clear();
            ChartResearchedSignal.Plot.AddSignalXY(_gS.researchedSignal.Select(p => p.X).ToArray(),
                _gS.researchedSignal.Select(p => p.Y).ToArray(), color: Color.Blue);
            ChartResearchedSignal.Plot.AddVerticalLine((double)delay/1000d, Color.Green);
            ChartResearchedSignal.Plot.AddVerticalLine((double)delay / 1000d + _gS.Tsample, Color.Green);
            ChartResearchedSignal.Refresh();

            ChartCorellation.Plot.Clear();
            ChartCorellation.Plot.AddSignalXY(_gS.correlation.Select(p => p.X).ToArray(),
                _gS.correlation.Select(p => p.Y).ToArray(), color: System.Drawing.Color.Blue);
            ChartCorellation.Plot.AddVerticalLine((double)delay/1000d, Color.Green);
            ChartCorellation.Plot.AddVerticalLine(MaxIndex * _gS.dt, Color.Red);
            ChartCorellation.Refresh();

            CorrelationDelay.Text = ((MaxIndex * _gS.dt)).ToString();
        }

        private void ConductResearch_Click(object sender, RoutedEventArgs e)
        {
            ConductResearch.Visibility = Visibility.Collapsed;
            ProgressResearch.Visibility = Visibility.Visible;

            _snrResearch = new Dictionary<ModulationType, List<PointD>>
            {
                [ModulationType.ASK] = new(),
                [ModulationType.FSK] = new(),
                [ModulationType.PSK] = new(),
            };
            var a0 = A0.Value;
            var a1 = A1.Value;
            var f1 = F1.Value;
            var f0 = F0.Value;

            var countBits = BitsCount.Value;
            var baudRate = BaudRate.Value;
            var carrierFreq = double.Round((double)CarrierFreq.Value, 1) * 1000;
            var samplingFreq = double.Round((double)SamplingFreq.Value, 1) * 1000;
            var delay = Delay.Value;

            //Параметры сигнала для модуляции
            signalParam = new
                (_modulationType, (int)countBits, (int)baudRate, (int)carrierFreq, (int)samplingFreq, (int)delay);

            //Параметры модуляции
            modParam = new Dictionary<string, object>
            {
                ["a0"] = a0,
                ["a1"] = a1,
                ["f0"] = f0,
                ["f1"] = f1
            };
            //Параметры исследования
            researchParam = new HashSet<object>
            { (int)MeanCount.Value, (int)UpBorder.Value, (int)DownBorder.Value, (double)Step.Value };

            ProgressResearch.Value = 0;
            ProgressResearch.Maximum = 3 * (int)researchParam.ElementAt(0) * ((int)researchParam.ElementAt(1) - (int)researchParam.ElementAt(2)) / ((double)researchParam.ElementAt(3) + 1);

            _bgResearch.RunWorkerAsync();
        }
        #endregion
        #region #BackgroundWorker Methods#
        private void OnDoWorkBackgroundWorkerConductResearch(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            try
            {
                var meanOrder = (int)researchParam.ElementAt(0);
                var snrFrom = (int)researchParam.ElementAt(2);
                var snrTo = (int)researchParam.ElementAt(1);
                var snrStep = (double)researchParam.ElementAt(3);

                var index = 0;

                Parallel.For(0, 3, type =>
                {
                    Parallel.For(0, (int)((snrTo - snrFrom) / snrStep + 1), n =>
                    {
                        var p = 0;
                        var snr = snrFrom + n * snrStep;
                        Parallel.For(0, meanOrder, i =>
                        {
                            int MaxIndex = 0;

                            var sge = new GenerateSignals(new((ModulationType)type, signalParam.Item2,
                                signalParam.Item3, signalParam.Item4, signalParam.Item5, signalParam.Item6));
                            sge.ModulateSignals(modParam);
                            sge.MakeNoise(snr);
                            sge.CrossCorrelate(out MaxIndex);

                            //Условие попадания найденного max в доверительный интервал
                            if (MaxIndex <= (double)sge.Delay / (double)1000 / sge.dt + sge.TBit / 2 &&
                                    MaxIndex >= (double)sge.Delay / (double)1000 / sge.dt - sge.TBit / 2)
                                p++;

                            _bgResearch.ReportProgress(++index);
                        });
                        _snrResearch[(ModulationType)type].Add(new PointD(snr, (double)p / meanOrder));
                    });
                    _snrResearch[(ModulationType)type] = _snrResearch[(ModulationType)type].OrderBy(p => p.X).ToList();
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show("Ошибка!", exception.Message);
            }
        }

        private void OnRunWorkerCompletedBackgroundWorkerConductResearch(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            ConductResearch.Visibility = Visibility.Visible;
            ProgressResearch.Visibility = Visibility.Collapsed;
            ChartResearch.Visibility = Visibility.Visible;

            ChartResearch.Plot.Clear();
            ChartResearch.Plot.AddSignalXY(
                  _snrResearch[ModulationType.ASK].Select(p => p.X).ToArray(),
                _snrResearch[ModulationType.ASK].Select(p => p.Y).ToArray(),
                Color.Red,
                "ASK"
            );
            ChartResearch.Plot.AddSignalXY(
                 _snrResearch[ModulationType.FSK].Select(p => p.X).ToArray(),
                _snrResearch[ModulationType.FSK].Select(p => p.Y).ToArray(),
                Color.Green,
                "FSK"
            );
            ChartResearch.Plot.AddSignalXY(
                 _snrResearch[ModulationType.PSK].Select(p => p.X).ToArray(),
                _snrResearch[ModulationType.PSK].Select(p => p.Y).ToArray(),
                Color.Blue,
                "PSK"
            );
            ChartResearch.Plot.Legend();
            ChartResearch.Plot.SetAxisLimits(xMin: (int)-20, xMax: (int)1, yMin: 0, yMax: 1);
            ChartResearch.Refresh();
        }

        private void OnProgressChangedBackgroundWorkerConductResearch(object sender, System.ComponentModel.ProgressChangedEventArgs e)
        {
            ProgressResearch.Value = e.ProgressPercentage;
        }
        #endregion
        private void OnLoadedMainWindow(object sender, RoutedEventArgs e)
        {
            RbIsAsk.IsChecked = true;

            //Настройка графиков.
            SetUpChart(ChartReferenceSignal, "Искомый сигнал", "Время, с", "Амплитуда");
            SetUpChart(ChartResearchedSignal, "Исследуемый сигнал", "Время, с", "Амплитуда");
            SetUpChart(ChartCorellation, "Взаимная корреляция сигналов", "Время, с", "Амплитуда");
            SetUpChart(ChartResearch, "Зависимость вероятности обнаружения сигнала от ОСШ", "Уровень шума, дБ", "Вероятность обнаружения");
        }
        private static void SetUpChart(IPlotControl chart, string title, string labelX, string labelY)
        {
            chart.Plot.Title(title);
            chart.Plot.XLabel(labelX);
            chart.Plot.YLabel(labelY);
            chart.Plot.XAxis.MajorGrid(enable: true, color: Color.FromArgb(50, Color.Black));
            chart.Plot.YAxis.MajorGrid(enable: true, color: Color.FromArgb(50, Color.Black));
            chart.Plot.XAxis.MinorGrid(enable: true, color: Color.FromArgb(30, Color.Black), lineStyle: LineStyle.Dot);
            chart.Plot.YAxis.MinorGrid(enable: true, color: Color.FromArgb(30, Color.Black), lineStyle: LineStyle.Dot);
            chart.Plot.Margins(x: 0.0, y: 0.8);
            chart.Plot.SetAxisLimits(xMin: 0);
            chart.Configuration.Quality = QualityMode.High;
            chart.Configuration.DpiStretch = false;
            chart.Refresh();
        }
    }
}
