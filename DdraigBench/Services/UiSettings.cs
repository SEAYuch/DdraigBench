// DdraigBench — 界面偏好模型（ui-settings.json 的内容）

namespace DdraigBench.Services;

/// <summary>
/// 落盘的界面偏好。<b>一个文件一条记录</b>：主题与「上次连接的连接名」必须一起读写，
/// 否则两个各自只写自己那半边的存储会互相覆盖（见 <see cref="IUiSettingsStore"/>）。
/// </summary>
/// <param name="Theme">界面外观（跟随系统 / 浅色 / 深色）。</param>
/// <param name="LastConnectionName">
/// 上次成功连接的名字，启动时据此自动重连。null / 空表示没有可重连的目标；
/// 名字对应的条目后来被移除时安静跳过（按名字匹配，不按索引——连接串与口令不落在这里）。
/// </param>
public sealed record UiSettings(ThemeMode Theme = ThemeMode.System, string? LastConnectionName = null);
