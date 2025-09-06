using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowControl.Graphics
{
    static class VkExt
    {
        public static void ThrowOnError(this Result r)
        {
            if (r != Result.Success)
                throw new InvalidOperationException($"Vulkan error: {r}");
        }
    }
}
