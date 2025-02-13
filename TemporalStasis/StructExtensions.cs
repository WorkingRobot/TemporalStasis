using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TemporalStasis;

public static class StructExtensions {
    public static T ReadStruct<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(this Stream stream) where T : struct {
        var buffer = stream.ReadBytes(Unsafe.SizeOf<T>());
        return buffer.ReadStruct<T>();
    }
    
    public static T ReadStruct<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(this byte[] data) where T : struct {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        var @struct = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        handle.Free();

        return @struct;
    }

    public static async Task<T> ReadStructAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(this Stream stream, CancellationToken token = default) where T : struct {
        var buffer = await stream.ReadBytesAsync(Unsafe.SizeOf<T>(), token);
        return buffer.ReadStruct<T>();
    }

    public static void WriteStruct<T>(this Stream stream, T @struct) where T : struct {
        var size = Unsafe.SizeOf<T>();
        var buffer = new byte[size];
        buffer.WriteStruct(@struct);
        stream.WriteBytes(buffer);
    }
    
    public static void WriteStruct<T>(this byte[] buffer, T @struct) where T : struct {
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        Marshal.StructureToPtr(@struct, handle.AddrOfPinnedObject(), false);
        handle.Free();
    }

    public static async Task WriteStructAsync<T>(this Stream stream, T @struct, CancellationToken token = default) where T : struct {
        var size = Unsafe.SizeOf<T>();
        var buffer = new byte[size];
        buffer.WriteStruct(@struct);
        await stream.WriteBytesAsync(buffer);
    }

    public static byte[] ReadBytes(this Stream stream, int size) {
        var buffer = new byte[size];
        var read = 0;
        while (read < size) read += stream.Read(buffer, read, size - read);
        return buffer;
    }

    public static async Task<byte[]> ReadBytesAsync(this Stream stream, int size, CancellationToken token = default) {
        var buffer = new byte[size];
        var read = 0;
        while (read < size && !token.IsCancellationRequested) read += await stream.ReadAsync(buffer.AsMemory(read, size - read), token);
        return buffer;
    }

    public static void WriteBytes(this Stream stream, byte[] buffer) {
        stream.Write(buffer, 0, buffer.Length);
    }

    public static async Task WriteBytesAsync(this Stream stream, byte[] buffer, CancellationToken token = default) {
        await stream.WriteAsync(buffer.AsMemory(0, buffer.Length), token);
    }
}
