using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Node.Algorithm.Interop
{
    internal sealed class WaybillRuntimeScope : IDisposable
    {
        private static readonly object Gate = new object();
        private static string _activeLibraryDirectory;
        private static IntPtr _directoryCookie;
        private static IntPtr _nativeLibraryHandle;
        private static int _referenceCount;
        private int _disposed;

        private WaybillRuntimeScope()
        {
        }

        internal static int ReferenceCount
        {
            get
            {
                lock (Gate)
                {
                    return _referenceCount;
                }
            }
        }

        internal static WaybillRuntimeScope Acquire(string pluginAssemblyPath)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("The Node.Algorithm plugin requires Windows.");
            }

            if (!Environment.Is64BitProcess)
            {
                throw new PlatformNotSupportedException("The Node.Algorithm plugin requires a 64-bit process.");
            }

            if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
            {
                throw new ArgumentException("A plugin assembly path is required.", nameof(pluginAssemblyPath));
            }

            var pluginRoot = Path.GetDirectoryName(Path.GetFullPath(pluginAssemblyPath));
            var libraryDirectory = Path.GetFullPath(Path.Combine(pluginRoot ?? string.Empty, "lib"));
            if (!Directory.Exists(libraryDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Waybill native library directory was not found: {libraryDirectory}");
            }

            lock (Gate)
            {
                if (_referenceCount > 0)
                {
                    if (!string.Equals(
                        _activeLibraryDirectory,
                        libraryDirectory,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Waybill native runtime is already active from '{_activeLibraryDirectory}'.");
                    }

                    _referenceCount++;
                    return new WaybillRuntimeScope();
                }

                var cookie = AddDllDirectory(libraryDirectory);
                if (cookie == IntPtr.Zero)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        $"AddDllDirectory failed for '{libraryDirectory}'.");
                }

                IntPtr nativeLibraryHandle;
                try
                {
                    nativeLibraryHandle = NativeLibrary.Load(
                        Path.Combine(libraryDirectory, WaybillNativeMethods.LibraryName));
                }
                catch
                {
                    RemoveDllDirectory(cookie);
                    throw;
                }

                _activeLibraryDirectory = libraryDirectory;
                _directoryCookie = cookie;
                _nativeLibraryHandle = nativeLibraryHandle;
                _referenceCount = 1;
                return new WaybillRuntimeScope();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            lock (Gate)
            {
                if (_referenceCount <= 0)
                {
                    return;
                }

                _referenceCount--;
                if (_referenceCount != 0)
                {
                    return;
                }

                var cookie = _directoryCookie;
                var nativeLibraryHandle = _nativeLibraryHandle;
                _directoryCookie = IntPtr.Zero;
                _nativeLibraryHandle = IntPtr.Zero;
                _activeLibraryDirectory = null;

                Exception removeException = null;
                try
                {
                    if (nativeLibraryHandle != IntPtr.Zero)
                    {
                        NativeLibrary.Free(nativeLibraryHandle);
                    }
                }
                finally
                {
                    if (cookie != IntPtr.Zero && !RemoveDllDirectory(cookie))
                    {
                        removeException = new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "RemoveDllDirectory failed for the waybill native runtime.");
                    }
                }

                if (removeException != null)
                {
                    throw removeException;
                }
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AddDllDirectory(string newDirectory);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveDllDirectory(IntPtr cookie);
    }
}
