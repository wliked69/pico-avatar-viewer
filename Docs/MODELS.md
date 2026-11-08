# 模型文件与仓库边界

本仓库采用源码白名单整理，不包含任何已上传、购买或转换后的用户角色。

源码树不包含：原始模型工程、FBX/VRM/GLB、Prefab、Animator Controller、动画、贴图、材质、用户着色器源码副本、AssetBundle、模型预览图、姿态记录、个人校准数据、USB 设备序列号、APK、Unity 缓存和本机工具。

`App/Assets/AvatarViewer/Runtime/PortableToon.shader` 是查看器自身的通用着色器，不是从用户模型导出的着色器。

Unity 上传插件的 `Generated/`、`Library/PicoAvatarUpload/` 和其他构建结果都是本地产物。它们会包含所选模型及其依赖，已由忽略规则排除，不要强制提交。

Release 仅发布从干净源码构建的无默认角色 APK 和源码插件包。以前本地构建的 APK 可能内置模型，不应直接作为公共发布附件。首次运行需要自行导入模型。Release 中的 APK 使用可随软件分发的 Noto Sans SC 字体，并在包内附字体许可证；它不包含原来本地验证使用的系统字体。

使用、转换或上传模型前，请确认拥有相应权限。将仓库设为公开不改变第三方模型、SDK、字体或着色器的许可。
