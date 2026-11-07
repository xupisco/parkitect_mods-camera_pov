using System;
using System.Collections.Generic;
using System.Reflection;
using Parkitect.UI;

namespace camera_pov
{
    // What the info window in front shows: in the game, opening a guest's, an employee's or a ride's window is
    // selecting them. Only people and attractions count, so a shop window in front of a guest's leaves the guest selected.
    internal static class selected_object
    {
        // ObjectInfoWindow<T>.getObject is protected, so it is read by reflection, once per window type.
        private static readonly Dictionary<Type, MethodInfo> get_object_methods = new Dictionary<Type, MethodInfo>();

        public static SerializedMonoBehaviour find(out UIWindowFrame window)
        {
            window = null;
            UIWindowsController windows = UIWindowsController.Instance;
            if (windows == null)
            {
                return null;
            }

            // Bringing a window to the front makes it the last sibling, so the highest index is the front one.
            SerializedMonoBehaviour front = null;
            int front_index = -1;
            foreach (UIWindowFrame frame in windows.getWindows())
            {
                if (frame == null || frame.windowContent == null)
                {
                    continue;
                }
                SerializedMonoBehaviour shown = object_of(frame.windowContent);
                int index = frame.transform.GetSiblingIndex();
                if ((shown is Person || shown is Attraction) && index > front_index)
                {
                    front = shown;
                    window = frame;
                    front_index = index;
                }
            }
            return front;
        }

        private static SerializedMonoBehaviour object_of(UIWindow window)
        {
            Type type = window.GetType();
            if (!get_object_methods.TryGetValue(type, out MethodInfo method))
            {
                method = is_object_window(type) ? type.GetMethod("getObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) : null;
                get_object_methods[type] = method;
            }
            return method != null ? method.Invoke(window, null) as SerializedMonoBehaviour : null;
        }

        private static bool is_object_window(Type type)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ObjectInfoWindow<>))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
