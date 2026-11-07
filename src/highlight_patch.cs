using HarmonyLib;

namespace camera_pov
{
    // A person's info window outlines them every frame unless Person.shouldNotBeHighlighted says otherwise, which the
    // game uses for guests hidden inside a ride. The possessed person answers the same way, so seeing through their
    // eyes doesn't show the outline of their own body. The window puts the outline back as soon as this is false again.
    [HarmonyPatch(typeof(Person), nameof(Person.shouldNotBeHighlighted), MethodType.Getter)]
    internal static class highlight_patch
    {
        // Set by the rig while it is in someone's eyes.
        public static Person possessed;

        private static void Postfix(Person __instance, ref bool __result)
        {
            if (possessed != null && ReferenceEquals(__instance, possessed))
            {
                __result = true;
            }
        }
    }
}
