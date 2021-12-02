using FFmpeg.AutoGen;
using Microsoft.Extensions.DependencyInjection;
using Pang.FFmpeg.Core.Helpers;


#pragma warning disable
namespace Pang.FFmpeg.Core.Extensions
{
    public static class FFmpegExtension
    {
        public static IServiceCollection AddFFmpeg(this IServiceCollection services)
        {
            ffmpeg.av_register_all();
            ffmpeg.avcodec_register_all();
            ffmpeg.avformat_network_init();

            FFmpegBinariesHelper.RegisterFFmpegBinaries();
            return services;
        }
    }
}