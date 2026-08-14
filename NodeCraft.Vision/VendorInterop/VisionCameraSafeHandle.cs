using System;
using Microsoft.Win32.SafeHandles;

namespace NodeCraft.Vision.VendorInterop
{
    internal sealed class VisionCameraSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly Func<IntPtr, int> _destroy;

        internal VisionCameraSafeHandle(IntPtr handle)
            : this(handle, ImvNativeMethods.IMV_DestroyHandle)
        {
        }

        internal VisionCameraSafeHandle(IntPtr handle, Func<IntPtr, int> destroy)
            : base(ownsHandle: true)
        {
            _destroy = destroy ?? throw new ArgumentNullException(nameof(destroy));
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
        {
            try
            {
                return _destroy(handle) == (int)ImvError.Ok;
            }
            catch
            {
                return false;
            }
        }
    }
}
