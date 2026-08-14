using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace NodeCraft.Vision.Runtime
{
    internal sealed class NativeRuntimeScope : IDisposable
    {
        private const string GenIcamEnvironmentVariable = "MV_GENICAM_64";
        private static readonly object Gate = new object();
        private static string _activeLibraryDirectory;
        private static string _previousGenIcamValue;
        private static IntPtr _directoryCookie;
        private static int _referenceCount;
        private int _disposed;

        private NativeRuntimeScope()
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

        internal static NativeRuntimeScope Acquire(string pluginAssemblyPath)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("The Vision plugin requires Windows.");
            }

            if (!Environment.Is64BitProcess)
            {
                throw new PlatformNotSupportedException("The Vision plugin requires a 64-bit process.");
            }

            if (string.IsNullOrWhiteSpace(pluginAssemblyPath))
            {
                throw new ArgumentException("A plugin assembly path is required.", nameof(pluginAssemblyPath));
            }

            var pluginRoot = Path.GetDirectoryName(Path.GetFullPath(pluginAssemblyPath));
            var libraryDirectory = Path.Combine(pluginRoot ?? string.Empty, "lib");
            if (!Directory.Exists(libraryDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Vision native library directory was not found: {libraryDirectory}");
            }

            lock (Gate)
            {
                if (_referenceCount > 0)
                {
                    if (!string.Equals(_activeLibraryDirectory, libraryDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Vision native runtime is already active from '{_activeLibraryDirectory}'.");
                    }

                    _referenceCount++;
                    return new NativeRuntimeScope();
                }

                var cookie = AddDllDirectory(libraryDirectory);
                if (cookie == IntPtr.Zero)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        $"AddDllDirectory failed for '{libraryDirectory}'.");
                }

                var previousValue = Environment.GetEnvironmentVariable(
                    GenIcamEnvironmentVariable,
                    EnvironmentVariableTarget.Process);
                try
                {
                    Environment.SetEnvironmentVariable(
                        GenIcamEnvironmentVariable,
                        libraryDirectory,
                        EnvironmentVariableTarget.Process);
                }
                catch
                {
                    RemoveDllDirectory(cookie);
                    throw;
                }

                _activeLibraryDirectory = libraryDirectory;
                _previousGenIcamValue = previousValue;
                _directoryCookie = cookie;
                _referenceCount = 1;
                return new NativeRuntimeScope();
            }
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
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
                var previousValue = _previousGenIcamValue;
                _directoryCookie = IntPtr.Zero;
                _activeLibraryDirectory = null;
                _previousGenIcamValue = null;

                Exception removeException = null;
                try
                {
                    if (cookie != IntPtr.Zero && !RemoveDllDirectory(cookie))
                    {
                        removeException = new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "RemoveDllDirectory failed for the Vision native runtime.");
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable(
                        GenIcamEnvironmentVariable,
                        previousValue,
                        EnvironmentVariableTarget.Process);
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
