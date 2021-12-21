using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Pang.FFmpeg.Core.Extensions
{
    public static unsafe class ArrayExtension
    {
        /// <summary>
        /// 数组转为指针
        /// </summary>
        /// <param name="buffer"> </param>
        /// <returns> </returns>
        public static byte* ToArrayPointer(this byte[] buffer)
        {
            byte* res = (byte*)Marshal.AllocHGlobal(buffer.Length);

            //Marshal.AllocHGlobal(buffer.Length);

            for (int i = 0; i < buffer.Length; i++)
            {
                res[i] = buffer[i];
            }

            return res;
        }

        /// <summary>
        /// 判断是否为空或者长度为0
        /// </summary>
        /// <param name="data"> </param>
        /// <returns> </returns>
        public static bool IsNullOrEmpty(this IEnumerable<byte[]>? data)
        {
            if (data is null || !data.Any())
            {
                return true;
            }

            return false;
        }
    }
}