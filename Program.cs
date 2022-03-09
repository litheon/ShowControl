using System;
using System.Threading;
using ShowControl.Host;

namespace ShowControl
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Host.Host.Main(args).Wait();
        }
    }
}
