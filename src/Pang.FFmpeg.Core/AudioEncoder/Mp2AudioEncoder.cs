using System;
using System.ComponentModel;
using System.IO;
using FFmpeg.AutoGen;
using Pang.FFmpeg.Core.Helpers;

namespace Pang.FFmpeg.Core.AudioEncoder
{
    public sealed unsafe class Mp2AudioEncoder : IDisposable
    {
        private AVCodecContext* pCodecContext = null;
        private AVCodec* pCodec;

        private AVFrame* pFrame;
        private AVPacket* pPacket;

        public string FileName { get; }
        public long BitRate { get; }
        public AVSampleFormat DestinationSampleFormat { get; }

        public FileStream Fs { get; }

        public string FilePath
        {
            get => Directory.GetCurrentDirectory() + $"/{FileName}.mp2";
        }

        public int Error { get; set; }

        public Mp2AudioEncoder(string fileName, long bitRate = 64000, AVSampleFormat destinationSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16)
        {
            FileName = string.IsNullOrEmpty(fileName) ? Guid.NewGuid().ToString() : fileName;
            BitRate = bitRate;
            DestinationSampleFormat = destinationSampleFormat;

            // 找到MP2编码器
            pCodec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_MP2);
            if (pCodec is null) throw new InvalidOperationException(@"Codec not found");

            // 初始化编码器上下文
            pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);
            if (pCodecContext is null) throw new InvalidOperationException(@"Could not allocate audio codec context.");

            // 设置比特率
            pCodecContext->bit_rate = BitRate;
            // 设置采样格式
            pCodecContext->sample_fmt = DestinationSampleFormat;

            // 检查采样格式是否支持
            if (!AudioEncoder.CheckSampleFormat(pCodec, pCodecContext->sample_fmt))
            {
                throw new InvalidOperationException($"Encoder does not support sample format: {ffmpeg.av_get_sample_fmt_name(pCodecContext->sample_fmt)}");
            }

            // 设置采样率, 通道布局, 通道数
            pCodecContext->sample_rate = AudioEncoder.SelectSampleRate(pCodec);
            pCodecContext->channel_layout = AudioEncoder.SelectChannelLayout(pCodec);
            pCodecContext->channels = ffmpeg.av_get_channel_layout_nb_channels(pCodecContext->channel_layout);

            Error = ffmpeg.avcodec_open2(pCodecContext, pCodec, null)
                .ThrowExceptionIfError(@"Could not open codec.");

            // 追加数据文件
            Fs = new FileStream(FilePath, FileMode.Append);

            pPacket = ffmpeg.av_packet_alloc();
            if (pPacket is null) throw new InvalidOperationException(@"Could not allocate audio packet.");

            pFrame = ffmpeg.av_frame_alloc();
            if (pFrame is null) throw new InvalidOperationException(@"Could not allocate audio frame");

            ffmpeg.av_frame_get_buffer(pFrame, 0).ThrowExceptionIfError(@"Could not allocate audio data buffers.");
        }

        public void Encode()
        {
            var t = 0;
            var tincr = 2 * Math.PI * 440.0 / pCodecContext->sample_rate;
            for (var i = 0; i < 200; i++)
            {
                ffmpeg.av_frame_make_writable(pFrame).ThrowExceptionIfError();

                var samples = (ulong*)pFrame->data[0];

                for (var j = 0; j < pCodecContext->frame_size; j++)
                {
                    samples[2 * j] = (ulong)(Math.Sin(t) * 10000);

                    for (var k = 0; k < pCodecContext->channels; k++)
                    {
                        samples[2 * j + k] = samples[2 * j];
                    }

                    t += (int)tincr;
                }
            }
        }

        public void Dispose()
        {
            var frame = pFrame;
            ffmpeg.av_frame_free(&frame);

            var packet = pPacket;
            ffmpeg.av_packet_free(&packet);

            var codecContext = pCodecContext;
            ffmpeg.avcodec_free_context(&codecContext);
        }
    }
}