using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Acorn.App.Platform.MacOS;

/// <summary>Minimal Objective-C runtime interop. Each overload is an exact objc_msgSend prototype (required on arm64).</summary>
[SupportedOSPlatform("macos")]
internal static partial class ObjC
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(LibObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetClass(string name);

    [LibraryImport(LibObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint Selector(string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, nint arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial void Send(nint receiver, nint selector, nint arg1, nint arg2, nint arg3, nint arg4);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendUInt(nint receiver, nint selector, nuint arg);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nuint SendReturningUInt(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_allocateClassPair", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint AllocateClassPair(nint superclass, string name, nuint extraBytes);

    [LibraryImport(LibObjC, EntryPoint = "objc_registerClassPair")]
    public static partial void RegisterClassPair(nint cls);

    [LibraryImport(LibObjC, EntryPoint = "class_addMethod", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AddMethod(nint cls, nint selector, nint implementation, string types);

    /// <summary>Creates an NSString (autoreleased) from a .NET string. Only for non-secret constants.</summary>
    public static nint NSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(GetClass("NSString"), Selector("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }
}
