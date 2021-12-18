using System;
using System.Collections.Generic;
using FFmpeg.AutoGen;

namespace Pang.FFmpeg.Core.Audio
{
    public sealed unsafe class AudioEncoder
    {
        private AVCodec* Codec;
        private AVCodecContext* CodecContext;
        private AVFormatContext* FormatContext;

        public uint FrameSize { get; private set; }

        public List<byte> FrameCache { get; private set; } = new List<byte>();

        public AVSampleFormat InputSampleFormat { get; private set; }
        public AVSampleFormat OutputSampleFormat { get; private set; }

        public int BitRate { get; private set; } = 64000;

        public int Channels { get; private set; }

        public uint SampleRate { get; private set; }
    }
}