using System;

namespace FS_Core
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class AINodeModuleAttribute : Attribute
    {
        public string ModuleName { get; }
        public string DisplayName { get; }

        public AINodeModuleAttribute(string moduleName, string displayName)
        {
            ModuleName = moduleName;
            DisplayName = displayName;
        }
    }
}
