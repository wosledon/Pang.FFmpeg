﻿using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Pang.FFmpeg.Core.Helpers
{
    internal static class FFmpegHelper
    {
        public static unsafe string av_strerror(int error)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
            return message;
        }

        public static int ThrowExceptionIfError(this int error)
        {
            if (error < 0)
                throw new ApplicationException(av_strerror(error));
            return error;
        }

        public static int ThrowExceptionIfError(this int error, string message)
        {
            if (error < 0)
                throw new ApplicationException($"[Pang.FFmpeg] - {error} - {av_strerror(error)} - [{message}]");
            return error;
        }
    }
}