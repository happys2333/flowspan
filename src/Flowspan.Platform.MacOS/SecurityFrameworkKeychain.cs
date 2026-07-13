using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Flowspan.Security;
using Microsoft.Win32.SafeHandles;

namespace Flowspan.Platform.MacOS;

public sealed class MacOSKeychainException : IOException
{
    internal MacOSKeychainException(int status, string operation)
        : base(CreateMessage(status, operation))
    {
        Status = status;
        RecoveryAction = status switch
        {
            MacOSSecurityStatus.NotAvailable =>
                "Unlock or restore the current user's login Keychain and retry.",
            MacOSSecurityStatus.InteractionNotAllowed =>
                "Unlock the login Keychain and retry.",
            MacOSSecurityStatus.AuthFailed =>
                "Allow Flowspan to access its Keychain item and retry.",
            MacOSSecurityStatus.UserCanceled =>
                "Retry and approve the Keychain access request.",
            MacOSSecurityStatus.MissingEntitlement =>
                "Install a correctly signed Flowspan build with Keychain access.",
            _ => "Inspect the macOS Keychain and Flowspan diagnostics, then retry.",
        };
    }

    public string RecoveryAction { get; }

    public int Status { get; }

    private static string CreateMessage(int status, string operation) =>
        $"macOS Keychain {operation} failed with Security status {status}.";
}

public sealed class SecurityFrameworkKeychain : IMacOSKeychain
{
    public bool DeleteGenericPassword(string service, string account)
    {
        EnsureMacOS();
        using MacOSKeychainQuery query = MacOSKeychainQuery.Create(service, account);
        int status = MacOSSecurityNative.SecItemDelete(query.Handle);
        return status switch
        {
            MacOSSecurityStatus.Success => true,
            MacOSSecurityStatus.ItemNotFound => false,
            _ => throw new MacOSKeychainException(status, "delete"),
        };
    }

    public byte[]? LoadGenericPassword(string service, string account)
    {
        EnsureMacOS();
        using MacOSKeychainQuery query = MacOSKeychainQuery.Create(service, account);
        query.SetBorrowed(
            MacOSSecuritySymbols.ReturnData,
            MacOSSecuritySymbols.BooleanTrue);
        query.SetBorrowed(
            MacOSSecuritySymbols.MatchLimit,
            MacOSSecuritySymbols.MatchLimitOne);
        int status = MacOSSecurityNative.SecItemCopyMatching(
            query.Handle,
            out IntPtr result);
        if (status == MacOSSecurityStatus.ItemNotFound)
        {
            return null;
        }

        if (status != MacOSSecurityStatus.Success)
        {
            throw new MacOSKeychainException(status, "load");
        }

        if (result == IntPtr.Zero)
        {
            throw new InvalidDataException(
                "The macOS Keychain returned no protected value after success.");
        }

        using var data = new MacOSCoreFoundationHandle(result);
        if (MacOSCoreFoundationNative.CFGetTypeID(data.DangerousGetHandle())
            != MacOSCoreFoundationNative.CFDataGetTypeID())
        {
            throw new InvalidDataException(
                "The macOS Keychain returned a non-data protected value.");
        }

        nint length = MacOSCoreFoundationNative.CFDataGetLength(
            data.DangerousGetHandle());
        if (length is < 1 or > TrustStorePayloadCodec.MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                "The macOS Keychain protected value has an invalid length.");
        }

        byte[] value = new byte[checked((int)length)];
        Marshal.Copy(
            MacOSCoreFoundationNative.CFDataGetBytePtr(data.DangerousGetHandle()),
            value,
            0,
            value.Length);
        return value;
    }

    public bool TryAddGenericPassword(
        string service,
        string account,
        ReadOnlyMemory<byte> value)
    {
        EnsureMacOS();
        ValidateValue(value);

        using MacOSKeychainQuery query = MacOSKeychainQuery.Create(service, account);
        query.SetOwnedData(MacOSSecuritySymbols.ValueData, value.Span);
        query.SetBorrowed(
            MacOSSecuritySymbols.Accessible,
            MacOSSecuritySymbols.AccessibleWhenUnlockedThisDeviceOnly);
        int status = MacOSSecurityNative.SecItemAdd(query.Handle, IntPtr.Zero);
        return status switch
        {
            MacOSSecurityStatus.Success => true,
            MacOSSecurityStatus.DuplicateItem => false,
            _ => throw new MacOSKeychainException(status, "create"),
        };
    }

    public bool UpdateGenericPassword(
        string service,
        string account,
        ReadOnlyMemory<byte> value)
    {
        EnsureMacOS();
        ValidateValue(value);
        using MacOSKeychainQuery query = MacOSKeychainQuery.Create(service, account);
        using MacOSKeychainQuery update = MacOSKeychainQuery.CreateValueUpdate(value);
        int status = MacOSSecurityNative.SecItemUpdate(query.Handle, update.Handle);
        return status switch
        {
            MacOSSecurityStatus.Success => true,
            MacOSSecurityStatus.ItemNotFound => false,
            _ => throw new MacOSKeychainException(status, "update"),
        };
    }

    private static void ValidateValue(ReadOnlyMemory<byte> value)
    {
        if (value.IsEmpty || value.Length > TrustStorePayloadCodec.MaximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"A Keychain protected value must contain 1 to {TrustStorePayloadCodec.MaximumPayloadBytes} bytes.");
        }
    }

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "Security.framework protected storage is available only on macOS.");
        }
    }
}

internal sealed class MacOSKeychainQuery : IDisposable
{
    private readonly List<MacOSCoreFoundationHandle> ownedValues = [];
    private readonly MacOSCoreFoundationHandle query;

    private MacOSKeychainQuery(MacOSCoreFoundationHandle query) =>
        this.query = query;

    public IntPtr Handle => query.DangerousGetHandle();

    public static MacOSKeychainQuery Create(string service, string account)
    {
        ValidateIdentifier(service, nameof(service));
        ValidateIdentifier(account, nameof(account));
        IntPtr dictionary = MacOSCoreFoundationNative.CFDictionaryCreateMutable(
            IntPtr.Zero,
            0,
            IntPtr.Zero,
            IntPtr.Zero);
        if (dictionary == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "CoreFoundation could not allocate a Keychain query.");
        }

        var query = new MacOSKeychainQuery(
            new MacOSCoreFoundationHandle(dictionary));
        try
        {
            query.SetBorrowed(
                MacOSSecuritySymbols.Class,
                MacOSSecuritySymbols.ClassGenericPassword);
            query.SetOwnedString(MacOSSecuritySymbols.AttributeService, service);
            query.SetOwnedString(MacOSSecuritySymbols.AttributeAccount, account);
            return query;
        }
        catch
        {
            query.Dispose();
            throw;
        }
    }

    public static MacOSKeychainQuery CreateValueUpdate(ReadOnlyMemory<byte> value)
    {
        MacOSKeychainQuery query = CreateMutable();
        try
        {
            query.SetOwnedData(MacOSSecuritySymbols.ValueData, value.Span);
            return query;
        }
        catch
        {
            query.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        query.Dispose();
        foreach (MacOSCoreFoundationHandle value in ownedValues)
        {
            value.Dispose();
        }
    }

    public void SetBorrowed(IntPtr key, IntPtr value) =>
        MacOSCoreFoundationNative.CFDictionarySetValue(Handle, key, value);

    public void SetOwnedData(IntPtr key, ReadOnlySpan<byte> value)
    {
        byte[] valueCopy = value.ToArray();
        IntPtr data;
        try
        {
            data = MacOSCoreFoundationNative.CFDataCreate(
                IntPtr.Zero,
                valueCopy,
                valueCopy.Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(valueCopy);
        }

        AddOwnedValue(key, data, "data");
    }

    private void SetOwnedString(IntPtr key, string value)
    {
        IntPtr text = MacOSCoreFoundationNative.CFStringCreateWithCString(
            IntPtr.Zero,
            value,
            MacOSCoreFoundationNative.Utf8Encoding);
        AddOwnedValue(key, text, "string");
    }

    private void AddOwnedValue(IntPtr key, IntPtr value, string kind)
    {
        if (value == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"CoreFoundation could not allocate a Keychain {kind} value.");
        }

        var handle = new MacOSCoreFoundationHandle(value);
        ownedValues.Add(handle);
        SetBorrowed(key, handle.DangerousGetHandle());
    }

    private static MacOSKeychainQuery CreateMutable()
    {
        IntPtr dictionary = MacOSCoreFoundationNative.CFDictionaryCreateMutable(
            IntPtr.Zero,
            0,
            IntPtr.Zero,
            IntPtr.Zero);
        if (dictionary == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "CoreFoundation could not allocate a Keychain query.");
        }

        return new MacOSKeychainQuery(new MacOSCoreFoundationHandle(dictionary));
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 200 || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A Keychain identifier must contain 1 to 200 non-control characters.",
                parameterName);
        }
    }
}

internal sealed class MacOSCoreFoundationHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public MacOSCoreFoundationHandle(IntPtr handle)
        : base(ownsHandle: true) => SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        MacOSCoreFoundationNative.CFRelease(handle);
        return true;
    }
}

internal static class MacOSSecurityStatus
{
    public const int AuthFailed = -25293;
    public const int DuplicateItem = -25299;
    public const int InteractionNotAllowed = -25308;
    public const int ItemNotFound = -25300;
    public const int MissingEntitlement = -34018;
    public const int NotAvailable = -25291;
    public const int Success = 0;
    public const int UserCanceled = -128;
}

internal static class MacOSSecuritySymbols
{
    private static readonly IntPtr CoreFoundationHandle = NativeLibrary.Load(
        MacOSCoreFoundationNative.Library);
    private static readonly IntPtr SecurityHandle = NativeLibrary.Load(
        MacOSSecurityNative.Library);

    public static IntPtr Accessible { get; } = ReadSecurityReference("kSecAttrAccessible");

    public static IntPtr AccessibleWhenUnlockedThisDeviceOnly { get; } =
        ReadSecurityReference("kSecAttrAccessibleWhenUnlockedThisDeviceOnly");

    public static IntPtr AttributeAccount { get; } =
        ReadSecurityReference("kSecAttrAccount");

    public static IntPtr AttributeService { get; } =
        ReadSecurityReference("kSecAttrService");

    public static IntPtr BooleanTrue { get; } =
        ReadCoreFoundationReference("kCFBooleanTrue");

    public static IntPtr Class { get; } = ReadSecurityReference("kSecClass");

    public static IntPtr ClassGenericPassword { get; } =
        ReadSecurityReference("kSecClassGenericPassword");

    public static IntPtr MatchLimit { get; } =
        ReadSecurityReference("kSecMatchLimit");

    public static IntPtr MatchLimitOne { get; } =
        ReadSecurityReference("kSecMatchLimitOne");

    public static IntPtr ReturnData { get; } =
        ReadSecurityReference("kSecReturnData");

    public static IntPtr ValueData { get; } =
        ReadSecurityReference("kSecValueData");

    private static IntPtr ReadCoreFoundationReference(string symbol) =>
        Marshal.ReadIntPtr(NativeLibrary.GetExport(CoreFoundationHandle, symbol));

    private static IntPtr ReadSecurityReference(string symbol) =>
        Marshal.ReadIntPtr(NativeLibrary.GetExport(SecurityHandle, symbol));
}

internal static partial class MacOSSecurityNative
{
    public const string Library =
        "/System/Library/Frameworks/Security.framework/Security";

    [LibraryImport(Library)]
    public static partial int SecItemAdd(IntPtr attributes, IntPtr result);

    [LibraryImport(Library)]
    public static partial int SecItemCopyMatching(
        IntPtr query,
        out IntPtr result);

    [LibraryImport(Library)]
    public static partial int SecItemDelete(IntPtr query);

    [LibraryImport(Library)]
    public static partial int SecItemUpdate(
        IntPtr query,
        IntPtr attributesToUpdate);
}

internal static partial class MacOSCoreFoundationNative
{
    public const string Library =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    public const uint Utf8Encoding = 0x08000100;

    [LibraryImport(Library)]
    public static partial IntPtr CFDataCreate(
        IntPtr allocator,
        byte[] bytes,
        nint length);

    [LibraryImport(Library)]
    public static partial IntPtr CFDataGetBytePtr(IntPtr data);

    [LibraryImport(Library)]
    public static partial nint CFDataGetLength(IntPtr data);

    [LibraryImport(Library)]
    public static partial nuint CFDataGetTypeID();

    [LibraryImport(Library)]
    public static partial IntPtr CFDictionaryCreateMutable(
        IntPtr allocator,
        nint capacity,
        IntPtr keyCallbacks,
        IntPtr valueCallbacks);

    [LibraryImport(Library)]
    public static partial void CFDictionarySetValue(
        IntPtr dictionary,
        IntPtr key,
        IntPtr value);

    [LibraryImport(Library)]
    public static partial nuint CFGetTypeID(IntPtr value);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr CFStringCreateWithCString(
        IntPtr allocator,
        string value,
        uint encoding);

    [LibraryImport(Library)]
    public static partial void CFRelease(IntPtr value);
}
