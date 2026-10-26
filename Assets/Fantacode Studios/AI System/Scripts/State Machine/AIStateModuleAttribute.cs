using System;

namespace FS_Core
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class AIStateModuleAttribute : Attribute
    {
        public string ModuleName { get; }
        public string DisplayName { get; }

        public AIStateModuleAttribute(string moduleName, string displayName)
        {
            ModuleName = moduleName;
            DisplayName = displayName;
        }
    }
}
