using System.Reflection;
using HarmonyLib;
using Moona;
using UnityEngine;

namespace camera_pov
{
    // The game's audio library places sounds by where they are on screen, which suits the view from above: what is on
    // screen plays at full volume and what is off it fades with how far off it is. In first person that turns a ride's
    // music up and down as it spins in and out of view, and mirrors sounds behind the camera onto the screen. While
    // one of the mod's modes is on, sounds are placed by their real distance and side instead.
    [HarmonyPatch]
    internal static class flat_panning_patch
    {
        // Set by the rig while it has the camera.
        public static Camera camera;

        // In tiles: sounds this close play at full volume, and fade out over the next fade_distance.
        private const float full_volume_distance = 3f;
        private const float fade_distance = 30f;
        // Sounds to one side are panned only this much of the way, so nothing plays in one ear alone.
        private const float pan_amount = 0.7f;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(MFlatPanning), "UpdateFlatPosition");
        }

        private static bool Prefix(MInstance target, Vector3 position, ref MInstance.FunctionResult __result)
        {
            if (camera == null)
            {
                return true;
            }

            Transform listener = camera.transform;
            Vector3 offset = position - listener.position;
            float tiles = offset.magnitude;
            double distance = Mathf.Clamp01((tiles - full_volume_distance) / fade_distance) * MManager.Config.FlatMaxDistance;
            float side = tiles > 0.001f ? Vector3.Dot(offset / tiles, listener.right) : 0f;
            float pan = MFlatPanning.ViewportToPan(0.5f + 0.5f * side * pan_amount);

            MInstanceParameters parameters = MRtpcManager.GetInstanceParameter(target);
            if (parameters.Distance == distance && parameters.Pan == pan)
            {
                __result = MInstance.FunctionResult.NoActionNeeded;
                return false;
            }
            parameters.Distance = distance;
            parameters.Pan = pan;
            parameters.IsDirty = true;
            __result = MInstance.FunctionResult.UpdateRequested;
            return false;
        }
    }
}
