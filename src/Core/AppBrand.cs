namespace Voltide.Core
{
    /// <summary>
    /// 集中的品牌标识。改名时由 rebrand.ps1 统一更新本文件，界面、托盘、
    /// 互斥名、注册表键、错误日志等全部引用这里，避免散落的硬编码。
    /// </summary>
    public static class AppBrand
    {
        /// <summary>界面大标题与托盘悬浮提示使用的名称。</summary>
        public const string Name = "Voltide";

        /// <summary>编译输出的可执行文件名（含 .exe）。</summary>
        public const string ExeName = "Voltide.exe";

        /// <summary>单实例互斥体名称。</summary>
        public const string MutexId = "Voltide-SingleInstance";

        /// <summary>开机启动/设置使用的注册表键。</summary>
        public const string SettingsKey = @"Software\Voltide";

        /// <summary>未捕获异常时写入临时目录的错误文件名。</summary>
        public const string ErrorLog = "Voltide-error.txt";
    }
}
