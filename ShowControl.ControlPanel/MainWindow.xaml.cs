using NAudio.Dsp;
using ShowControl.Devices.Audio;
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

using Point = ShowControl.Devices.Audio.Point;
using WindowsPoint = System.Windows.Point;

namespace ShowControl.ControlPanel
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly double Limit = 1;
        Point[] fftChartPoints;
        PointCollection points1;
        PointCollection points2;
        PointCollection activePoints;
        public MainWindow()
        {
            InitializeComponent();
            Host.Host.Main(new string[] { }, OnAudioFrameProcessed);
        }

        private void OnAudioFrameProcessed(object sender, AudioFrameProcessedEventArgs e)
        {
            if (fftChartPoints == null || fftChartPoints.Length != e.AudioSpectrum.Length)
            {
                fftChartPoints = new Point[e.AudioSpectrum.Length];
            }

            Array.Copy(e.AudioSpectrum, fftChartPoints, fftChartPoints.Length);
            
            Dispatcher.Invoke(() => {
                Bpm.Text = string.Format("BPM: {0}", e.Bpm);
                double previous = 0;
                WindowsPoint point1;
                WindowsPoint point2;

                if (points1 == null)
                {
                    points1 = new PointCollection(2 * fftChartPoints.Length);
                    points2 = new PointCollection(2 * fftChartPoints.Length);
                    activePoints = points1;
                }

                if (polyline.Points == points1)
                {
                    activePoints = points2;
                }
                else if (activePoints == points2)
                {
                    activePoints = points1;
                }

                activePoints.Clear();
                for (int i = 0; i < fftChartPoints.Length; i++)
                {
                    var X = fftChartPoints[i].X * polyline.Width;
                    var Y = (fftChartPoints[i].Y / -140.0d) * polyline.Height;

                    if (fftChartPoints[i].X <= Limit)
                    {
                        point1.X = previous;
                        point1.Y = Y;
                        
                        point2.X = X;
                        point2.Y = Y;

                        if (activePoints.Count < fftChartPoints.Length * 2 + 1)
                        {
                            activePoints.Add(point1);
                            activePoints.Add(point2);
                        }
                        else
                        {
                            activePoints[2 * i] = point1;
                            activePoints[2 * i + 1] = point2;
                        }

                        previous = X;
                    }
                    else if (fftChartPoints[i].X > Limit)
                    {
                        break;
                    }
                }
                polyline.Points = activePoints;
            });
        }
    }
}
