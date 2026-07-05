using Grasshopper.Kernel.Types;

namespace Marmoset.Components.RL
{
    /// <summary>Shared helpers for the RL component family.</summary>
    internal static class RLComponentUtil
    {
        /// <summary>
        /// Unwraps a value coming through a generic Grasshopper parameter.
        /// Handles raw instances, GH_ObjectWrapper and any other IGH_Goo.
        /// </summary>
        public static T Unwrap<T>(object source) where T : class
        {
            switch (source)
            {
                case null:
                    return null;
                case T direct:
                    return direct;
                case GH_ObjectWrapper wrapper:
                    return wrapper.Value as T;
                case IGH_Goo goo:
                    return goo.ScriptVariable() as T;
                default:
                    return null;
            }
        }
    }
}
