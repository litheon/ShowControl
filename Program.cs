using System;
using System.Threading;
using ShowControl.Host;

namespace ShowControl
{
    class Program
    {
        static async System.Threading.Tasks.Task Main(string[] args)
        {
            await Host.Host.Main(args);
        }
    }
}
