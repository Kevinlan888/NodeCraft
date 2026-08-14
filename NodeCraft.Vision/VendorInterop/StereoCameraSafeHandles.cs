using System;
using Microsoft.Win32.SafeHandles;

namespace NodeCraft.Vision.StereoCamera.VendorInterop
{
    internal abstract class StereoCameraSafeHandleBase : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly Func<IntPtr, bool> _releaseHandle;

        protected StereoCameraSafeHandleBase()
            : base(ownsHandle: true)
        {
            _releaseHandle = NativeMethods.scReleaseHandle;
        }

        internal StereoCameraSafeHandleBase(IntPtr handle)
            : this()
        {
            SetHandle(handle);
        }

        internal StereoCameraSafeHandleBase(IntPtr handle, Func<IntPtr, bool> releaseHandle)
            : base(ownsHandle: true)
        {
            _releaseHandle = releaseHandle ?? throw new ArgumentNullException(nameof(releaseHandle));
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
        {
            return _releaseHandle(handle);
        }
    }

    internal sealed class StereoCameraCameraHandle : StereoCameraSafeHandleBase
    {
        internal StereoCameraCameraHandle(IntPtr handle)
            : base(handle)
        {
        }

        internal StereoCameraCameraHandle(IntPtr handle, Func<IntPtr, bool> releaseHandle)
            : base(handle, releaseHandle)
        {
        }
    }

    internal sealed class StereoCameraFrameHandle : StereoCameraSafeHandleBase
    {
        internal StereoCameraFrameHandle(IntPtr handle)
            : base(handle)
        {
        }

        internal StereoCameraFrameHandle(IntPtr handle, Func<IntPtr, bool> releaseHandle)
            : base(handle, releaseHandle)
        {
        }
    }

    internal sealed class StereoCameraImageHandle : StereoCameraSafeHandleBase
    {
        internal StereoCameraImageHandle(IntPtr handle)
            : base(handle)
        {
        }

        internal StereoCameraImageHandle(IntPtr handle, Func<IntPtr, bool> releaseHandle)
            : base(handle, releaseHandle)
        {
        }
    }

    internal sealed class StereoCameraCalibrationManagerHandle : StereoCameraSafeHandleBase
    {
        internal StereoCameraCalibrationManagerHandle(IntPtr handle)
            : base(handle)
        {
        }

        internal StereoCameraCalibrationManagerHandle(IntPtr handle, Func<IntPtr, bool> releaseHandle)
            : base(handle, releaseHandle)
        {
        }
    }
}
