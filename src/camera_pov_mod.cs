using HarmonyLib;
using UnityEngine;

namespace camera_pov
{
    // Entry point. The game discovers this type through GetExportedTypes, so it is the only public type in the assembly.
    public class camera_pov_mod : AbstractMod, IModSettings
    {
        internal const string identifier = "xupisco@CameraPOV";
        internal const string first_person_hotkey_id = identifier + "/first_person";
        internal const string fly_hotkey_id = identifier + "/fly";
        internal const string jump_hotkey_id = identifier + "/jump";
        internal const string collision_hotkey_id = identifier + "/collision";
        private Harmony harmony;
        private KeyGroup key_group;
        private KeyMapping[] key_mappings;
        private GameObject behaviour_object;

        public override string getName()
        {
            return "POV Cam";
        }

        public override string getDescription()
        {
            return texts.mod_description;
        }

        public override string getVersionNumber()
        {
            return "0.10.1";
        }

        public override string getIdentifier()
        {
            return identifier;
        }

        // The camera is each player's own, so nothing here reaches the other players.
        public override bool isMultiplayerModeCompatible()
        {
            return true;
        }

        public override bool isRequiredByAllPlayersInMultiplayerMode()
        {
            return false;
        }

        public override void onEnabled()
        {
            camera_pov_settings.load();
            harmony = new Harmony(identifier);
            harmony.PatchAll(typeof(camera_pov_mod).Assembly);
            register_hotkeys();
            behaviour_object = new GameObject("camera_pov");
            Object.DontDestroyOnLoad(behaviour_object);
            behaviour_object.AddComponent<camera_rig>();
        }

        public override void onDisabled()
        {
            if (behaviour_object != null)
            {
                // The rig gives the camera back to the game in OnDestroy.
                Object.Destroy(behaviour_object);
                behaviour_object = null;
            }
            unregister_hotkeys();
            harmony?.UnpatchAll(identifier);
            harmony = null;
        }

        public void onSettingsOpened()
        {
            camera_pov_settings.load();
        }

        public void onSettingsClosed()
        {
            camera_pov_settings.save();
        }

        public void onDrawSettingsUI()
        {
            GUILayout.BeginVertical();
            camera_pov_settings.eye_height = slider(texts.eye_height_label, camera_pov_settings.eye_height, 0.3f, 1.5f, 0.05f, "0.00");
            camera_pov_settings.field_of_view = slider(texts.field_of_view_label, camera_pov_settings.field_of_view, 50f, 100f, 1f, "0");
            camera_pov_settings.mouse_sensitivity = slider(texts.mouse_sensitivity_label, camera_pov_settings.mouse_sensitivity, 0.5f, 6f, 0.1f, "0.0");
            camera_pov_settings.transition_time = slider(texts.transition_time_label, camera_pov_settings.transition_time, 0.3f, 2f, 0.05f, "0.00");
            camera_pov_settings.invert_mouse = GUILayout.Toggle(camera_pov_settings.invert_mouse, " " + texts.invert_mouse_label);
            camera_pov_settings.collision = GUILayout.Toggle(camera_pov_settings.collision, " " + texts.collision_label);
            GUILayout.Space(8f);
            GUILayout.Label(texts.controls_hint);
            GUILayout.EndVertical();
        }

        private static float slider(string label, float value, float min, float max, float step, string format)
        {
            GUILayout.Label(label + ": " + value.ToString(format));
            float moved = GUILayout.HorizontalSlider(value, min, max);
            return Mathf.Round(moved / step) * step;
        }

        // The group is registered explicitly, because registering one that already exists makes the game throw, which
        // the implicit per-mod group would hit when the mod is enabled again.
        private void register_hotkeys()
        {
            InputManager input = ScriptableSingleton<InputManager>.Instance;

            key_group = new KeyGroup(identifier);
            key_group.keyGroupName = getName();
            input.registerKeyGroup(key_group);

            // The controls settings show these as given, in the language the game had when the mod was enabled.
            key_mappings = new[]
            {
                key_mapping(first_person_hotkey_id, KeyCode.G, texts.first_person_key_name, texts.first_person_key_description),
                key_mapping(fly_hotkey_id, KeyCode.F, texts.fly_key_name, texts.fly_key_description),
                key_mapping(jump_hotkey_id, KeyCode.Space, texts.jump_key_name, texts.jump_key_description),
                key_mapping(collision_hotkey_id, KeyCode.Tab, texts.collision_key_name, texts.collision_key_description),            };
            foreach (KeyMapping mapping in key_mappings)
            {
                input.registerKeyMapping(mapping);
            }
        }

        private static KeyMapping key_mapping(string id, KeyCode key, string name, string description)
        {
            KeyMapping mapping = new KeyMapping(id, key, KeyCode.None);
            mapping.keyName = name;
            mapping.keyDescription = description;
            mapping.keyGroupIdentifier = identifier;
            return mapping;
        }

        private void unregister_hotkeys()
        {
            InputManager input = ScriptableSingleton<InputManager>.Instance;
            foreach (KeyMapping mapping in key_mappings)
            {
                input.unregisterKeyMapping(mapping);
            }
            input.unregisterKeyGroup(key_group);
            key_mappings = null;
            key_group = null;
        }
    }
}
