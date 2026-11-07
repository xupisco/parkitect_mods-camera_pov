using UnityEngine;

namespace camera_pov
{
    // Collision while walking, against what the player has built. Buildings, scenery, rides, tracks, fences and path
    // items have no physics colliders; the game keeps their meshes in its own mouse collision structure instead. Its
    // ray test only sees triangles from the front, which lets thin fences through from one side, so walls are found
    // with its box test instead, which checks the triangles themselves from both sides. Terrain and paths stay with
    // the physics engine, as before.
    internal static class walk_collision
    {
        // In tiles: half the body's width, and where the body starts above the feet. Anything lower than the knee,
        // such as flowers, a low border, stairs or the rise of a ramp, is walked over.
        private const float radius = 0.12f;
        private const float knee_height = 0.25f;
        // A built surface no more than this far above the feet is stepped onto, about a step of a stair.
        public const float object_step = 0.12f;
        private const float floor_min_normal_y = 0.7f;

        private static MouseCollisions collisions
        {
            get
            {
                MouseCollisions instance = MouseCollisions.Instance;
                return instance != null && instance.octree != null ? instance : null;
            }
        }

        // The part of a horizontal move that doesn't run into something. Blocked moves try each axis on its own, so
        // walking into a fence at an angle slides along it, since the game lines things up with its grid. When the
        // body already touches something, such as after entering inside a building, every move is let through until
        // it is free again.
        public static Vector3 slide(Vector3 feet_position, Vector3 move, float eye_height)
        {
            if (move.sqrMagnitude < 1e-10f || collisions == null || touches(feet_position, eye_height))
            {
                return move;
            }
            if (!touches(feet_position + move, eye_height))
            {
                return move;
            }

            Vector3 along_x = new Vector3(move.x, 0f, 0f);
            Vector3 along_z = new Vector3(0f, 0f, move.z);
            Vector3 first = Mathf.Abs(move.x) >= Mathf.Abs(move.z) ? along_x : along_z;
            Vector3 second = first == along_x ? along_z : along_x;
            if (first.sqrMagnitude > 1e-10f && !touches(feet_position + first, eye_height))
            {
                return first;
            }
            if (second.sqrMagnitude > 1e-10f && !touches(feet_position + second, eye_height))
            {
                return second;
            }
            return Vector3.zero;
        }

        // Whether the body, from the knee up to just above the eyes, touches anything built.
        private static bool touches(Vector3 feet_position, float eye_height)
        {
            float top = eye_height + 0.05f;
            Vector3 center = new Vector3(feet_position.x, feet_position.y + (knee_height + top) / 2f, feet_position.z);
            return touches(new Bounds(center, new Vector3(radius * 2f, top - knee_height, radius * 2f)));
        }

        private static bool touches(Bounds box)
        {
            MouseCollisions instance = collisions;
            if (instance == null)
            {
                return false;
            }
            foreach (MouseCollider.HitInfo hit in instance.boxcastAll(box))
            {
                if (hit.hitSomething)
                {
                    return true;
                }
            }
            return false;
        }

        // Whether something built is right above the head, within the given distance.
        public static bool ceiling(Vector3 head, float distance)
        {
            float height = distance + 0.05f;
            Vector3 center = new Vector3(head.x, head.y + 0.01f + height / 2f, head.z);
            return touches(new Bounds(center, new Vector3(radius * 2f, height, radius * 2f)));
        }

        // The highest flat built surface below the given height, such as a floor, a bridge or a roof. A ray down finds
        // the top of things, which is the side the game's ray test sees.
        public static bool floor(Vector3 position, float from_height, out float height)
        {
            height = 0f;
            MouseCollisions instance = collisions;
            if (instance == null)
            {
                return false;
            }
            MouseCollider.HitInfo hit = instance.raycast(new Ray(new Vector3(position.x, from_height, position.z), Vector3.down), 50f);
            if (!hit.hitSomething || Mathf.Abs(hit.hitNormal.y) < floor_min_normal_y)
            {
                return false;
            }
            height = hit.hitPosition.y;
            return true;
        }
    }
}
