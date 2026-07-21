// Project: Daggerfall Unity iOS
// Purpose: Preserve the Compiler API while runtime C# compilation is excluded from iOS IL2CPP.

#if UNITY_IOS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DaggerfallWorkshop.Game.Utility
{
    public class Compiler
    {
        private const string UnsupportedMessage =
            "Runtime C# compilation is unavailable on iOS IL2CPP builds.";

        public static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        }

        public static Assembly CompileSource(
            string[] sources,
            bool isSource,
            bool GenerateInMemory = true)
        {
            throw new PlatformNotSupportedException(UnsupportedMessage);
        }
    }
}
#endif
