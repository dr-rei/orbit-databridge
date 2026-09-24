using System.Runtime.InteropServices;
using System.Text;

namespace Dbms.App.Updates;

internal static class PackageIdentity
{
    private const int AppModelErrorNoPackage = 15700;

    public static bool IsPackaged
    {
        get
        {
            var packagePathLength = 0;
            return GetCurrentPackagePath(ref packagePathLength, null) != AppModelErrorNoPackage;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetCurrentPackagePath(ref int packagePathLength, StringBuilder? packagePath);
}
