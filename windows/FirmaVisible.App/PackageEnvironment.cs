using System.Runtime.InteropServices;
using System.Text;

namespace FirmaVisible.App;

public static class PackageEnvironment
{
    public static bool IsPackaged
    {
        get
        {
            uint length=0;
            int result=GetCurrentPackageFullName(ref length,null);
            return result is 0 or 122; // ERROR_INSUFFICIENT_BUFFER means an identity exists.
        }
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength,StringBuilder? packageFullName);
}
