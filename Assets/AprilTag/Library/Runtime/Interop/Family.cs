using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AprilTag.Interop {

public enum TagFamily
{
    TagStandard41h12,
    Tag36h11
}

public sealed class Family : SafeHandleZeroOrMinusOneIsInvalid
{
    #region SafeHandle implementation

    Family() : base(true) {}

    protected override bool ReleaseHandle()
    {
        if (_currentFamily == TagFamily.Tag36h11)
        {
            _DestroyTag36h11(handle);
        }
        else
        {
            _DestroyTagStandard41h12(handle);
        }
        return true;
    }

    #endregion

    #region Public methods

    public static Family CreateTagStandard41h12()
      => _CreateTagStandard41h12();

    public static Family CreateTag36h11()
    {
        var family = _CreateTag36h11();
        family._currentFamily = TagFamily.Tag36h11;
        return family;
    }

    #endregion

    #region Private fields

    private TagFamily _currentFamily = TagFamily.TagStandard41h12;

    #endregion

    #region Unmanaged interface

    [DllImport(Config.DllName, EntryPoint = "tagStandard41h12_create")]
    private static extern Family _CreateTagStandard41h12();

    [DllImport(Config.DllName, EntryPoint = "tagStandard41h12_destroy")]
    private static extern void _DestroyTagStandard41h12(IntPtr ptr);

    [DllImport(Config.DllName, EntryPoint = "tag36h11_create")]
    private static extern Family _CreateTag36h11();

    [DllImport(Config.DllName, EntryPoint = "tag36h11_destroy")]
    private static extern void _DestroyTag36h11(IntPtr ptr);

    #endregion
}

} // namespace AprilTag.Interop
