﻿using Stride.Core;
using Stride.Graphics;
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using VL.Lib.Basics.Imaging;
using VL.Lib.Collections;

namespace VL.Stride.Graphics
{
    public interface IStrideGraphicsDataProvider
    {
        int SizeInBytes { get; }
        int ElementSizeInBytes { get; }
        int RowSizeInBytes { get; }
        int SliceSizeInBytes { get; }
        PinnedGraphicsData Pin();
    }

    public interface IMemoryPinner : IDisposable
    {
        public IntPtr Pin();
    }

    public class ReadonlyMemoryPinner<T> : IMemoryPinner where T : struct
    {
        public ReadOnlyMemory<T> Memory;
        MemoryHandle memoryHandle;

        public unsafe IntPtr Pin()
        {
            memoryHandle = Memory.Pin();
            return new IntPtr(memoryHandle.Pointer);
        }

        public void Dispose()
        {
            memoryHandle.Dispose();
        }
    }

    public class ImagePinner : IMemoryPinner
    {
        public IImage Image;

        IImageData imageData;
        MemoryHandle memoryHandle;

        public unsafe IntPtr Pin()
        {
            imageData = Image.GetData();
            memoryHandle = imageData.Bytes.Pin();
            return new IntPtr(memoryHandle.Pointer);
        }

        public void Dispose()
        {
            memoryHandle.Dispose();
            imageData.Dispose();
        }
    }

    public class NonePinner : IMemoryPinner
    {
        public IntPtr Pin()
        {
            return IntPtr.Zero;
        }

        public void Dispose()
        {
        }
    }

    public struct PinnedGraphicsData : IDisposable
    {
        public static readonly PinnedGraphicsData None = new PinnedGraphicsData(new NonePinner());

        public readonly IntPtr Pointer;
        readonly IDisposable disposable;

        public PinnedGraphicsData(IMemoryPinner pinner)
        {
            Pointer = pinner.Pin();
            disposable = pinner;
        }

        public void Dispose()
        {
            disposable.Dispose();
        }
    }

    public class MemoryDataProvider : IStrideGraphicsDataProvider
    {
        public IMemoryPinner Pinner = new NonePinner();

        public void SetMemoryData<T>(ReadOnlyMemory<T> memory, int offsetInBytes = 0, int sizeInBytes = 0, int elementSizeInBytes = 0, int rowSizeInBytes = 0, int sliceSizeInBytes = 0) where T : struct
        {
            var pnr = Pinner as ReadonlyMemoryPinner<T>;
            pnr ??= new ReadonlyMemoryPinner<T>();

            pnr.Memory = memory;
            Pinner = pnr;

            OffsetInBytes = offsetInBytes;
            SizeInBytes = sizeInBytes > 0 ? sizeInBytes : memory.Length;
            ElementSizeInBytes = elementSizeInBytes > 0 ? elementSizeInBytes : Utilities.SizeOf<T>();
            RowSizeInBytes = rowSizeInBytes;
            SliceSizeInBytes = sliceSizeInBytes;
        }

        public void SetImageData(IImage image, int offsetInBytes = 0, int sizeInBytes = 0, int elementSizeInBytes = 0, int rowSizeInBytes = 0, int sliceSizeInBytes = 0)
        {
            var pnr = Pinner as ImagePinner;
            pnr ??= new ImagePinner();

            pnr.Image = image;
            Pinner = pnr;

            OffsetInBytes = offsetInBytes;

            SizeInBytes = sizeInBytes > 0 ? sizeInBytes : image.Info.ImageSize;
            ElementSizeInBytes = elementSizeInBytes > 0 ? elementSizeInBytes : image.Info.Format.GetPixelSize();
            RowSizeInBytes = rowSizeInBytes > 0 ? rowSizeInBytes : image.Info.ScanSize;
            SliceSizeInBytes = sliceSizeInBytes > 0 ? sliceSizeInBytes : RowSizeInBytes * image.Info.Height;
        }

        public int OffsetInBytes { get; set; }

        public int SizeInBytes { get; set; }

        public int ElementSizeInBytes { get; set; }

        public int RowSizeInBytes { get; set; }

        public int SliceSizeInBytes { get; set; }

        public PinnedGraphicsData Pin()
        {
            return new PinnedGraphicsData(Pinner);
        }
    }

    public class VLImagePinner : IDisposable
    {
        IImageData imageData;
        MemoryHandle imageDataHandle;
        IntPtr pointer;

        public unsafe VLImagePinner(IImage image)
        {
            imageData = image.GetData();
            imageDataHandle = imageData.Bytes.Pin();
            pointer = (IntPtr)imageDataHandle.Pointer;
        }

        public IntPtr Pointer
        {
            get => pointer;
        }

        public int SizeInBytes
        {
            get => imageData.Bytes.Length;
        }

        public int ScanSize
        {
            get => imageData.ScanSize;
        }

        public void Dispose()
        {
            imageDataHandle.Dispose();
            imageData.Dispose();
        }
    }

    public class GCPinner : IDisposable
    {
        GCHandle pinnedObject;

        public GCPinner(object obj)
        {
            pinnedObject = GCHandle.Alloc(obj, GCHandleType.Pinned);
        }

        public IntPtr Pointer
        {
            get => pinnedObject.AddrOfPinnedObject();
        }

        public void Dispose()
        {
            pinnedObject.Free();
        }
    }

    public static class ResourceDataHelpers
    {
        public static void PinSpread<T>(Spread<T> input, out IntPtr pointer, out int sizeInBytes, out int byteStride, out GCPinner pinner) where T : struct
        {
            pointer = IntPtr.Zero;
            sizeInBytes = 0;
            byteStride = 0;
            pinner = null;

            var count = input.Count;
            if (count > 0)
            {
                byteStride = Utilities.SizeOf<T>();
                sizeInBytes = byteStride * count;

                pinner = new GCPinner(input);
                pointer = pinner.Pointer;
            }
        }

        public static void PinArray<T>(T[] input, out IntPtr pointer, out int sizeInBytes, out int byteStride, out GCPinner pinner) where T : struct
        {
            pointer = IntPtr.Zero;
            sizeInBytes = 0;
            byteStride = 0;
            pinner = null;

            var count = input.Length;
            if (count > 0)
            {
                input.AsMemory();
                byteStride = Utilities.SizeOf<T>();
                sizeInBytes = byteStride * count;

                pinner = new GCPinner(input);
                pointer = pinner.Pointer;
            }
        }

        public static void PinImage(IImage input, out IntPtr pointer, out int sizeInBytes, out int bytePerRow, out int bytesPerPixel, out VLImagePinner pinner)
        {
            pointer = IntPtr.Zero;
            sizeInBytes = 0;
            bytePerRow = 0;
            bytesPerPixel = 0;
            pinner = null;

            if (input != null)
            {
                pinner = new VLImagePinner(input);
                sizeInBytes = pinner.SizeInBytes;
                bytePerRow = pinner.ScanSize;
                bytesPerPixel = input.Info.PixelSize;
                pointer = pinner.Pointer;
            }
        }
    }
}
