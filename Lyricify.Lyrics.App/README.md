# Lyricify — Mobile App

[English](#english) | [中文](#中文)

---

## English

### Overview

Lyricify is a .NET MAUI mobile application (Android & iOS) that shows real-time, synchronized lyrics for the track currently playing in your Spotify client.

**Key capabilities**

| Feature | Details |
|---|---|
| Now-Playing polling | Queries `GET /v1/me/player/currently-playing` every 500 ms via the Spotify Web API |
| Lyric sync | 100 ms local timer interpolates the playback position between API polls |
| Lyrics sources | ① Spotify internal lyrics (requires `sp_dc` cookie) → ② [LRCLIB](https://lrclib.net) public API as a free fallback |
| Android overlay | Floating window that stays on top of the Spotify app |
| Credentials | Client ID, Client Secret, and `sp_dc` cookie are all stored in device Preferences — no rebuild needed |

---

### Requirements

| Requirement | Version |
|---|---|
| .NET SDK | 8.0 or later |
| .NET MAUI workload | `dotnet workload install maui` |
| Android | API 26 (Android 8.0) or later |
| iOS | 15.0 or later |
| Spotify account | Free or Premium |

---

### Spotify Developer Setup

Before signing in you must register a Spotify application and obtain credentials.

1. Go to [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) and create a new app (or open an existing one).
2. In **Settings → Redirect URIs**, add exactly:
   ```
   lyricify://callback
   ```
3. Copy the **Client ID** and **Client Secret** shown on the app's overview page.

> **Note — PKCE vs secret flow**
>
> If your app is registered in _PKCE-only_ mode you can leave the Client Secret field empty in Settings.
> If your app uses the standard authorization-code flow a Client Secret is required.

---

### Getting Started

#### 1. Build & run

```bash
# Android (connected device or emulator)
dotnet build -t:Run -f net8.0-android

# iOS (connected device or Simulator)
dotnet build -t:Run -f net8.0-ios
```

#### 2. Enter credentials in Settings

On first launch the **Sign in** screen shows a warning banner because no Client ID is configured yet.

1. Tap **Open Settings** (or navigate to **Settings** via the tab bar).
2. Under **Spotify**, fill in:
   - **Client ID** — paste from the Spotify dashboard (required)
   - **Client Secret** — paste from the Spotify dashboard (required for non-PKCE apps)
   - **sp_dc cookie** *(optional)* — enables Spotify's own lyrics; LRCLIB is used as a fallback when omitted
3. Tap **Save Credentials**.

> **How to get the `sp_dc` cookie (optional)**
>
> Log in to [open.spotify.com](https://open.spotify.com) in a desktop browser, open DevTools → Application → Cookies, and copy the value of the `sp_dc` cookie.

#### 3. Sign in

Return to the **Sign in** screen and tap **Connect Spotify**. A browser window opens for the Spotify OAuth2 consent screen. After you approve, the app receives the access token automatically and opens the lyrics view.

---

### Architecture

```
Lyricify.Lyrics.App/
├── Services/
│   ├── SpotifyOAuthService.cs        # OAuth2 PKCE + Basic auth, token storage
│   ├── SpotifyNowPlayingService.cs   # 500 ms polling loop, TrackChanged / StateUpdated events
│   ├── LyricsService.cs              # Fetch & cache lyrics (Spotify → LRCLIB fallback)
│   └── LyricsSyncService.cs          # 100 ms timer, position interpolation, SyncResultUpdated event
├── ViewModels/
│   └── LyricsViewModel.cs            # Binds services to the lyrics UI
├── Views/
│   ├── LoginPage.xaml(.cs)           # Credential-check guard + OAuth entry point
│   ├── LyricsPage.xaml(.cs)          # Scrolling, highlighted lyric lines
│   └── SettingsPage.xaml(.cs)        # Client ID / Secret / sp_dc / display settings
└── Platforms/
    ├── Android/
    │   ├── AndroidManifest.xml       # Permissions: INTERNET, SYSTEM_ALERT_WINDOW, FOREGROUND_SERVICE
    │   └── LyricsOverlayService.cs   # Floating window foreground service
    └── iOS/
        ├── Info.plist                # ATS exceptions (api.spotify.com, lrclib.net, localhost)
        └── AppDelegate.cs            # Forwards OpenUrl to WebAuthenticator
```

---

### Preferences stored on device

| Key | Content |
|---|---|
| `spotify_client_id` | Spotify app Client ID |
| `spotify_client_secret` | Spotify app Client Secret |
| `spotify_sp_dc` | Spotify web `sp_dc` session cookie |
| `spotify_access_token` | OAuth2 access token |
| `spotify_refresh_token` | OAuth2 refresh token |
| `spotify_token_expires_at` | Unix timestamp (seconds) |
| `lyrics_font_size` | Lyrics display font size (12–32) |
| `overlay_opacity` | Android overlay opacity (0.3–1.0) |

---

### Troubleshooting

| Symptom | Solution |
|---|---|
| "Spotify Client ID is not set" | Enter your Client ID in Settings → Save Credentials |
| Login page shows warning banner | Client ID is missing — tap **Open Settings** |
| Login fails with `invalid_client` | Verify Client ID and Client Secret match those in your Spotify dashboard |
| Redirect mismatch error | Ensure `lyricify://callback` is added **exactly** as a Redirect URI in your Spotify dashboard |
| No lyrics shown | Check your internet connection; optionally add an `sp_dc` cookie in Settings |
| Lyrics out of sync | The 100 ms local timer estimates position; a brief lag is normal. It corrects itself on the next API poll. |

---

---

## 中文

### 概述

Lyricify 是一个基于 .NET MAUI 的移动端应用（支持 Android 和 iOS），可实时显示 Spotify 当前播放曲目的同步歌词。

**主要功能**

| 功能 | 说明 |
|---|---|
| 正在播放轮询 | 每 500 ms 通过 Spotify Web API 查询 `GET /v1/me/player/currently-playing` |
| 歌词同步 | 本地 100 ms 定时器在两次 API 轮询之间插值估算播放进度 |
| 歌词来源 | ① Spotify 内部歌词接口（需要 `sp_dc` cookie）→ ② [LRCLIB](https://lrclib.net) 公共 API 作为免费回退 |
| Android 悬浮窗 | 悬浮在 Spotify 应用上方的歌词窗口 |
| 凭据管理 | Client ID、Client Secret 和 `sp_dc` cookie 均保存在设备 Preferences 中，无需重新编译 |

---

### 运行环境要求

| 要求 | 版本 |
|---|---|
| .NET SDK | 8.0 及以上 |
| .NET MAUI 工作负载 | `dotnet workload install maui` |
| Android | API 26（Android 8.0）及以上 |
| iOS | 15.0 及以上 |
| Spotify 账号 | 免费或高级版均可 |

---

### Spotify 开发者配置

登录前，你需要在 Spotify 开发者平台注册应用并获取凭据。

1. 前往 [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard)，创建一个新应用（或打开已有应用）。
2. 在 **Settings → Redirect URIs** 中，添加以下地址（必须完全一致）：
   ```
   lyricify://callback
   ```
3. 复制应用概览页面显示的 **Client ID** 和 **Client Secret**。

> **说明 — PKCE 模式与 Secret 模式**
>
> 如果你的应用注册为 _仅 PKCE_ 模式，可在设置页中留空 Client Secret。
> 如果使用标准授权码流程，则必须填写 Client Secret。

---

### 快速上手

#### 1. 编译并运行

```bash
# Android（连接设备或模拟器）
dotnet build -t:Run -f net8.0-android

# iOS（连接设备或模拟器）
dotnet build -t:Run -f net8.0-ios
```

#### 2. 在设置中填写凭据

首次启动时，**登录**页面会显示警告横幅，提示尚未配置 Client ID。

1. 点击 **Open Settings**（或通过底部导航栏进入 **Settings**）。
2. 在 **Spotify** 区域填写：
   - **Client ID** — 从 Spotify 开发者控制台粘贴（必填）
   - **Client Secret** — 从 Spotify 开发者控制台粘贴（非 PKCE 应用必填）
   - **sp_dc cookie** *(可选)* — 用于获取 Spotify 自有歌词；未填时自动使用 LRCLIB 回退
3. 点击 **Save Credentials（保存凭据）**。

> **如何获取 `sp_dc` cookie（可选）**
>
> 在桌面浏览器中登录 [open.spotify.com](https://open.spotify.com)，打开开发者工具 → Application → Cookies，复制 `sp_dc` 的值。

#### 3. 登录

返回**登录**页面，点击 **Connect Spotify**。浏览器会打开 Spotify OAuth2 授权页面。授权后，应用将自动获取访问令牌并跳转到歌词界面。

---

### 项目架构

```
Lyricify.Lyrics.App/
├── Services/
│   ├── SpotifyOAuthService.cs        # OAuth2 PKCE + Basic 认证，令牌存储
│   ├── SpotifyNowPlayingService.cs   # 500 ms 轮询循环，TrackChanged / StateUpdated 事件
│   ├── LyricsService.cs              # 获取并缓存歌词（Spotify → LRCLIB 回退）
│   └── LyricsSyncService.cs          # 100 ms 定时器，播放进度插值，SyncResultUpdated 事件
├── ViewModels/
│   └── LyricsViewModel.cs            # 将服务绑定到歌词 UI
├── Views/
│   ├── LoginPage.xaml(.cs)           # 凭据检查 + OAuth 入口
│   ├── LyricsPage.xaml(.cs)          # 滚动高亮歌词行
│   └── SettingsPage.xaml(.cs)        # Client ID / Secret / sp_dc / 显示设置
└── Platforms/
    ├── Android/
    │   ├── AndroidManifest.xml       # 权限：INTERNET、SYSTEM_ALERT_WINDOW、FOREGROUND_SERVICE
    │   └── LyricsOverlayService.cs   # 悬浮窗前台服务
    └── iOS/
        ├── Info.plist                # ATS 例外（api.spotify.com、lrclib.net、localhost）
        └── AppDelegate.cs            # 将 OpenUrl 转发给 WebAuthenticator
```

---

### 设备端存储的 Preferences

| 键名 | 内容 |
|---|---|
| `spotify_client_id` | Spotify 应用 Client ID |
| `spotify_client_secret` | Spotify 应用 Client Secret |
| `spotify_sp_dc` | Spotify 网页端 `sp_dc` 会话 cookie |
| `spotify_access_token` | OAuth2 访问令牌 |
| `spotify_refresh_token` | OAuth2 刷新令牌 |
| `spotify_token_expires_at` | Unix 时间戳（秒） |
| `lyrics_font_size` | 歌词字体大小（12–32） |
| `overlay_opacity` | Android 悬浮窗透明度（0.3–1.0） |

---

### 常见问题排查

| 现象 | 解决方法 |
|---|---|
| 提示"Spotify Client ID is not set" | 在设置中填写 Client ID 并点击保存 |
| 登录页显示警告横幅 | Client ID 尚未填写，点击 **Open Settings** |
| 登录失败，提示 `invalid_client` | 检查 Client ID 和 Client Secret 是否与 Spotify 控制台一致 |
| 回调地址不匹配错误 | 确保 Spotify 控制台中已**精确**添加 `lyricify://callback` 为回传地址 |
| 无歌词显示 | 检查网络连接；可选填写 `sp_dc` cookie 以启用 Spotify 自有歌词 |
| 歌词不同步 | 本地 100 ms 定时器会估算播放进度，轻微延迟属正常现象，下次 API 轮询后会自动校正 |
