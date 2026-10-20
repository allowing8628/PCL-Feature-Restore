# 实现与来源

目标为上游标签 2.99.0.0 的正式版 EXE。参考流程来自上游标签 2.14.0.0 的正式版 ZIP；该 ZIP 内 EXE 的实际版本是 **2.13.1.1**，脚本会同时检查版本与哈希。

1. `scripts/Restore-Download.ps1`：在 PageDownloadInstall.LoaderInit 内将“关闭下载”任务替换为已有的 DlClientListLoader。限制字节修改范围，不运行 PCL。
2. `src/RestoreModpack.cs` 的 Run：恢复无参数的选择文件入口；保留目标版本路径检查、ZIP 打开和格式识别前缀，映射参考版本分派尾部至目标现有的七种 InstallPack 方法。映射分支与异常边界，修复 finally HandlerEnd。
3. RestoreRemaining：恢复 PageDownloadModify.LoadInstanceFinished 中 Minecraft 卡片；在线 ModPack 版本项使用现有 Install_Click、Save_Click 双委托；拖入路径跳过过时的禁用提示，保留原有取消/错误处理；RestrictMcDownload 元数据常量改为 false。
4. 比较修改前后方法指纹、资源和程序集引用。仅将元数据标志设为 false 不够，因为 VB 常量已在编译期影响方法体。

原始文件地址、固定哈希和下载过程均在 Build.ps1 可审查。工具只通过 dnlib 读取/改写 PE，不执行启动器。构建时的静态检查不能替代运行安装验证。

源码定位：ModSecret.vb、PageDownloadInstall.xaml.vb、ModModpack.vb、PageDownloadModify.xaml.vb、PageDownloadCompDetail.xaml.vb、FormMain.xaml.vb，参见上游固定标签。
