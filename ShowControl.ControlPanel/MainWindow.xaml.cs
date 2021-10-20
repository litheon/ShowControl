using NAudio.Dsp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ShowControl.ControlPanel
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        int fftCount = 0;
        int sampleAverageWindowSize = 5;
        Dictionary<double, List<double>> decibels = new Dictionary<double, List<double>>();
        HashSet<double> seen = new HashSet<double>();
        public MainWindow()
        {
            InitializeComponent();
            Host.Host.Main(new string[] { }, FftCallback);
        }

        private void FftCallback(object sender, Complex[] fftPoints)
        {
            double width = 48000.0d / fftPoints.Length;
            double Hz = 0;
            var points = fftPoints.Take(20000 / Convert.ToInt32(width));
            var seen = new HashSet<double>();
            foreach (var point in points)
            {
                if (point.Y > 0 && Hz > 0)
                {
                    if (decibels.ContainsKey(Hz))
                    {
                        decibels[Hz].Insert(0, Math.Sqrt(Math.Pow(point.Y, 2) + Math.Pow(point.X, 2)));
                        if (decibels[Hz].Count > sampleAverageWindowSize)
                        {
                            decibels[Hz].RemoveAt(decibels[Hz].Count - 1);
                        }
                    }
                    else
                    {
                        decibels[Hz] = new List<double>(sampleAverageWindowSize);
                        decibels[Hz].Add(Math.Sqrt(Math.Pow(point.Y, 2) + Math.Pow(point.X, 2)));
                    }
                    seen.Add(Hz);
                }
                Hz += width;
            }
            foreach (var key in decibels.Keys.ToArray())
            {
                if (!seen.Contains(key))
                {
                    decibels[key].Insert(0, 1e-7);
                    decibels[key].RemoveAt(decibels[key].Count - 1);
                }
            }
            seen.Clear();
            foreach (var kvPair in decibels.OrderBy(db => db.Key).ToArray())
            {
                var dB = 20*Math.Log10(Math.Abs(kvPair.Value.Average()));
                var dbScaled = Convert.ToInt32(Math.Round(dB / -100.0d * 1000.0d));
            }

            fftCount++;
            if (fftCount == 10)
            {
                polyline.Dispatcher.Invoke(() => {
                    polyline.Points.Clear();

                    foreach (var kvPair in decibels.OrderBy(db => db.Key).ToArray())
                    {
                        var dB = 20*Math.Log10(Math.Abs(kvPair.Value.Average()));
                        var dbScaled = Convert.ToInt32(Math.Round(dB / -100.0d * 1000.0d));
                        polyline.Points.Add(new Point(kvPair.Key / 24000.0d * 2000.0d, dbScaled));
                    }
                });
                fftCount = 0;
                StringBuilder sb = new StringBuilder();
                foreach (var kvPair in decibels.OrderBy(db => db.Key).ToArray())
                {
                    var dB = 20*Math.Log10(Math.Abs(kvPair.Value.Average()));
                    var dbScaled = Convert.ToInt32(Math.Round(dB / -100.0d * 10000.0d));
                    sb.AppendLine(string.Format("{0} Hz: {1} dB", kvPair.Key, dB));
                }
                FrequencyOutput.Dispatcher.Invoke(() =>
                {
                    FrequencyOutput.Text = sb.ToString();
                });
            }
        }
    }
}
