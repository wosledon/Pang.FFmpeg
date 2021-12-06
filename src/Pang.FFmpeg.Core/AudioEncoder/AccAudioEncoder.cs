using System;
using System.ComponentModel;
using System.IO;
using FFmpeg.AutoGen;
using Pang.FFmpeg.Core.Helpers;

namespace Pang.FFmpeg.Core.AudioEncoder
{
    public sealed unsafe class AccAudioEncoder : IDisposable
    {
        private AVCodecContext* pCodecContext = null;
        private AVCodec* pCodec;
        private SwrContext* pSwrContext;

        private AVFrame* pFrame;
        private AVPacket* pPacket;

        public string FileName { get; }
        public int SampleRate { get; }
        public long BitRate { get; }
        public int Channels { get; }
        public AVSampleFormat DestinationSampleFormat { get; }
        public AVSampleFormat SourceSampleFormat { get; }

        public FileStream Fs { get; }

        private AudioEncoder audioEncoder;

        public string FilePath
        {
            get => Directory.GetCurrentDirectory() + $"/{FileName}.aac";
        }

        public int Error { get; set; }

        ulong sourceChannelLayout = ffmpeg.AV_CH_LAYOUT_MONO;
        ulong destinationChannelLayout = ffmpeg.AV_CH_LAYOUT_STEREO;

        public AccAudioEncoder(string fileName, int sampleRate = 8000, long bitRate = 16, int channels = 1, AVSampleFormat sourceSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16, AVSampleFormat destinationSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_FLTP)
        {
            audioEncoder = new AudioEncoder();

            FileName = string.IsNullOrEmpty(fileName) ? Guid.NewGuid().ToString() : fileName;
            SampleRate = sampleRate;
            BitRate = bitRate;
            Channels = channels;
            SourceSampleFormat = sourceSampleFormat;
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

            pSwrContext = ffmpeg.swr_alloc();

            pSwrContext = ffmpeg.swr_alloc_set_opts(pSwrContext, (long)pCodecContext->channel_layout, pCodecContext->sample_fmt,
                pCodecContext->sample_rate,
                (long)pCodecContext->channel_layout, SourceSampleFormat, SampleRate, 0, null);

            ffmpeg.swr_init(pSwrContext).ThrowExceptionIfError(@"PCM压缩为ACC格式");

            // 追加数据文件
            Fs = new FileStream(FilePath, FileMode.Append);

            pPacket = ffmpeg.av_packet_alloc();
            if (pPacket is null) throw new InvalidOperationException(@"Could not allocate audio packet.");

            pFrame = ffmpeg.av_frame_alloc();
            if (pFrame is null) throw new InvalidOperationException(@"Could not allocate audio frame");

            ffmpeg.av_frame_get_buffer(pFrame, 0).ThrowExceptionIfError(@"Could not allocate audio data buffers.");
        }

        public void Encode(byte[] input, out byte[] output)
        {
            output = Array.Empty<byte>();

            byte** sourceData;
            byte** destinationData;

            #region 官网示例

            //var t = 0;
            //var tincr = 2 * Math.PI * 440.0 / pCodecContext->sample_rate;
            //for (var i = 0; i < 200; i++)
            //{
            //    ffmpeg.av_frame_make_writable(pFrame).ThrowExceptionIfError();

            // var samples = (ulong*)pFrame->data[0];

            // for (var j = 0; j < pCodecContext->frame_size; j++) { samples[2 * j] =
            // (ulong)(Math.Sin(t) * 10000);

            // for (var k = 0; k < pCodecContext->channels; k++) { samples[2 * j + k] = samples[2 *
            // j]; }

            //        t += (int)tincr;
            //    }
            //}

            #endregion 官网示例

            pFrame->channels = pCodecContext->channels;
            pFrame->format = (int)pCodecContext->sample_fmt;
            pFrame->nb_samples = pCodecContext->frame_size;

            int sourceLineSize;
            // 单声道
            int sourceChannelsCount = ffmpeg.av_get_channel_layout_nb_channels(sourceChannelLayout);

            // ffmpeg.av_samples_alloc_array_and_samples(&sourceData, &sourceLineSize,
            //     sourceChannelsCount, SourceSampleFormat, 0);



            // // 转码
            // ffmpeg.swr_convert(pSwrContext, 0, pCodecContext->frame_size, 0, pFrame->nb_samples);

            // 从文件中读取原始数据
            int size = ffmpeg.av_samples_get_buffer_size(null, pCodecContext->channels, pCodecContext->frame_size,
                pCodecContext->sample_fmt, 1);

            // 分配空间
            ffmpeg.av_frame_get_buffer(pFrame, 0).ThrowExceptionIfError("Could not allocate frame.");

            byte* outBuffer = (byte*)ffmpeg.av_malloc((ulong)size);
            ffmpeg.avcodec_fill_audio_frame(pFrame, pCodecContext->channels, pCodecContext->sample_fmt, outBuffer, size, 1);

            audioEncoder.Encode(pCodecContext, pFrame, pPacket, Fs);
        }

        public void Dispose()
        {
            var frame = pFrame;
            ffmpeg.av_frame_free(&frame);

            var packet = pPacket;
            ffmpeg.av_packet_free(&packet);

            var codecContext = pCodecContext;
            ffmpeg.avcodec_free_context(&codecContext);

            ffmpeg.av_free(pSwrContext);
        }
    }
}