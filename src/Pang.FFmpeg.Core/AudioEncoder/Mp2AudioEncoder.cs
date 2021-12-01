using System;
using System.ComponentModel;
using FFmpeg.AutoGen;

namespace Pang.FFmpeg.Core.AudioEncoder
{
    public sealed unsafe class Mp2AudioEncoder
    {
        private AVCodecContext* pCodecContext = null;
        private AVCodec* pCodec;

        private AVFrame* pFrame;
        private AVPacket* pPacket;

        public string FileName { get; set; }

        public Mp2AudioEncoder(string fileName)
        {
            FileName = fileName;

            pCodec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_MP2);
            if (pCodec is null) throw new InvalidOperationException(@"Codec not found");

            pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);
            if (pCodecContext is null) throw new InvalidOperationException(@"Could not allocate audio codec context.");
        }
    }
}