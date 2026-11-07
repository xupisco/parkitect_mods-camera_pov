using System;
using System.Collections.Generic;
using System.IO;
using MiniJSON;
using UnityEngine;

namespace camera_pov
{
    // The player's choices for the mod. Kept next to the game's settings rather than in the mod folder, which Workshop
    // updates can replace.
    internal static class camera_pov_settings
    {
        private static string file_path => FilePaths.getFolderPath("camera_pov.settings.json");

        private static bool loaded;

        // In tiles. A guest's eyes, estimated from the seat offset the game's unused ride camera uses.
        public static float eye_height = 0.6f;
        public static float field_of_view = 70f;
        public static float mouse_sensitivity = 2f;
        public static bool invert_mouse;
        // Walking runs into buildings, scenery and rides and stands on built floors. Flying always goes through.
        public static bool collision = true;
        // In seconds, for the whole way in or out between the normal camera and first person.
        public static float transition_time = 0.75f;

        public static void load()
        {
            if (loaded)
            {
                return;
            }
            loaded = true;

            try
            {
                if (File.Exists(file_path) && Json.Deserialize(File.ReadAllText(file_path)) is Dictionary<string, object> values)
                {
                    eye_height = read_float(values, "eye_height", eye_height);
                    field_of_view = read_float(values, "field_of_view", field_of_view);
                    mouse_sensitivity = read_float(values, "mouse_sensitivity", mouse_sensitivity);
                    transition_time = Mathf.Clamp(read_float(values, "transition_time", transition_time), 0.3f, 2f);
                    if (values.TryGetValue("collision", out object collide) && collide is bool collide_value)
                    {
                        collision = collide_value;
                    }
                    if (values.TryGetValue("invert_mouse", out object invert) && invert is bool invert_value)
                    {
                        invert_mouse = invert_value;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[camera_pov] Couldn't read " + file_path + ": " + exception);
            }
        }

        public static void save()
        {
            try
            {
                Dictionary<string, object> values = new Dictionary<string, object>
                {
                    { "eye_height", eye_height },
                    { "field_of_view", field_of_view },
                    { "mouse_sensitivity", mouse_sensitivity },
                    { "invert_mouse", invert_mouse },
                    { "collision", collision },
                    { "transition_time", transition_time }
                };
                File.WriteAllText(file_path, Json.Serialize(values));
            }
            catch (Exception exception)
            {
                Debug.LogError("[camera_pov] Couldn't write " + file_path + ": " + exception);
            }
        }

        // MiniJSON reads whole numbers as long and the rest as double.
        private static float read_float(Dictionary<string, object> values, string key, float fallback)
        {
            if (!values.TryGetValue(key, out object value))
            {
                return fallback;
            }
            switch (value)
            {
                case double number:
                    return (float)number;
                case long whole:
                    return whole;
                default:
                    return fallback;
            }
        }
    }
}
