using System.Reflection;

namespace JmcModLib.Utils
{

    internal partial class BuildLoggerUI
    {
        internal abstract class BuildBase
        {
            protected virtual string GroupName => "Mod Logger";
            internal abstract void BuildUI(Assembly asm);
        }
    }
}
