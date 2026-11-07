using System.Collections;
using System.Collections.Generic;
using Parkitect.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace camera_pov
{
    // Takes the main camera from the game's controller and walks or flies it around the park in first person. The
    // controller is only disabled, so its own state (focus point, zoom, rotation) is untouched and the camera goes back
    // to exactly where it was.
    internal sealed class camera_rig : MonoBehaviour
    {
        // The game's surfaces to stand on: terrain and paths. Buildings and scenery are walked through.
        private const int ground_mask = LayerMasks.TERRAIN | LayerMasks.PATHS;


        private const float near_clip = 0.05f;
        private const float min_far_clip = 800f;
        private const float max_pitch = 85f;

        // In tiles and seconds. A tile is roughly 2.5 m next to a guest, so these are a brisk walk and a small hop.
        private const float default_walk_speed = 1.6f;
        private const float min_walk_speed = 0.4f;
        private const float max_walk_speed = 12f;
        private const float run_multiplier = 2.5f;
        private const float gravity = 6f;
        private const float jump_speed = 2.2f;

        // Ground up to this far above the feet is stepped onto, which covers stairs and sloped paths. Higher ground,
        // such as a cliff, is climbed at rise_speed so the camera never ends up inside the terrain.
        private const float step_height = 0.6f;
        private const float rise_speed = 4f;
        // Ground this far below the feet is followed while walking, so going down a slope doesn't turn into a fall.
        private const float stick_distance = 0.3f;

        private const float default_fly_speed = 4f;
        private const float min_fly_speed = 0.5f;
        private const float max_fly_speed = 60f;
        private const float fly_run_multiplier = 3f;

        // In tiles, for a person of normal size. The camera goes on the eyes the game draws on the face, just in front of
        // them. A person without a face (frog mode) gets the head's pivot, which sits lower, and the larger offset.
        private const float eyes_offset = 0.02f;
        private const float head_pivot_offset = 0.1f;

        // Person.eyes is protected. It is the face's "eyes" mesh, placed on the front of the head.
        private static readonly System.Reflection.FieldInfo eyes_field = typeof(Person).GetField("eyes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private Renderer eyes_renderer;
        // How quickly the view follows the possessed person's turns, per second. Lower is smoother and lags more.
        private const float body_follow_rate = 10f;

        private enum state { off, entering, walking, flying, possessing, riding, leaving }

        // The person whose eyes the camera is in, and where their head and body are, smoothed.
        private Person target;
        private Vector3 head_point;
        private Quaternion body_rotation;

        // The ride being ridden, which seat of which car the camera sits in, and the seat's turn, smoothed lightly to
        // take the shake out of a coaster's track.
        private Attraction ride;
        private Seat seat;
        private int car_index;
        private int seat_index;
        private Quaternion seat_rotation;
        // A seated rider's eyes, in tiles above the seat. The same offset as the game's unused ride camera.
        private const float seat_eye_height = 0.4f;
        private const float seat_follow_rate = 20f;

        // The audio mixes world sounds by the camera's zoom, louder when close. In first person the camera is as close as
        // it gets, so the mix is set to that and the game's own level goes back on the way out.
        private static readonly System.Reflection.FieldInfo audio_zoom_field = typeof(AudioController).GetField("zoomLevel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private float saved_audio_zoom = -1f;

        private state current = state.off;
        private Camera camera;

        // What the camera looked like before, and the components that stopped moving it, to put everything back.
        private Vector3 saved_position;
        private Quaternion saved_rotation;
        private float saved_fov;
        private float saved_near;
        private float saved_far;
        private CursorLockMode saved_lock;
        private bool saved_cursor_visible;
        private readonly List<Behaviour> disabled_behaviours = new List<Behaviour>();

        private float yaw;
        private float pitch;
        private float feet;
        private float vertical_speed;
        private bool grounded;
        private float fly_speed = default_fly_speed;
        private float walk_speed = default_walk_speed;

        // The mouse wheel speeds up or slows down walking and flying, each kept apart, unless the mouse is over a window.
        private static float wheel_speed(float speed, float min, float max)
        {
            float wheel = Input.mouseScrollDelta.y;
            if (wheel == 0f || UIUtility.isMouseOverUIElement())
            {
                return speed;
            }
            return Mathf.Clamp(speed * (1f + 0.15f * wheel), min, max);
        }

        private bool active => current != state.off;

        private void Update()
        {
            GameController game = GameController.Instance;
            if (game == null || game.park == null || game.isLoadingGame)
            {
                // The park went away under the camera, so there is nothing to walk on.
                if (active)
                {
                    restore_now();
                }
                return;
            }

            bool typing = UIUtility.isInputFieldFocused();
            if (!typing && InputManager.getKeyDown(camera_pov_mod.first_person_hotkey_id))
            {
                if (current == state.possessing || current == state.riding)
                {
                    leave();
                }
                else if (current == state.off || current == state.walking || current == state.flying)
                {
                    // A selected person is possessed and a selected ride is ridden; without either, the key walks from
                    // the normal camera and goes back to it from walking or flying.
                    SerializedMonoBehaviour selected = selected_object.find(out UIWindowFrame window);
                    if (selected is Person person)
                    {
                        possess(game, person);
                        put_window_away(selected, window);
                    }
                    else if (selected is Attraction attraction)
                    {
                        start_riding(game, attraction);
                        put_window_away(selected, window);
                    }
                    else if (current == state.off)
                    {
                        enter(game);
                    }
                    else
                    {
                        leave();
                    }
                }
            }

            if (current != state.walking && current != state.flying && current != state.possessing && current != state.riding)
            {
                return;
            }
            if (camera == null)
            {
                restore_now();
                return;
            }

            if (current == state.possessing)
            {
                update_possessing(typing);
                return;
            }
            if (current == state.riding)
            {
                update_riding(typing);
                return;
            }

            float delta = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            if (!typing && InputManager.getKeyDown(camera_pov_mod.fly_hotkey_id))
            {
                current = current == state.flying ? state.walking : state.flying;
                vertical_speed = 0f;
                grounded = false;
            }

            // Kept with the settings, so the choice lasts. A note under the dot says which way it went.
            if (!typing && InputManager.getKeyDown(camera_pov_mod.collision_hotkey_id))
            {
                camera_pov_settings.collision = !camera_pov_settings.collision;
                camera_pov_settings.save();
                show_note(camera_pov_settings.collision ? texts.collision_on : texts.collision_off);
            }

            bool free_cursor = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            lock_cursor(!free_cursor);
            if (!free_cursor)
            {
                look();
            }

            Vector3 position = camera.transform.position;
            if (current == state.flying)
            {
                position = fly(position, delta, typing);
            }
            else
            {
                position = walk(position, delta, typing);
            }
            camera.transform.SetPositionAndRotation(position, Quaternion.Euler(pitch, yaw, 0f));
        }

        private void OnDestroy()
        {
            if (active)
            {
                restore_now();
            }
            if (dot_texture != null)
            {
                Destroy(dot_texture);
            }
        }

        // With the cursor locked the game reads the mouse at the middle of the screen, so hover tooltips and clicks act
        // on whatever is there. A small dot shows that spot.
        private Texture2D dot_texture;
        private const float dot_size = 6f;

        private void OnGUI()
        {
            if ((current != state.walking && current != state.flying && current != state.possessing && current != state.riding) || Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (Time.unscaledTime < note_until && !string.IsNullOrEmpty(note))
            {
                draw_note();
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }
            if (dot_texture == null)
            {
                dot_texture = make_dot_texture();
            }
            GUI.DrawTexture(new Rect((Screen.width - dot_size) / 2f, (Screen.height - dot_size) / 2f, dot_size, dot_size), dot_texture);
        }

        // A short note under the dot, such as when collision is switched.
        private const float note_duration = 1.5f;
        private string note;
        private float note_until;
        private GUIStyle note_style;
        private GUIStyle note_shadow_style;

        private void show_note(string text)
        {
            note = text;
            note_until = Time.unscaledTime + note_duration;
        }

        private void draw_note()
        {
            if (note_style == null)
            {
                note_style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold };
                note_style.normal.textColor = Color.white;
                note_shadow_style = new GUIStyle(note_style);
                note_shadow_style.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            }
            Rect area = new Rect(0f, Screen.height / 2f + 18f, Screen.width, 30f);
            GUI.Label(new Rect(area.x + 1f, area.y + 1f, area.width, area.height), note, note_shadow_style);
            GUI.Label(area, note, note_style);
        }

        // A white dot with a dark rim, so it shows on the sky and on the ground alike.
        private static Texture2D make_dot_texture()
        {
            const int size = 12;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.filterMode = FilterMode.Bilinear;
            float center = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    Color color = distance <= 3.5f ? new Color(1f, 1f, 1f, 0.9f)
                        : distance <= 5.5f ? new Color(0f, 0f, 0f, 0.45f)
                        : Color.clear;
                    texture.SetPixel(x, y, color);
                }
            }
            texture.Apply();
            return texture;
        }

        // Saves the camera as it is and stops everything else from moving it. False when there is no camera to take.
        private bool take_camera(GameController game)
        {
            // The game's camera control lock is not checked: the Perspective Camera mod holds it the whole time its
            // camera is on, to keep the game's controller still.
            camera = Camera.main;
            if (camera == null)
            {
                return false;
            }

            Transform camera_transform = camera.transform;
            saved_position = camera_transform.position;
            saved_rotation = camera_transform.rotation;
            saved_fov = camera.fieldOfView;
            saved_near = camera.nearClipPlane;
            saved_far = camera.farClipPlane;
            saved_lock = Cursor.lockState;
            saved_cursor_visible = Cursor.visible;

            // Where the middle of the screen meets the ground, which the normal camera looks at.
            saved_focus = null;
            if (Physics.Raycast(camera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0f)), out RaycastHit focus_hit, 5000f, ground_mask, QueryTriggerInteraction.Collide))
            {
                saved_focus = focus_hit.point;
            }

            disable_camera_movers(game);
            camera.nearClipPlane = near_clip;
            camera.farClipPlane = Mathf.Max(saved_far, min_far_clip);

            // The transition in first widens the game's far, narrow view in place; see the dolly. The Perspective Camera
            // mod already has a field of view close to first person's, so it goes straight down.
            entry_dolly = jump_to_of(game.cameraController) == null && make_dolly(saved_position, saved_rotation, saved_fov, out dolly found) ? found : (dolly?)null;
            EscapeHierarchy.Instance.push(leave);
            fly_speed = default_fly_speed;
            walk_speed = default_walk_speed;

            AudioController audio = AudioController.Instance;
            if (audio != null && audio_zoom_field != null)
            {
                saved_audio_zoom = (float)audio_zoom_field.GetValue(audio);
                audio.setZoomLevel(0f);
            }
            flat_panning_patch.camera = camera;
            return true;
        }

        private void enter(GameController game)
        {
            if (!take_camera(game))
            {
                return;
            }

            // Start where the middle of the screen points at, facing the way the camera faced.
            Transform camera_transform = camera.transform;
            Vector3 start = camera_transform.position;
            if (Physics.Raycast(camera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0f)), out RaycastHit hit, 5000f, ground_mask, QueryTriggerInteraction.Collide))
            {
                start = hit.point;
            }
            else if (ground_from_above(start, out float below))
            {
                start.y = below;
            }

            feet = start.y;
            yaw = camera_transform.eulerAngles.y;
            pitch = 0f;
            vertical_speed = 0f;
            grounded = true;

            Vector3 eyes = start + Vector3.up * camera_pov_settings.eye_height;
            Quaternion facing = Quaternion.Euler(pitch, yaw, 0f);
            current = state.entering;
            StartCoroutine(move_camera(() => eyes, () => facing, camera_pov_settings.field_of_view, () =>
            {
                current = state.walking;
                lock_cursor(true);
            }, take_entry_dolly()));
        }

        // From the normal camera or from walking and flying. The view starts looking where the person looks, and the
        // mouse then turns it from there, like turning one's head.
        private void possess(GameController game, Person person)
        {
            if (current == state.off && !take_camera(game))
            {
                return;
            }

            target = person;
            Transform eyes = eyes_field != null ? eyes_field.GetValue(person) as Transform : null;
            eyes_renderer = eyes != null ? eyes.GetComponent<Renderer>() : null;
            head_point = head_of(person);
            body_scale = person.transform.lossyScale.y;
            body_rotation = person.transform.rotation;
            yaw = 0f;
            last_look_time = Time.unscaledTime;
            pitch = 0f;
            current = state.entering;
            StartCoroutine(move_camera(eye_position, view_rotation, camera_pov_settings.field_of_view, () =>
            {
                current = state.possessing;
                lock_cursor(true);
                hide_body(target);
            }, take_entry_dolly()));
        }

        // Mods such as Vary Guest Height scale the whole person, so the eyes sit that much further in front of the head.
        private float body_scale = 1f;

        // The possessed body still casts its shadow, but its meshes are not drawn, so looking down or back shows no
        // inside of a head. Only the shadow mode changes, which the game itself doesn't touch, so the game's own
        // culling, which turns renderers on and off, isn't fought with.
        private readonly Dictionary<Renderer, ShadowCastingMode> hidden_renderers = new Dictionary<Renderer, ShadowCastingMode>();
        private float next_hide_scan;
        private const float hide_scan_interval = 0.5f;

        // Run again now and then, since a person gets new renderers for what they buy, such as a hat. Only the body and
        // what is worn are hidden, the same split the info window makes for its outline: what the person holds, such
        // as an umbrella, food or a balloon, stays in view.
        // The body hidden: the possessed person's, or whoever sits in the seat the camera rides in.
        private Person hidden_person;

        private void hide_body(Person person)
        {
            next_hide_scan = Time.unscaledTime + hide_scan_interval;
            if (person != hidden_person)
            {
                show_body();
            }
            if (person == null)
            {
                return;
            }
            hidden_person = person;
            highlight_patch.possessed = person;
            foreach (Renderer renderer in person.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || hidden_renderers.ContainsKey(renderer) || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                {
                    continue;
                }
                Item item = renderer.GetComponentInParent<Item>();
                if (item != null && !(item is WearableProduct))
                {
                    continue;
                }
                hidden_renderers[renderer] = renderer.shadowCastingMode;
                renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
        }

        private void show_body()
        {
            hidden_person = null;
            highlight_patch.possessed = null;
            foreach (KeyValuePair<Renderer, ShadowCastingMode> hidden in hidden_renderers)
            {
                if (hidden.Key != null)
                {
                    hidden.Key.shadowCastingMode = hidden.Value;
                }
            }
            hidden_renderers.Clear();
        }

        // Follows the person or the seat after the game has moved and animated them for the frame.
        private void LateUpdate()
        {
            if (blending)
            {
                blend_step();
                return;
            }
            if (ride_is_set && (current == state.riding || current == state.entering))
            {
                late_update_riding();
                return;
            }
            if (target == null || (current != state.possessing && current != state.entering))
            {
                return;
            }

            // Gone from the park, fired, or put away by the game: carry on walking from where they were.
            if (!target.gameObject.activeInHierarchy)
            {
                if (current == state.possessing)
                {
                    detach(state.walking);
                }
                return;
            }

            float delta = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            body_rotation = Quaternion.Slerp(body_rotation, target.transform.rotation, 1f - Mathf.Exp(-body_follow_rate * delta));
            head_point = head_of(target);
            body_scale = target.transform.lossyScale.y;
            if (current == state.possessing && camera != null)
            {
                camera.transform.SetPositionAndRotation(eye_position(), view_rotation());
                if (Time.unscaledTime >= next_hide_scan)
                {
                    hide_body(target);
                }
            }
        }

        private void update_possessing(bool typing)
        {
            // Destroyed between LateUpdate and now.
            if (target == null)
            {
                detach(state.walking);
                return;
            }

            look_and_recenter();

            if (typing)
            {
                return;
            }
            if (InputManager.getKeyDown(camera_pov_mod.fly_hotkey_id))
            {
                detach(state.flying);
            }
            else if (InputManager.getKey("CameraMoveUp") || InputManager.getKey("CameraMoveDown") || InputManager.getKey("CameraMoveLeft") || InputManager.getKey("CameraMoveRight"))
            {
                detach(state.walking);
            }
        }

        // The mouse looks around from where the person or the seat faces, and after a while without it the view turns
        // back to that heading. Alt frees the cursor instead.
        private void look_and_recenter()
        {
            bool free_cursor = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            lock_cursor(!free_cursor);
            if (!free_cursor && look())
            {
                last_look_time = Time.unscaledTime;
            }

            if (Time.unscaledTime - last_look_time > recenter_delay)
            {
                float ease = 1f - Mathf.Exp(-recenter_rate * Mathf.Min(Time.unscaledDeltaTime, 0.1f));
                yaw = Mathf.LerpAngle(Mathf.DeltaAngle(0f, yaw), 0f, ease);
                pitch = Mathf.Lerp(pitch, 0f, ease);
            }
        }

        // Unity's null check on the ride, which is true once the ride has been demolished.
        private bool ride_is_set => !ReferenceEquals(ride, null);

        // From the normal camera or from walking and flying: the first seat of the first car. A ride without any placed
        // seat, such as a coaster with no train yet, isn't entered.
        private void start_riding(GameController game, Attraction attraction)
        {
            List<List<Seat>> cars = ride_seats.cars_of(attraction);
            if (cars.Count == 0)
            {
                ErrorMessageController.showErrorMessage(texts.no_seats);
                return;
            }
            if (current == state.off && !take_camera(game))
            {
                return;
            }

            ride = attraction;
            car_index = 0;
            seat_index = 0;
            seat = cars[0][0];
            seat_rotation = seat.transform.rotation;
            yaw = 0f;
            pitch = 0f;
            last_look_time = Time.unscaledTime;
            current = state.entering;
            StartCoroutine(move_camera(seat_eyes, seat_view, camera_pov_settings.field_of_view, () =>
            {
                current = state.riding;
                lock_cursor(true);
                hide_body(seat != null ? seat.occupiedByPerson : null);
            }, take_entry_dolly()));
        }

        private void late_update_riding()
        {
            // Demolished: carry on flying from the same spot, since the seat may have been high up.
            if (ride == null)
            {
                if (current == state.riding)
                {
                    detach(state.flying);
                }
                return;
            }
            if (seat == null || seat.transform == null)
            {
                if (!sit(car_index, seat_index))
                {
                    if (current == state.riding)
                    {
                        detach(state.flying);
                    }
                    return;
                }
            }

            float delta = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            seat_rotation = Quaternion.Slerp(seat_rotation, seat.transform.rotation, 1f - Mathf.Exp(-seat_follow_rate * delta));
            if (current == state.riding && camera != null)
            {
                camera.transform.SetPositionAndRotation(seat_eyes(), seat_view());
                // Riders get on and off at the station.
                if (Time.unscaledTime >= next_hide_scan || seat.occupiedByPerson != hidden_person)
                {
                    hide_body(seat.occupiedByPerson);
                }
            }
        }

        private void update_riding(bool typing)
        {
            look_and_recenter();
            if (typing)
            {
                return;
            }

            if (InputManager.getKeyDown(camera_pov_mod.fly_hotkey_id))
            {
                detach(state.flying);
                return;
            }

            // The wheel moves along the seats of the car, Tab to the next car, which after a train's last car is the
            // next train's first. Shift+Tab goes back.
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f)
            {
                sit(car_index, seat_index + (wheel < 0f ? 1 : -1));
            }
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                sit(car_index + (running ? -1 : 1), 0);
            }
        }

        // Sits in the given seat of the given car, both wrapping around. The seats are read again each time, since
        // trains may have been added or removed. False when the ride has no seats left.
        private bool sit(int car, int seat_number)
        {
            List<List<Seat>> cars = ride_seats.cars_of(ride);
            if (cars.Count == 0)
            {
                return false;
            }
            car_index = wrap(car, cars.Count);
            seat_index = wrap(seat_number, cars[car_index].Count);
            Seat next = cars[car_index][seat_index];
            if (next != seat)
            {
                seat = next;
                seat_rotation = seat.transform.rotation;
                hide_body(seat.occupiedByPerson);
            }
            return true;
        }

        private static int wrap(int value, int count)
        {
            return ((value % count) + count) % count;
        }

        private Vector3 seat_eyes()
        {
            if (seat == null || seat.transform == null)
            {
                return camera.transform.position;
            }
            Transform seat_transform = seat.transform;
            return seat_transform.position + seat_rotation * Vector3.up * (seat_eye_height * seat_transform.lossyScale.y);
        }

        private Quaternion seat_view()
        {
            return seat_rotation * Quaternion.Euler(pitch, yaw, 0f);
        }

        // Leaves the person's body or the seat and goes on walking or flying from the same spot, looking the same way.
        private void detach(state next)
        {
            show_body();
            // Walking or flying away on your own lets go of the selection, so its window stays closed.
            reopen_on_leave = null;
            target = null;
            ride = null;
            seat = null;
            if (camera != null)
            {
                Vector3 angles = camera.transform.rotation.eulerAngles;
                yaw = angles.y;
                pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x), -max_pitch, max_pitch);
                feet = camera.transform.position.y - camera_pov_settings.eye_height;
            }
            vertical_speed = 0f;
            grounded = false;
            current = next;
        }

        // The middle of the eyes mesh follows the head as it is animated. Its bounds stay right while the body is hidden,
        // since only the shadow mode changes.
        private Vector3 head_of(Person person)
        {
            if (eyes_renderer != null)
            {
                return eyes_renderer.bounds.center;
            }
            return person.head != null ? person.head.position : person.transform.position + Vector3.up * camera_pov_settings.eye_height;
        }

        private Vector3 eye_position()
        {
            float offset = eyes_renderer != null ? eyes_offset : head_pivot_offset;
            return head_point + body_rotation * Vector3.forward * (offset * body_scale);
        }

        private Quaternion view_rotation()
        {
            return body_rotation * Quaternion.Euler(pitch, yaw, 0f);
        }

        // Also what Esc calls, through the game's escape hierarchy.
        private void leave()
        {
            if (current != state.walking && current != state.flying && current != state.possessing && current != state.riding)
            {
                return;
            }

            EscapeHierarchy.Instance.remove(leave);
            show_body();
            target = null;
            ride = null;
            seat = null;
            current = state.leaving;
            unlock_cursor();

            // The normal camera comes back above where you are now, with the zoom and angle it had, instead of flying
            // back across the park. The controller itself works out where that is, so it carries on from there.
            CameraController controller = GameController.Instance != null ? GameController.Instance.cameraController : null;
            Vector3 here = camera != null ? camera.transform.position : saved_position;
            if (!saved_focus.HasValue || camera == null || controller == null || !ground_from_above(here, out float ground))
            {
                StartCoroutine(move_camera(() => saved_position, () => saved_rotation, saved_fov, give_back));
                return;
            }
            Vector3 focus = new Vector3(here.x, ground, here.z);

            // The Perspective Camera mod places the camera every frame from a target of its own, which its public
            // JumpTo moves without its smoothing. It gets the camera back now, and the view blends from first person
            // into what it draws, so it ends exactly where that mod puts it.
            System.Reflection.MethodInfo jump_to = jump_to_of(controller);
            if (jump_to != null)
            {
                jump_to.Invoke(controller, new object[] { focus, true });
                blend_from_position = camera.transform.position;
                blend_from_rotation = camera.transform.rotation;
                blend_from_fov = camera.fieldOfView;
                blend_elapsed = 0f;
                enable_camera_movers();
                blending = true;
                return;
            }

            // The game's controller keeps its state in the camera's position. Its focusOn places the camera above the
            // spot from where and how the camera was when the mod took it; the result is read as the destination,
            // and the first-person view is put back for the transition.
            Vector3 first_position = camera.transform.position;
            Quaternion first_rotation = camera.transform.rotation;
            float first_fov = camera.fieldOfView;
            camera.transform.SetPositionAndRotation(saved_position, saved_rotation);
            camera.fieldOfView = saved_fov;
            controller.focusOn(focus);
            Vector3 destination = camera.transform.position;
            Quaternion facing = camera.transform.rotation;
            camera.transform.SetPositionAndRotation(first_position, first_rotation);
            camera.fieldOfView = first_fov;
            camera.nearClipPlane = near_clip;
            camera.farClipPlane = Mathf.Max(saved_far, min_far_clip);
            // focusOn also sets the blur and the sound mix for its zoom, which stay off until the end.
            Behaviour tilt_shift = camera.GetComponent("TiltShift") as Behaviour;
            if (tilt_shift != null && disabled_behaviours.Contains(tilt_shift))
            {
                tilt_shift.enabled = false;
            }
            AudioController.Instance?.setZoomLevel(0f);

            // The transition goes to where that view looks the same with this mod's field of view, then narrows it in
            // place into the game's view.
            StartCoroutine(leave_into_game_camera(destination, facing));
        }

        // The move up to the stand-in and the flattening overlap: the dolly starts when the move is most of the way
        // there, and both end together, so it reads as one motion.
        private IEnumerator leave_into_game_camera(Vector3 destination, Quaternion facing)
        {
            if (!make_dolly(destination, facing, saved_fov, out dolly view))
            {
                yield return move_camera(() => destination, () => facing, saved_fov, () => { });
            }
            else
            {
                float wide = camera_pov_settings.field_of_view;
                Vector3 stand_in = view.pose_for(wide);
                Vector3 from_position = camera.transform.position;
                Quaternion from_rotation = camera.transform.rotation;
                float from_fov = camera.fieldOfView;
                float dolly_start = move_time * (1f - dolly_overlap);
                float end = dolly_start + dolly_time;

                for (float elapsed = 0f; elapsed < end; elapsed += Time.unscaledDeltaTime)
                {
                    if (camera == null)
                    {
                        yield break;
                    }
                    float move = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / move_time));
                    float flatten = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - dolly_start) / dolly_time));
                    float fov = flatten > 0f ? dolly_fov(wide, saved_fov, flatten) : Mathf.Lerp(from_fov, wide, move);
                    Vector3 position = Vector3.Lerp(from_position, stand_in, move) + (view.pose_for(Mathf.Min(fov, wide)) - stand_in);
                    place(position, Quaternion.Slerp(from_rotation, facing, move), fov, view.spot);
                    yield return null;
                }
            }
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(destination, facing);
                camera.fieldOfView = saved_fov;
            }
            give_back();
        }

        // The ground point the normal camera looked at when the mod took it.
        private Vector3? saved_focus;

        // The Perspective Camera mod's public JumpTo, which tells it apart from the game's own controller.
        private static System.Reflection.MethodInfo jump_to_of(CameraController controller)
        {
            return controller != null ? controller.GetType().GetMethod("JumpTo", new[] { typeof(Vector3), typeof(bool) }) : null;
        }

        // The game's camera is far away with a narrow field of view, almost isometric, and first person is close with
        // a wide one. Between the two, the field of view changes while the camera slides along its line of sight so
        // that the spot in the middle keeps its size: the picture flattens or deepens in place instead of the camera
        // swooping across the park.
        private struct dolly
        {
            public Vector3 spot;
            public Vector3 forward;
            public Quaternion rotation;
            public float distance;
            public float fov;

            // Where the camera goes for a given field of view, keeping the spot the same size on screen.
            public Vector3 pose_for(float other_fov)
            {
                float scale = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(other_fov * 0.5f * Mathf.Deg2Rad);
                return spot - forward * (distance * scale);
            }
        }

        private dolly? entry_dolly;

        // The transition time from the settings is shared between the move and the dolly, which overlap by this much
        // of the move, so the whole thing takes the time set.
        private const float dolly_overlap = 0.4f;
        private static float move_time => camera_pov_settings.transition_time * 0.533f;
        private static float dolly_time => camera_pov_settings.transition_time - move_time * (1f - dolly_overlap);

        // The field of view part of the way through a dolly, changing evenly in how wide the picture is, which keeps
        // the motion steady.
        private static float dolly_fov(float from_fov, float to_fov, float t)
        {
            float from_width = Mathf.Tan(from_fov * 0.5f * Mathf.Deg2Rad);
            float to_width = Mathf.Tan(to_fov * 0.5f * Mathf.Deg2Rad);
            return 2f * Mathf.Atan(Mathf.Lerp(from_width, to_width, t)) * Mathf.Rad2Deg;
        }

        // Far from the ground, the near plane moves out with the camera, as the game's own does, so the depth of the
        // far view stays precise and nothing flickers.
        private void place(Vector3 position, Quaternion rotation, float fov, Vector3 spot)
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = fov;
            camera.nearClipPlane = Mathf.Max(near_clip, Vector3.Distance(position, spot) * 0.02f);
        }

        // Taken by the first transition in, so only a way in from the normal camera starts with the dolly.
        private dolly? take_entry_dolly()
        {
            dolly? view = entry_dolly;
            entry_dolly = null;
            return view;
        }

        // Only for a view narrower than first person's that looks at the ground.
        private bool make_dolly(Vector3 position, Quaternion rotation, float fov, out dolly view)
        {
            view = default;
            Vector3 forward = rotation * Vector3.forward;
            if (fov >= camera_pov_settings.field_of_view - 1f || !Physics.Raycast(position, forward, out RaycastHit hit, 5000f, ground_mask, QueryTriggerInteraction.Collide))
            {
                return false;
            }
            view = new dolly { spot = hit.point, forward = forward, rotation = rotation, distance = hit.distance, fov = fov };
            return true;
        }


        // Leaving into the Perspective Camera mod: the view moves from where first person was to where that mod puts
        // the camera this frame, which it does in Update, before this LateUpdate.
        private bool blending;
        private float blend_elapsed;
        private Vector3 blend_from_position;
        private Quaternion blend_from_rotation;
        private float blend_from_fov;

        private void blend_step()
        {
            if (camera == null)
            {
                blending = false;
                give_back();
                return;
            }
            blend_elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(blend_elapsed / move_time));
            Transform camera_transform = camera.transform;
            if (t < 1f)
            {
                camera_transform.SetPositionAndRotation(Vector3.Lerp(blend_from_position, camera_transform.position, t), Quaternion.Slerp(blend_from_rotation, camera_transform.rotation, t));
                camera.fieldOfView = Mathf.Lerp(blend_from_fov, saved_fov, t);
                return;
            }
            camera.fieldOfView = saved_fov;
            blending = false;
            give_back();
        }

        private void enable_camera_movers()
        {
            foreach (Behaviour behaviour in disabled_behaviours)
            {
                if (behaviour != null)
                {
                    behaviour.enabled = true;
                }
            }
            disabled_behaviours.Clear();
        }

        // Without the animation, for when the park or the mod goes away.
        private void restore_now()
        {
            StopAllCoroutines();
            blending = false;
            reopen_on_leave = null;
            if (EscapeHierarchy.Instance != null)
            {
                EscapeHierarchy.Instance.remove(leave);
            }
            unlock_cursor();
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(saved_position, saved_rotation);
                camera.fieldOfView = saved_fov;
            }
            give_back();
        }

        private void give_back()
        {
            if (camera != null)
            {
                camera.nearClipPlane = saved_near;
                camera.farClipPlane = saved_far;
            }
            enable_camera_movers();
            show_body();
            if (saved_audio_zoom >= 0f && AudioController.Instance != null)
            {
                AudioController.Instance.setZoomLevel(saved_audio_zoom);
            }
            saved_audio_zoom = -1f;
            flat_panning_patch.camera = null;
            camera = null;
            target = null;
            ride = null;
            seat = null;
            current = state.off;

            // Back at the normal camera, the person or ride you went into is selected again, if it is still there.
            SerializedMonoBehaviour reopen = reopen_on_leave;
            reopen_on_leave = null;
            if (reopen != null && reopen.gameObject.activeInHierarchy)
            {
                reopen.onOpenInfoWindow();
            }
        }

        // The selection's window covers the view and outlines what it shows, a whole ride included, so it is closed
        // on the way in, and opened again on the way back to the normal camera.
        private SerializedMonoBehaviour reopen_on_leave;

        private void put_window_away(SerializedMonoBehaviour selected, UIWindowFrame window)
        {
            // A ride without seats isn't entered, and keeps its window.
            if (current != state.entering || window == null)
            {
                return;
            }
            reopen_on_leave = selected;
            window.close();
        }

        // The game's controller, plus anything else on the camera that moves it, such as the Perspective Camera mod's
        // controller and its key and mouse handlers. The tilt-shift blur is for the view from above.
        private void disable_camera_movers(GameController game)
        {
            disabled_behaviours.Clear();
            disable(game.cameraController);
            foreach (MonoBehaviour behaviour in camera.GetComponents<MonoBehaviour>())
            {
                if (behaviour is CameraController || (behaviour != null && behaviour.GetType().Namespace == "PerspectiveCamera"))
                {
                    disable(behaviour);
                }
            }
            disable(camera.GetComponent("TiltShift") as Behaviour);
        }

        private void disable(Behaviour behaviour)
        {
            if (behaviour != null && behaviour.enabled && !disabled_behaviours.Contains(behaviour))
            {
                behaviour.enabled = false;
                disabled_behaviours.Add(behaviour);
            }
        }

        // The destination is read every frame, so a transition into a walking person lands on them. Coming in from
        // the normal camera, the dolly deepens its view in place first, and the move down starts before it is done.
        private IEnumerator move_camera(System.Func<Vector3> destination, System.Func<Quaternion> facing, float fov, System.Action done, dolly? into = null)
        {
            Transform camera_transform = camera.transform;
            Vector3 from_position = camera_transform.position;
            Quaternion from_rotation = camera_transform.rotation;
            float from_fov = camera.fieldOfView;
            float move_start = 0f;
            float end = move_time;
            Vector3 stand_in = from_position;
            if (into.HasValue)
            {
                stand_in = into.Value.pose_for(fov);
                from_position = stand_in;
                from_rotation = into.Value.rotation;
                from_fov = fov;
                move_start = Mathf.Max(0f, dolly_time - move_time * dolly_overlap);
                end = Mathf.Max(dolly_time, move_start + move_time);
            }

            for (float elapsed = 0f; elapsed < end; elapsed += Time.unscaledDeltaTime)
            {
                if (camera == null)
                {
                    yield break;
                }
                float move = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - move_start) / move_time));
                Vector3 position = Vector3.Lerp(from_position, destination(), move);
                Quaternion rotation = Quaternion.Slerp(from_rotation, facing(), move);
                if (into.HasValue)
                {
                    float deepen = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dolly_time));
                    float current_fov = dolly_fov(into.Value.fov, fov, deepen);
                    place(position + (into.Value.pose_for(current_fov) - stand_in), rotation, current_fov, into.Value.spot);
                }
                else
                {
                    camera_transform.SetPositionAndRotation(position, rotation);
                    camera.fieldOfView = Mathf.Lerp(from_fov, fov, move);
                }
                yield return null;
            }

            if (camera != null)
            {
                camera_transform.SetPositionAndRotation(destination(), facing());
                camera.fieldOfView = fov;
                if (into.HasValue)
                {
                    camera.nearClipPlane = near_clip;
                }
            }
            done();
        }

        // Whether the mouse moved.
        private bool look()
        {
            float sensitivity = camera_pov_settings.mouse_sensitivity;
            float horizontal = Input.GetAxisRaw("Mouse X");
            float vertical = Input.GetAxisRaw("Mouse Y");
            yaw += horizontal * sensitivity;
            pitch += (camera_pov_settings.invert_mouse ? vertical : -vertical) * sensitivity;
            pitch = Mathf.Clamp(pitch, -max_pitch, max_pitch);
            return horizontal != 0f || vertical != 0f;
        }

        // Seeing through someone's eyes, the view turns back to their heading after this long without the mouse.
        private const float recenter_delay = 2f;
        // Per second; at 4 most of the way back takes about half a second.
        private const float recenter_rate = 4f;
        private float last_look_time;

        // The camera keys from the game's controls, so a remapped WASD still works.
        private Vector3 move_input(Vector3 forward, Vector3 right, bool typing)
        {
            Vector3 direction = Vector3.zero;
            if (typing)
            {
                return direction;
            }
            if (InputManager.getKey("CameraMoveUp"))
            {
                direction += forward;
            }
            if (InputManager.getKey("CameraMoveDown"))
            {
                direction -= forward;
            }
            if (InputManager.getKey("CameraMoveRight"))
            {
                direction += right;
            }
            if (InputManager.getKey("CameraMoveLeft"))
            {
                direction -= right;
            }
            return direction;
        }

        private static bool running => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        private Vector3 walk(Vector3 position, float delta, bool typing)
        {
            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            Vector3 direction = move_input(heading * Vector3.forward, heading * Vector3.right, typing);
            walk_speed = wheel_speed(walk_speed, min_walk_speed, max_walk_speed);
            float speed = walk_speed * (running ? run_multiplier : 1f);
            Vector3 move = direction.normalized * speed * delta;
            bool collide = camera_pov_settings.collision;
            if (collide)
            {
                move = walk_collision.slide(new Vector3(position.x, feet, position.z), move, camera_pov_settings.eye_height);
            }
            position += move;

            if (ground_below(position, collide, out float ground))
            {
                if (grounded)
                {
                    if (ground > feet)
                    {
                        feet = Mathf.MoveTowards(feet, ground, rise_speed * delta);
                    }
                    else if (feet - ground <= stick_distance)
                    {
                        feet = ground;
                    }
                    else
                    {
                        // Walked off a ledge.
                        grounded = false;
                        vertical_speed = 0f;
                    }
                }

                if (grounded && !typing && InputManager.getKeyDown(camera_pov_mod.jump_hotkey_id))
                {
                    grounded = false;
                    vertical_speed = jump_speed;
                }

                if (!grounded)
                {
                    vertical_speed -= gravity * delta;
                    // A jump stops where the head meets a ceiling.
                    if (collide && vertical_speed > 0f && walk_collision.ceiling(new Vector3(position.x, feet + camera_pov_settings.eye_height, position.z), vertical_speed * delta))
                    {
                        vertical_speed = 0f;
                    }
                    feet += vertical_speed * delta;
                    if (feet <= ground)
                    {
                        feet = ground;
                        vertical_speed = 0f;
                        grounded = true;
                    }
                }
            }

            position.y = feet + camera_pov_settings.eye_height;
            return position;
        }

        private Vector3 fly(Vector3 position, float delta, bool typing)
        {
            Transform camera_transform = camera.transform;
            Vector3 direction = move_input(camera_transform.forward, camera_transform.right, typing);
            if (!typing)
            {
                if (InputManager.getKey("CameraRotateRight") || InputManager.getKey(camera_pov_mod.jump_hotkey_id))
                {
                    direction += Vector3.up;
                }
                if (InputManager.getKey("CameraRotateLeft") || Input.GetKey(KeyCode.LeftControl))
                {
                    direction -= Vector3.up;
                }
            }

            fly_speed = wheel_speed(fly_speed, min_fly_speed, max_fly_speed);

            position += direction.normalized * fly_speed * (running ? fly_run_multiplier : 1f) * delta;

            // Never below what one could stand on, so landing works by switching back to walking. Flying goes through
            // everything built, so only the terrain and paths count here.
            if (ground_below(position, false, out float ground))
            {
                position.y = Mathf.Max(position.y, ground + camera_pov_settings.eye_height * 0.5f);
            }
            feet = position.y - camera_pov_settings.eye_height;
            return position;
        }

        // The highest surface at most step_height above the feet, or, when the feet are inside a hill, its top. With
        // built objects included, a floor, bridge or roof just above the feet or anywhere below them counts too, when
        // it is higher than the terrain or path under it.
        private bool ground_below(Vector3 position, bool include_built, out float ground)
        {
            Vector3 origin = new Vector3(position.x, feet + step_height, position.z);
            bool found;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2000f, ground_mask, QueryTriggerInteraction.Collide))
            {
                ground = hit.point.y;
                found = true;
            }
            else
            {
                found = ground_from_above(position, out ground);
            }

            if (include_built && walk_collision.floor(position, feet + walk_collision.object_step, out float built) && (!found || built > ground))
            {
                ground = built;
                found = true;
            }
            return found;
        }

        private static bool ground_from_above(Vector3 position, out float ground)
        {
            if (Physics.Raycast(new Vector3(position.x, 2000f, position.z), Vector3.down, out RaycastHit hit, 4000f, ground_mask, QueryTriggerInteraction.Collide))
            {
                ground = hit.point.y;
                return true;
            }
            ground = 0f;
            return false;
        }

        private static void lock_cursor(bool locked)
        {
            if (locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = Settings.Instance.controlsLockCursor ? CursorLockMode.Confined : CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void unlock_cursor()
        {
            Cursor.lockState = saved_lock;
            Cursor.visible = saved_cursor_visible;
        }
    }
}
