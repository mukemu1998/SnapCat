using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Bitmap = System.Drawing.Bitmap;
using Graphics = System.Drawing.Graphics;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using ImageFormat = System.Drawing.Imaging.ImageFormat;
using CopyPixelOperation = System.Drawing.CopyPixelOperation;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Resource = SharpDX.DXGI.Resource;

namespace SnapCat.App.Services;

public sealed class ScreenCaptureService
{
    public string CaptureToTempFile(Int32Rect region)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new InvalidOperationException("截图区域无效。");
        }

        try
        {
            return CaptureWithGdi(region);
        }
        catch
        {
            return CaptureWithDesktopDuplication(region);
        }
    }

    private static string CaptureWithGdi(Int32Rect region)
    {
        var outputPath = CreateOutputPath();

        using var bitmap = new Bitmap(region.Width, region.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(region.X, region.Y, 0, 0, new DrawingSize(region.Width, region.Height), CopyPixelOperation.SourceCopy);
        bitmap.Save(outputPath, ImageFormat.Png);

        return outputPath;
    }

    private static string CaptureWithDesktopDuplication(Int32Rect region)
    {
        using var factory = new Factory1();
        var monitorHandle = NativeMethods.GetMonitorHandleForRegion(region);

        for (var adapterIndex = 0; adapterIndex < factory.Adapters1.Length; adapterIndex++)
        {
            using var adapter = factory.GetAdapter1(adapterIndex);

            for (var outputIndex = 0; outputIndex < adapter.Outputs.Length; outputIndex++)
            {
                using var output = adapter.GetOutput(outputIndex);
                var description = output.Description;

                if (description.MonitorHandle != monitorHandle)
                {
                    continue;
                }

                using var output1 = output.QueryInterface<Output1>();
                using var device = new Device(adapter, DeviceCreationFlags.BgraSupport);
                using var duplication = output1.DuplicateOutput(device);

                return CaptureFromOutputDuplication(duplication, device, region, description.DesktopBounds);
            }
        }

        throw new InvalidOperationException("未找到对应的显示器输出。");
    }

    private static string CaptureFromOutputDuplication(
        OutputDuplication duplication,
        Device device,
        Int32Rect region,
        RawRectangle outputBounds)
    {
        var result = duplication.TryAcquireNextFrame(500, out _, out Resource screenResource);
        result.CheckError();

        try
        {
            using var screenTexture = screenResource.QueryInterface<Texture2D>();
            var textureDescription = screenTexture.Description;

            using var stagingTexture = new Texture2D(device, new Texture2DDescription
            {
                CpuAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None,
                Format = textureDescription.Format,
                Width = textureDescription.Width,
                Height = textureDescription.Height,
                OptionFlags = ResourceOptionFlags.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging
            });

            device.ImmediateContext.CopyResource(screenTexture, stagingTexture);
            var dataBox = device.ImmediateContext.MapSubresource(stagingTexture, 0, MapMode.Read, MapFlags.None);

            try
            {
                var relativeLeft = Math.Max(0, region.X - outputBounds.Left);
                var relativeTop = Math.Max(0, region.Y - outputBounds.Top);
                var cropWidth = Math.Min(region.Width, textureDescription.Width - relativeLeft);
                var cropHeight = Math.Min(region.Height, textureDescription.Height - relativeTop);

                if (cropWidth <= 0 || cropHeight <= 0)
                {
                    throw new InvalidOperationException("截图裁切区域超出显示器范围。");
                }

                using var croppedBitmap = new Bitmap(cropWidth, cropHeight, PixelFormat.Format32bppArgb);
                var bitmapData = croppedBitmap.LockBits(
                    new DrawingRectangle(0, 0, cropWidth, cropHeight),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);

                try
                {
                    var rowBuffer = new byte[cropWidth * 4];

                    for (var row = 0; row < cropHeight; row++)
                    {
                        var sourcePtr = dataBox.DataPointer + ((relativeTop + row) * dataBox.RowPitch) + (relativeLeft * 4);
                        Marshal.Copy(sourcePtr, rowBuffer, 0, rowBuffer.Length);

                        for (var offset = 3; offset < rowBuffer.Length; offset += 4)
                        {
                            rowBuffer[offset] = 255;
                        }

                        var destinationPtr = bitmapData.Scan0 + (row * bitmapData.Stride);
                        Marshal.Copy(rowBuffer, 0, destinationPtr, rowBuffer.Length);
                    }
                }
                finally
                {
                    croppedBitmap.UnlockBits(bitmapData);
                }

                var outputPath = CreateOutputPath();
                croppedBitmap.Save(outputPath, ImageFormat.Png);
                return outputPath;
            }
            finally
            {
                device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
            }
        }
        finally
        {
            screenResource.Dispose();
            duplication.ReleaseFrame();
        }
    }

    private static string CreateOutputPath()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "SnapCat");
        Directory.CreateDirectory(tempDirectory);
        return Path.Combine(tempDirectory, $"capture-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");
    }

    private static class NativeMethods
    {
        private const uint MonitorDefaultToNearest = 2;

        public static IntPtr GetMonitorHandleForRegion(Int32Rect region)
        {
            var point = new NativePoint(region.X + (region.Width / 2), region.Y + (region.Height / 2));
            return MonitorFromPoint(point, MonitorDefaultToNearest);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NativePoint
        {
            public NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; }

            public int Y { get; }
        }
    }
}
