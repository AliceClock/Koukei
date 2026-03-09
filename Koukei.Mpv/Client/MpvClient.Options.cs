//using Koukei.Mpv.Interop;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace Koukei.Mpv.Client;

//public sealed partial class MpvClient
//{
//    public async Task SetVideoOutputDriverAsync(VideoOutputDriver driver)
//    {
//        var mpvError = MpvError.Success;
//        var driverStr = driver switch
//        {
//            VideoOutputDriver.Gpu => "gpu",
//            VideoOutputDriver.GpuNext => "gpu-next",
//            VideoOutputDriver.Direct3D => "direct3d",
//            VideoOutputDriver.Sdl => "sdl",
//            _ => throw new NotImplementedException(),
//        };
//        await Task.Run(() => mpvError = MpvNative.MpvSetOptionString(Handle, "vo", driverStr));
//        ThrowMpvException("MPV | set 'vo' failed", mpvError);
//    }

//    public async Task SetGpuApiAsync(GpuApi api)
//    {
//        var mpvError = MpvError.Success;
//        var apiStr = api switch
//        {
//            GpuApi.Auto => "auto",
//            GpuApi.OpenGL => "opengl",
//            GpuApi.Vulkan => "vulkan",
//            GpuApi.D3d11 => "d3d11",
//            _ => throw new NotImplementedException(),
//        };
//        await Task.Run(() => mpvError = MpvNative.MpvSetOptionString(Handle, "gpu-api", apiStr));
//        ThrowMpvException("MPV | set 'gpu-api' failed", mpvError);
//    }

//    public async Task SetGpuContextAsync(GpuContext context)
//    {
//        var mpvError = MpvError.Success;
//        var contextStr = context switch
//        {
//            GpuContext.Auto => "auto",
//            GpuContext.Win => "win",
//            GpuContext.WinVk => "winvk",
//            GpuContext.Angle => "angle",
//            GpuContext.DxInterop => "dxinterop",
//            GpuContext.D3d11 => "d3d11",
//            _ => throw new NotImplementedException(),
//        };
//        await Task.Run(() => mpvError = MpvNative.MpvSetOptionString(Handle, "gpu-context", contextStr));
//        ThrowMpvException("MPV | set 'gpu-context' failed", mpvError);
//    }

//    public async Task SetHardwareDecoderAsync(HardwareDecoder type)
//    {
//        var mpvError = MpvError.Success;
//        var decoderStr = type switch
//        {
//            HardwareDecoder.No => "no",
//            HardwareDecoder.Auto => "auto",
//            HardwareDecoder.AutoUnsafe => "auto-unsafe",
//            HardwareDecoder.D3d11Va => "d3d11va",
//            HardwareDecoder.D3d11VaCopy => "d3d11va-copy",
//            HardwareDecoder.Nvdec => "nvdec",
//            HardwareDecoder.NvdecCopy => "nvdec-copy",
//            HardwareDecoder.Vulkan => "vulkan",
//            HardwareDecoder.VulkanCopy => "vulkan-copy",
//            HardwareDecoder.Dxva2 => "dxva2",
//            HardwareDecoder.Dxva2Copy => "dxva2-copy",
//            HardwareDecoder.Vaapi => "vaapi",
//            HardwareDecoder.VaapiCopy => "vaapi-copy",
//            HardwareDecoder.Cuda => "cuda",
//            HardwareDecoder.CudaCopy => "cuda-copy",
//            _ => throw new NotImplementedException(),
//        };
//        await Task.Run(() => mpvError = MpvNative.MpvSetOptionString(Handle, "hwdec", decoderStr));
//        ThrowMpvException("MPV | set 'hwdec' failed", mpvError);
//    }
//}