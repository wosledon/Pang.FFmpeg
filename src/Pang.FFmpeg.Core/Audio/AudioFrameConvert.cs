using FFmpeg.AutoGen;
using Pang.FFmpeg.Core.Helpers;

namespace Pang.FFmpeg.Core.Audio
{
    public sealed unsafe class AudioFrameConvert
    {
        private readonly AVCodecContext* _codecContext;
        private readonly AVSampleFormat _outputSampleFormat;
        private readonly AVSampleFormat _inputSampleFormat;

        // 转码器
        private SwrContext* SwrContext;

        // 声道
        private long OutCHLayout { get; }

        public AudioFrameConvert(AVCodecContext* codecContext,
            int outSampleRate, long inChannelLayout, AVSampleFormat inputSampleFormat, int inSampleRate)
        {
            _codecContext = codecContext;
            _inputSampleFormat = inputSampleFormat;
            // 初始化转码器
            SwrContext = ffmpeg.swr_alloc();

            SwrContext = ffmpeg.swr_alloc_set_opts(SwrContext,
                _codecContext->channels, _codecContext->sample_fmt,
                _codecContext->sample_rate,
                inChannelLayout, inputSampleFormat,
                inSampleRate, 0, null);

            ffmpeg.swr_init(SwrContext)
                .ThrowExceptionIfError(@"Failed to initialize the swr context.");
        }

        public AVFrame? Convert(byte[] input)
        {
            return null;
        }
    }
}