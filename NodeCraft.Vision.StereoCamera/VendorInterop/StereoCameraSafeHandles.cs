using System;
using Microsoft.Win32.SafeHandles;

namespace NodeCraft.Vision.StereoCamera.VendorInterop
{
    internal abstract class StereoCameraSafeHandleBase : SafeHandleZeroOrMinusOneIsInvalid
    {
        protected StereoCameraSafeHandleBase()
            : base(ownsHandle: true)
        {
        }

        internal StereoCameraSafeHandleBase(IntPtr handle)
            : this()
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.scReleaseHandle(handle);
        }
    }

    internal sealed class StereoCameraCameraHandle : StereoCameraSafeHandleBase
    {
        internal StereoCameraCameraHandle(IntPtr handle)
            : base(handle)
        {
        }
    }

    internal sealed class StereoCameraFrameHandle : StereoCameraSafeHandleBase
    {
        internal StereoCameraFrameHandle(IntPtr handle)
            : base(handle)
        {
        }
    }

    internal sealed class StereoCameraImageHandle : StereoCameraSafeHandleBase
    {
        internal StereoCameraImageHandle(IntPtr handle)
            : base(handle)
        {
        }
    }

    internal sealed class StereoCameraCalibrationManagerHandle : StereoCameraSafeHandleBase
    {
        internal StereoCameraCalibrationManagerHandle(IntPtr handle)
            : base(handle)
        {
        }
    }
}
