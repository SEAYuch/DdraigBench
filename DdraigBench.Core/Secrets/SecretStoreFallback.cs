// DdraigBench — 密钥库回退说明（把「为什么只能明文保存」变成用户可读、可操作的一句话）

namespace DdraigBench.Core.Secrets;

/// <summary>
/// 系统密钥库不可用的原因分类。平台无关：macOS 的 <c>OSStatus</c>、Linux 的 D-Bus 失败都收敛到这里，
/// 再由 <see cref="SecretStoreFallback.Describe"/> 翻成给用户看的中文说明。
/// </summary>
/// <remarks>
/// 分类与文案都是纯逻辑（不碰 P/Invoke、不碰 D-Bus），因此**在 Windows 上也能完整单测**——
/// 这正是把"钥匙串 ACL 拦下"和"系统根本没有钥匙库"分开的价值：前者用户能自己修（删条目/解锁/允许授权），
/// 后者只能换环境。以前两者都被压成一句"未检测到可用的系统密钥库"，用户无从下手。
/// </remarks>
public enum SecretStoreFallbackReason
{
    /// <summary>未能归类（文案会带上平台给的原始细节）。</summary>
    Unknown = 0,

    /// <summary>系统没有可用的密钥库（macOS 钥匙串不可用 / Linux 无 Secret Service 实现）。</summary>
    KeyStoreMissing,

    /// <summary>当前平台没有内置的密钥库通道。</summary>
    PlatformUnsupported,

    /// <summary>连不上密钥库服务（Linux：D-Bus 会话总线不可达）。</summary>
    ServiceUnreachable,

    /// <summary>密钥库拒绝访问授权（macOS <c>errSecAuthFailed</c>，多为 ACL 拦下）。</summary>
    AccessDenied,

    /// <summary>密钥库已锁定且不允许弹出授权界面（macOS <c>errSecInteractionNotAllowed</c>）。</summary>
    Locked,

    /// <summary>缺少钥匙串访问授权组（macOS <c>errSecMissingEntitlement</c>，沙箱/未签名打包）。</summary>
    MissingEntitlement,

    /// <summary>用户在授权对话框里拒绝了访问。</summary>
    UserDeclined,
}

/// <summary>
/// 密钥库回退原因 → 用户可读说明。**文案必须给出下一步动作**（删条目 / 解锁 / 换桌面会话 / 用签名版本），
/// 只说"不可用"等于把问题丢回给用户。
/// </summary>
public static class SecretStoreFallback
{
    /// <summary>
    /// macOS <c>OSStatus</c> → 原因分类。常量值取自 SDK 的 <c>SecBase.h</c>（<c>errSec*</c>），
    /// 与 <see cref="MacSecurityFramework"/> 的 <c>Status*</c> 常量同源；这里用编译期常量引用，
    /// 不会触发任何 P/Invoke 类型初始化，故可在任意平台调用。
    /// </summary>
    public static SecretStoreFallbackReason FromMacStatus(int status) => status switch
    {
        MacSecurityFramework.StatusUserCanceled => SecretStoreFallbackReason.UserDeclined,
        MacSecurityFramework.StatusNotAvailable => SecretStoreFallbackReason.KeyStoreMissing,
        MacSecurityFramework.StatusAuthFailed => SecretStoreFallbackReason.AccessDenied,
        MacSecurityFramework.StatusInteractionNotAllowed => SecretStoreFallbackReason.Locked,
        MacSecurityFramework.StatusMissingEntitlement => SecretStoreFallbackReason.MissingEntitlement,
        _ => SecretStoreFallbackReason.Unknown,
    };

    /// <summary>
    /// Linux / 通用探测失败的归类（尽力而为）：平台守卫 → <see cref="SecretStoreFallbackReason.PlatformUnsupported"/>；
    /// 异常类型名或消息里出现 D-Bus 线索 → <see cref="SecretStoreFallbackReason.ServiceUnreachable"/>；其余归
    /// <see cref="SecretStoreFallbackReason.Unknown"/>（文案会带上原始错误文本）。
    /// </summary>
    /// <remarks>
    /// 刻意按**类型名与消息**判断而不是硬编异常类型：D-Bus 绑定的异常类型随包版本变化，
    /// 硬编会在升级时静默退化成"未能归类"。代价是归类是启发式的——所以 Unknown 分支必须保留原始细节。
    /// </remarks>
    public static SecretStoreFallbackReason FromFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is PlatformNotSupportedException)
        {
            return SecretStoreFallbackReason.PlatformUnsupported;
        }

        var text = $"{exception.GetType().FullName} {exception.Message}";
        return text.Contains("DBus", StringComparison.OrdinalIgnoreCase)
            || text.Contains("D-Bus", StringComparison.OrdinalIgnoreCase)
            || text.Contains("session bus", StringComparison.OrdinalIgnoreCase)
            ? SecretStoreFallbackReason.ServiceUnreachable
            : SecretStoreFallbackReason.Unknown;
    }

    /// <summary>
    /// 把原因翻成一句给用户看的说明；<paramref name="platformDetail"/> 是平台层的原始细节
    /// （如 <c>钥匙串授权校验失败（OSStatus -25293）</c>），只在 <see cref="SecretStoreFallbackReason.Unknown"/>
    /// 时作为补充展示——已知原因自带的文案比原始状态码对用户更有用。
    /// </summary>
    public static string Describe(SecretStoreFallbackReason reason, string? platformDetail = null) => reason switch
    {
        SecretStoreFallbackReason.KeyStoreMissing =>
            "系统没有可用的密钥库（macOS 钥匙串不可用；Linux 上通常是当前会话里没有 gnome-keyring / KWallet"
            + " 等 Secret Service 实现）。口令将不再受系统密钥库保护。",
        SecretStoreFallbackReason.PlatformUnsupported =>
            "当前平台没有内置的密钥库通道（Windows 用 DPAPI、macOS 用钥匙串、Linux/FreeBSD 用 Secret Service）。",
        SecretStoreFallbackReason.ServiceUnreachable =>
            "连接不上 freedesktop Secret Service（D-Bus 会话总线不可达，常见于 SSH 会话、容器或纯命令行环境）。"
            + "请在桌面会话中启动本应用，或安装并解锁 gnome-keyring / KWallet。",
        SecretStoreFallbackReason.AccessDenied =>
            "系统钥匙串拒绝了对 DdraigBench 主密钥的访问授权（多半是条目的访问控制列表拦下了本次应用）。"
            + "可在「钥匙串访问」中删除名为 DdraigBench 的条目后重启本应用——它会在重新创建条目时再次请求授权。",
        SecretStoreFallbackReason.Locked =>
            "密钥库处于锁定状态，且探测阶段不允许弹出授权界面。请先解锁钥匙串（macOS：在「钥匙串访问」中"
            + "解锁 login 钥匙串），再重启本应用。",
        SecretStoreFallbackReason.MissingEntitlement =>
            "当前进程缺少钥匙串访问授权组（keychain-access-groups），多见于沙箱或未按发布流程打包的构建。"
            + "请改用正式签名的构建。",
        SecretStoreFallbackReason.UserDeclined =>
            "授权对话框里选择了拒绝访问钥匙串。如需加密保存口令，请重启本应用并在弹框中选择允许。",
        _ => string.IsNullOrWhiteSpace(platformDetail)
            ? "系统密钥库不可用。"
            : $"系统密钥库不可用（{platformDetail}）。",
    };
}
