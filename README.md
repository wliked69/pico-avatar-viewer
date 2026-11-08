# PICO Avatar Viewer

PICO 头显独立运行的角色查看器，以及 Unity USB 模型上传和头显模型管理插件。

本仓库源码对应正常版：查看器基线 **0.3.23**，上传插件基线 **0.3.8**。源码树不包含任何用户模型、转换后的模型包、贴图、材质、动画、生成资源、字体二进制、第三方 SDK 或 APK。安装后需要自行导入有权使用的模型。

## 下载安装包

前往 [Releases](https://github.com/wliked69/pico-avatar-viewer/releases/latest)：

- **AvatarViewer-0.3.23-no-models.apk**：正常版头显应用，不内置用户角色。
- **PicoAvatarUploader-0.3.8.unitypackage**：Unity 上传和模型管理插件，导入你自己的模型工程。
- **SHA256SUMS.txt**：下载文件的 SHA-256 校验值。

APK 使用 Noto Sans SC 中文字体，字体许可证位于 APK 内的 `assets/ThirdParty/NotoSansSC-OFL.txt`，Release 也附同一许可证。安装 APK 后，再用插件通过 USB 上传你有权使用的模型；Release 不包含模型包。

## 功能

- 现实透视、第一人称附身、手部骨骼重定向与比例校准。
- 镜子、近似动骨、眼动和面捕；对应功能需要头显及模型支持。
- 导入模型的 VRC 表情菜单、换装与道具开关。
- 拇指与小指指尖接触两秒召唤轮盘，轮盘跟随召唤手，双手丢失追踪后关闭。
- Unity 插件通过 USB 构建、上传和加载 Android 模型包，显示传输进度。
- 头显模型管理：扫描、搜索、加载、备份、重命名和删除。

这是独立查看器，不是 VRChat 客户端。近似动骨不等同于完整 PhysBones；SPS、完整 Contacts 和完整 Base/Action/Gesture 动画层尚未实现，部分衣物碰撞和特殊着色器仍有差异。

## 目录

| 目录 | 内容 |
| --- | --- |
| `App/` | Unity 查看器源码与项目设置 |
| `UnityPlugin/Assets/AvatarViewerExport/` | 可复制到模型工程的 Unity 上传插件 |
| `Tools/` | 本机依赖配置和构建脚本 |
| `Docs/` | 模型导入和仓库内容说明 |

## 构建查看器

需要 Windows、Unity **2022.3.22f1**、Android Build Support（包括 SDK、NDK、OpenJDK），以及自行获取的 **PICO Unity XR SDK 6.1.1**。

1. 克隆仓库。
2. 配置本地 SDK：

   ```powershell
   ./Tools/Setup.ps1 -PicoSdkPath '你的 PICO Unity SDK/XR 路径'
   ```

3. 将有权使用的中文 TrueType 字体放到 `App/Assets/AvatarViewer/Fonts/ViewerFont.ttf`。字体和生成的本机 SDK 路径均不会提交到 Git。
   要使用与公开 APK 一致的字体，可运行 `./Tools/Prepare-ReleaseFont.ps1`。脚本从官方固定版本下载 Noto Sans SC、校验哈希，并准备 APK 内的 OFL 许可证。
4. 使用 Unity 2022.3.22f1 打开 `App/`，等待依赖导入完成。可通过命令行构建：

   ```powershell
   ./Tools/Build.ps1 -UnityEditor '你的 Unity/Editor/Unity.exe 路径'
   ```

   仅生成场景并运行基础行为检查时添加 `-ChecksOnly`。构建结果位于 `Builds/AvatarViewer.apk`，构建目录不纳入版本控制。

查看器不依赖仓库内的默认角色。首次运行可通过插件上传模型，或在应用中导入 `.avatarbundle`。

## 使用上传插件

1. 使用 Unity 2022.3 打开你自己的模型工程，确保安装 Android Build Support。
2. 将 `UnityPlugin/Assets/AvatarViewerExport` 文件夹复制到该工程的 `Assets/` 下；保留 `.meta` 文件。插件应装在模型工程中，而非再次导入本仓库的查看器工程。
3. 头显安装查看器，并通过 USB 连接电脑，在头显内授权 USB 调试。
4. 将模型放入场景并选中根对象，打开 **PICO Avatar → USB 模型上传**。
5. 确认目标设备，点击转换并上传。插件在独立临时工程构建模型包，不需要切换原模型工程的平台。

管理已有文件使用 **PICO Avatar → 头显模型管理**。备份和上传使用 SHA-256 校验；删除前会要求确认。模型只通过 USB 传到所选头显，插件不把模型上传到 GitHub 或其他云端。

模型使用 VRChat SDK、Modular Avatar 或 NDMF 时，请在你自己的模型工程中安装所需依赖。插件保留可转换部分，但不保证兼容所有模型扩展。

## 头显内操作

先站直完成身高校准。将同一只手的拇指和小指指尖接触、食指分开，保持两秒；指尖处圆环填满后召唤菜单。松开后才能再次召唤。也可使用左手柄菜单键。

轮盘提供模型库、角色设置、镜子、菜单位置和应用设置。无身体追踪器时，躯干与腿部姿态为估算。眼追和面捕需要在头显内授权，并使用包含对应眼骨或表情形态的模型。

## 模型与依赖

请阅读 [模型文件与仓库边界](Docs/MODELS.md)。第三方 SDK、模型、字体及模型使用的着色器分别遵守其原始许可，本仓库不授予它们的使用或再分发权。

## 本次源码发布验证

- 从此源码目录构建无默认模型的 APK：成功，0 个错误。
- 查看器基础行为检查：39 项通过。
- 插件在无 VRC SDK、无模型的独立 Unity 工程编译，5 项导出入口和 USB 选择检查通过。
- Git 提交清单已检查：不包含用户模型、生成资源、本机依赖路径、字体、设备序列号和凭据。

这些是源码整理与构建验证，不代表重新完成了全部头显佩戴测试。历史使用效果和限制应结合实际设备与模型验证。
