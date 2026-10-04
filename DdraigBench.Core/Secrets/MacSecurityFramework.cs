// DdraigBench — macOS 钥匙串（Security.framework SecItem*）最小封装

using System.Runtime.InteropServices;

namespace DdraigBench.Core.Secrets;

/// <summary>
/// 对 Security.framework 通用密码条目（<c>kSecClassGenericPassword</c>）的最小封装：
/// 按 <c>service + account</c> 读 / 写 / 删，并构造所需的查询字典。
/// </summary>
/// <remarks>
/// ① 查询字典的键与类名常量**不硬编码字符串**，而是用 <c>dlsym</c> 读取框架导出的
/// <c>const CFStringRef</c> 变量——这些键的字面量（"class" / "genp" / "svce" / "v_Data" …）
/// 在任何公开头文件里都查不到，实查 <c>Security.framework.tbd</c> 确认导出了
/// <c>_kSecClass</c>、<c>_kSecClassGenericPassword</c>、<c>_kSecAttrService</c>、<c>_kSecAttrAccount</c>、
/// <c>_kSecAttrLabel</c>、<c>_kSecValueData</c>、<c>_kSecMatchLimitOne</c>、<c>_kSecReturnData</c>、
/// <c>_kSecUseAuthenticationUI(Allow|Fail)</c>，CoreFoundation 导出 <c>_kCFBooleanTrue</c>。
/// 这样既不必手写易错字面量，也不用为此引第三方包（§2.8）。
/// ② 只在 macOS 可用：调用前须 <see cref="EnsureSupportedPlatform"/>，绝不把
/// <c>DllNotFoundException</c> 泄给上层（做法同 <see cref="DpapiSecretProtector"/>）。
/// ③ 本类只做"状态码 + 数据搬运"，不解释业务：何时重建密钥、何时报错由 <see cref="MacKeychainKeyProvider"/> 决定。
/// </remarks>
internal static partial class MacSecurityFramework
{
    internal const string SecurityLibrary = "/System/Library/Frameworks/Security.framework/Security";
    internal const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // OSStatus（实查 SDK Security.framework/Headers/SecBase.h 的 errSec* 枚举值）
    internal const int StatusSuccess = 0;
    internal const int StatusUserCanceled = -128;
    internal const int StatusNotAvailable = -25291;
    internal const int StatusAuthFailed = -25293;
    internal const int StatusDuplicateItem = -25299;
    internal const int StatusItemNotFound = -25300;
    internal const int StatusInteractionNotAllowed = -25308;
    internal const int StatusMissingEntitlement = -34018;

    private const uint Utf8Encoding = 0x08000100; // kCFStringEncodingUTF8（实查 CFString.h:113）

    /// <summary>
    /// 字典的键/值回调结构体符号名。<c>CFDictionaryCreate</c> 的回调参数**不可传 NULL**——实测那样建出的
    /// 字典**不 retain 键与值**，随后释放 CFString/CFData 会让 SecItem 读到野指针并**永久卡死**（不是崩溃，
    /// 无任何报错，CI 里表现为测试挂住）。必须传 CoreFoundation 导出的
    /// <c>kCFTypeDictionaryKeyCallBacks</c> / <c>kCFTypeDictionaryValueCallBacks</c>（取结构体地址）。
    /// </summary>
    private const string KeyCallBacksSymbol = "kCFTypeDictionaryKeyCallBacks";

    private const string ValueCallBacksSymbol = "kCFTypeDictionaryValueCallBacks";

    /// <summary>平台守卫：非 macOS 直接抛，绝不触碰 P/Invoke。</summary>
    internal static void EnsureSupportedPlatform()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "macOS Keychain 仅在 macOS 可用（Windows 用 DPAPI，Linux/FreeBSD 用 freedesktop Secret Service）。");
        }
    }

    /// <summary>
    /// 状态码是否属于"密钥库不可用 / 未获授权"这一类——调用方据此回退明文保护器。
    /// 含用户拒绝（-128）、钥匙串不可用/未授权（-25291/-25293/-25308）与缺授权组（-34018）。
    /// </summary>
    internal static bool IsAuthorizationFailure(int status) => status
        is StatusUserCanceled or StatusNotAvailable or StatusAuthFailed
            or StatusInteractionNotAllowed or StatusMissingEntitlement;

    internal static string DescribeStatus(int status) => status switch
    {
        StatusUserCanceled => "用户拒绝了钥匙串访问授权",
        StatusNotAvailable => "系统没有可用的钥匙串",
        StatusAuthFailed => "钥匙串授权校验失败",
        StatusInteractionNotAllowed => "钥匙串已锁定且不允许弹出授权界面",
        StatusMissingEntitlement => "应用缺少 keychain-access-groups 授权",
        _ => $"OSStatus {status}",
    };

    /// <summary>读取条目数据。返回 <see cref="StatusSuccess"/> 且 <paramref name="data"/> 非 null 表示命中。</summary>
    internal static int TryCopyData(string service, string account, bool suppressUi, out byte[]? data)
    {
        data = null;
        var query = CreateGenericPasswordQuery(service, account, returnData: true, suppressUi);
        try
        {
            var status = SecItemCopyMatching(query, out var result);
            if (status != StatusSuccess || result == IntPtr.Zero)
            {
                return status;
            }

            try
            {
                data = ReadData(result);
                return StatusSuccess;
            }
            finally
            {
                CFRelease(result);
            }
        }
        finally
        {
            CFRelease(query);
        }
    }

    /// <summary>新建条目。返回 <see cref="StatusDuplicateItem"/> 表示已有同名条目（由调用方决定后续）。</summary>
    internal static int TryAddData(string service, string account, string label, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var owned = new List<IntPtr>(4);
        try
        {
            var constants = Constants.Value;
            var keys = new IntPtr[5];
            var values = new IntPtr[5];
            keys[0] = constants.Class;
            values[0] = constants.ClassGenericPassword;
            keys[1] = constants.AttrService;
            values[1] = Own(CFString(service), owned);
            keys[2] = constants.AttrAccount;
            values[2] = Own(CFString(account), owned);
            keys[3] = constants.AttrLabel;
            values[3] = Own(CFString(label), owned);
            keys[4] = constants.ValueData;
            values[4] = Own(CFData(data), owned);

            var attributes = CreateDictionary(keys, values, keys.Length);
            try
            {
                return SecItemAdd(attributes, out _);
            }
            finally
            {
                CFRelease(attributes);
            }
        }
        finally
        {
            foreach (var handle in owned)
            {
                CFRelease(handle);
            }
        }
    }

    /// <summary>原地更新条目的数据（不删再加，故没有"条目短暂消失"的窗口）。</summary>
    internal static int TryUpdateData(string service, string account, byte[] data, bool suppressUi)
    {
        ArgumentNullException.ThrowIfNull(data);

        var owned = new List<IntPtr>(1);
        try
        {
            var constants = Constants.Value;
            var query = CreateGenericPasswordQuery(service, account, returnData: false, suppressUi);
            try
            {
                var attributes = CreateDictionary(
                    [constants.ValueData],
                    [Own(CFData(data), owned)],
                    1);
                try
                {
                    return SecItemUpdate(query, attributes);
                }
                finally
                {
                    CFRelease(attributes);
                }
            }
            finally
            {
                CFRelease(query);
            }
        }
        finally
        {
            foreach (var handle in owned)
            {
                CFRelease(handle);
            }
        }
    }

    /// <summary>删除条目；条目不存在同样视为成功（调用方据此判断"是否真的删掉了"）。</summary>
    internal static int TryDelete(string service, string account, bool suppressUi)
    {
        var query = CreateGenericPasswordQuery(service, account, returnData: false, suppressUi);
        try
        {
            var status = SecItemDelete(query);
            return status == StatusItemNotFound ? StatusSuccess : status;
        }
        finally
        {
            CFRelease(query);
        }
    }

    private static IntPtr CreateGenericPasswordQuery(string service, string account, bool returnData, bool suppressUi)
    {
        var owned = new List<IntPtr>(2);
        try
        {
            var constants = Constants.Value;
            // 最多 6 项：class / service / account / matchLimit / authUI /（可选）returnData
            var keys = new IntPtr[6];
            var values = new IntPtr[6];
            var count = 0;

            void Add(IntPtr key, IntPtr value)
            {
                keys[count] = key;
                values[count] = value;
                count++;
            }

            Add(constants.Class, constants.ClassGenericPassword);
            Add(constants.AttrService, Own(CFString(service), owned));
            Add(constants.AttrAccount, Own(CFString(account), owned));
            Add(constants.MatchLimit, constants.MatchLimitOne);
            Add(constants.UseAuthenticationUi, suppressUi ? constants.UseAuthenticationUiFail : constants.UseAuthenticationUiAllow);

            if (returnData)
            {
                Add(constants.ReturnData, constants.CfBooleanTrue);
            }

            return CreateDictionary(keys, values, count);
        }
        finally
        {
            foreach (var handle in owned)
            {
                CFRelease(handle);
            }
        }
    }

    /// <summary>用非托管数组构造 CFDictionary；字典会 retain 键值（回调见 <see cref="KeyCallBacksSymbol"/>），故返回后即可释放自建 handle。</summary>
    private static IntPtr CreateDictionary(IntPtr[] keys, IntPtr[] values, int count)
    {
        var constants = Constants.Value;
        var nativeKeys = IntPtr.Zero;
        var nativeValues = IntPtr.Zero;
        try
        {
            nativeKeys = Marshal.AllocHGlobal(count * IntPtr.Size);
            nativeValues = Marshal.AllocHGlobal(count * IntPtr.Size);
            for (var i = 0; i < count; i++)
            {
                Marshal.WriteIntPtr(nativeKeys, i * IntPtr.Size, keys[i]);
                Marshal.WriteIntPtr(nativeValues, i * IntPtr.Size, values[i]);
            }

            return CFDictionaryCreate(
                IntPtr.Zero,
                nativeKeys,
                nativeValues,
                count,
                constants.DictionaryKeyCallBacks,
                constants.DictionaryValueCallBacks,
                IntPtr.Zero);
        }
        finally
        {
            if (nativeKeys != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(nativeKeys);
            }

            if (nativeValues != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(nativeValues);
            }
        }
    }

    private static IntPtr CFString(string value) =>
        CFStringCreateWithCString(IntPtr.Zero, value, Utf8Encoding);

    private static IntPtr CFData(byte[] value)
    {
        // 不用 fixed/unsafe：Core 只开 Nullable+ImplicitUsings，用 GCHandle 钉住数组即可。
        var pinned = GCHandle.Alloc(value, GCHandleType.Pinned);
        try
        {
            return CFDataCreate(IntPtr.Zero, pinned.AddrOfPinnedObject(), value.Length);
        }
        finally
        {
            pinned.Free();
        }
    }

    private static byte[] ReadData(IntPtr cfData)
    {
        var length = CFDataGetLength(cfData);
        if (length <= 0)
        {
            return [];
        }

        var buffer = new byte[(int)length];
        Marshal.Copy(CFDataGetBytePtr(cfData), buffer, 0, buffer.Length);
        return buffer;
    }

    private static IntPtr Own(IntPtr handle, List<IntPtr> owned)
    {
        owned.Add(handle);
        return handle;
    }

    [LibraryImport(SecurityLibrary, EntryPoint = "SecItemCopyMatching")]
    private static partial int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [LibraryImport(SecurityLibrary, EntryPoint = "SecItemAdd")]
    private static partial int SecItemAdd(IntPtr attributes, out IntPtr result);

    [LibraryImport(SecurityLibrary, EntryPoint = "SecItemDelete")]
    private static partial int SecItemDelete(IntPtr query);

    [LibraryImport(SecurityLibrary, EntryPoint = "SecItemUpdate")]
    private static partial int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [LibraryImport(CoreFoundationLibrary, EntryPoint = "CFDictionaryCreate")]
    private static partial IntPtr CFDictionaryCreate(
        IntPtr allocator, IntPtr keys, IntPtr values, nint numValues, IntPtr keyCallBack, IntPtr valueCallBack, IntPtr context);

    [LibraryImport(CoreFoundationLibrary, EntryPoint = "CFStringCreateWithCString", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

    [LibraryImport(CoreFoundationLibrary, EntryPoint = "CFDataCreate")]
    private static partial IntPtr CFDataCreate(IntPtr allocator, IntPtr bytes, nint length);

    [LibraryImport(CoreFoundationLibrary, EntryPoint = "CFDataGetLength")]
    private static partial nint CFDataGetLength(IntPtr theData);

    [LibraryImport(CoreFoundationLibrary, EntryPoint = "CFDataGetBytePtr")]
    private static partial IntPtr CFDataGetBytePtr(IntPtr theData);

    [LibraryImport(CoreFoundationLibrary, EntryPoint = "CFRelease")]
    private static partial void CFRelease(IntPtr handle);

    /// <summary>
    /// 一次性解析框架导出的 <c>const CFStringRef</c> 常量（键名 / 类名 / 授权 UI 模式 / CFBooleanTrue）。
    /// </summary>
    private sealed class Constants
    {
        internal static readonly Lazy<Constants> Lazy = new(Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

        internal static Constants Value => Lazy.Value;

        internal IntPtr Class { get; private init; }

        internal IntPtr ClassGenericPassword { get; private init; }

        internal IntPtr AttrService { get; private init; }

        internal IntPtr AttrAccount { get; private init; }

        internal IntPtr AttrLabel { get; private init; }

        internal IntPtr ValueData { get; private init; }

        internal IntPtr MatchLimit { get; private init; }

        internal IntPtr MatchLimitOne { get; private init; }

        internal IntPtr ReturnData { get; private init; }

        internal IntPtr UseAuthenticationUi { get; private init; }

        internal IntPtr UseAuthenticationUiAllow { get; private init; }

        internal IntPtr UseAuthenticationUiFail { get; private init; }

        internal IntPtr CfBooleanTrue { get; private init; }

        /// <summary><c>kCFTypeDictionaryKeyCallBacks</c> 结构体地址（<c>CFDictionaryCreate</c> 的回调参数，不可传 NULL）。</summary>
        internal IntPtr DictionaryKeyCallBacks { get; private init; }

        /// <summary><c>kCFTypeDictionaryValueCallBacks</c> 结构体地址。</summary>
        internal IntPtr DictionaryValueCallBacks { get; private init; }

        private static Constants Resolve()
        {
            var security = LoadLibrary(SecurityLibrary);
            var coreFoundation = LoadLibrary(CoreFoundationLibrary);

            return new Constants
            {
                Class = Symbol(security, "kSecClass"),
                ClassGenericPassword = Symbol(security, "kSecClassGenericPassword"),
                AttrService = Symbol(security, "kSecAttrService"),
                AttrAccount = Symbol(security, "kSecAttrAccount"),
                AttrLabel = Symbol(security, "kSecAttrLabel"),
                ValueData = Symbol(security, "kSecValueData"),
                MatchLimit = Symbol(security, "kSecMatchLimit"),
                MatchLimitOne = Symbol(security, "kSecMatchLimitOne"),
                ReturnData = Symbol(security, "kSecReturnData"),
                UseAuthenticationUi = Symbol(security, "kSecUseAuthenticationUI"),
                UseAuthenticationUiAllow = Symbol(security, "kSecUseAuthenticationUIAllow"),
                UseAuthenticationUiFail = Symbol(security, "kSecUseAuthenticationUIFail"),
                CfBooleanTrue = Symbol(coreFoundation, "kCFBooleanTrue"),
                DictionaryKeyCallBacks = Address(coreFoundation, KeyCallBacksSymbol),
                DictionaryValueCallBacks = Address(coreFoundation, ValueCallBacksSymbol),
            };
        }

        private static IntPtr LoadLibrary(string path)
        {
            if (!NativeLibrary.TryLoad(path, out var handle) || handle == IntPtr.Zero)
            {
                throw new KeychainUnavailableException($"无法加载 macOS 系统框架 {path}。");
            }

            return handle;
        }

        /// <summary>
        /// 读取导出的数据符号：<c>dlsym</c> 对数据符号返回**变量地址**，故须再解引用一次拿到
        /// <c>CFStringRef</c> / <c>CFBooleanRef</c>。
        /// </summary>
        private static IntPtr Symbol(IntPtr handle, string name) => Marshal.ReadIntPtr(Address(handle, name));

        /// <summary>取导出符号的地址（用于 <c>kCFTypeDictionary*CallBacks</c> 这类结构体变量）。</summary>
        private static IntPtr Address(IntPtr handle, string name)
        {
            if (!NativeLibrary.TryGetExport(handle, name, out var address) || address == IntPtr.Zero)
            {
                throw new KeychainUnavailableException($"无法解析系统框架符号 {name}（macOS 版本或框架布局异常）。");
            }

            return (IntPtr)address;
        }
    }
}