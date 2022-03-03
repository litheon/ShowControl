using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ShowControl.Devices.Audio
{
    public class AutoCorrelation
    {
        public static double GetAverage(double[] data)
        {
            int len = data.Length;
            double sum = 0;

            for (int i = 0; i < len; i++)
                sum += data[i];

            return sum / len;
        }

        public static double GetVariance(double[] data)
        {
            int len = data.Length;
            double avg = GetAverage(data);
            double sum = 0;

            for (int i = 0; i < data.Length; i++)
            {
                sum += Math.Pow((data[i] - avg), 2);
            }
            return sum / len;
        }
        public static double GetStdev(double[] data)
        {
            return Math.Sqrt(GetVariance(data));
        }

        public static double GetCorrelation(double[] x, double[] y)
        {
            if (x.Length != y.Length)
                throw new Exception("Length of sources is different");
            double avgX = GetAverage(x);
            double stdevX = GetStdev(x);
            double avgY = GetAverage(y);
            double stdevY = GetStdev(y);
            double covXY = 0;
            double pearson = 0;
            int len = x.Length;
            for (int i = 0; i < len; i++)
            {
                covXY += (x[i] - avgX) * (y[i] - avgY);
            }
            covXY /= len;
            pearson = covXY / (stdevX * stdevY);
            return pearson;
        }

        public static double[] GetAutoCorrelationOfSeries(double[] x)
        {
            int half = (int)x.Length / 2;
            double[] autoCorrelation = new double[half];
            double[] a = new double[half];
            double[] b = new double[half];
            for (int i = 0; i < half; i++)
            {
                a[i] = x[i];
                b[i] = x[i + i];
                autoCorrelation[i] = GetCorrelation(a, b);
            }
            return autoCorrelation;
        }

        public static async Task<double[]> GetAutoCorrelationOfSeriesAsync(double[] x)
        {
            int half = (int)x.Length / 2;
            Task[] tasks = new Task[half];
            double[] autoCorrelation = new double[half];
            double[] a = new double[half];
            double[] b = new double[half];
            for (int i = 0; i < half; i++)
            {

                a[i] = x[i];
                b[i] = x[i + i];

                var task = Task.Factory.StartNew(() => DoSomeWork(a, b), TaskCreationOptions.LongRunning);
                tasks[i] = task;
                autoCorrelation[i] = task.Result;
            }
            await Task.WhenAll(tasks);
            return autoCorrelation;
        }

        public static double DoSomeWork(double[] a, double[] b)
        {
            return GetCorrelation(a, b);
        }
    }
}
