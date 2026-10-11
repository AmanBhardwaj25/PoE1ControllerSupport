using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PoEController.World
{
    /// <summary>
    /// Analog camera panning and the controller's follow-camera toggle, both just before <c>CameraControl.DoUpdate</c>.
    /// <list type="bullet">
    /// <item>Pan: the stick offset is added to CameraControl's pending position offset after the same public helpers
    /// the keyboard pan path calls, so the game's own map-edge clamping still applies.</item>
    /// <item>Follow (R3 toggles <see cref="FollowOn"/>, kept in the config): the game only follows the units it copied
    /// when FOLLOW_CAM was pressed, and any pan or area change forgets them. While the toggle is on (controller mode),
    /// the game's own follow list is refilled with the current selection every frame, so following survives area
    /// changes, loads and selection changes. Panning with the stick starts "looking around": the camera stays where it
    /// was left (it used to glide back 0.75 s after the stick was released) until the
    /// selection's centre moves more than <see cref="ResumeDistance"/> (the party walks, another character is
    /// selected) or <see cref="Recenter"/> is called (Console style: R3 while looking around); then it glides back
    /// (<c>FocusOnPoint(…, time)</c>) and following resumes. Not during cutscenes, scripted camera moves or when the
    /// game has taken camera control.</item>
    /// </list>
    /// </summary>
    [HarmonyPatch]
    [Compat.Feature(Compat.Features.Camera)]
    internal static class CameraPan
    {
        /// <summary>Looking around ends when the selection's centre has moved this far (m) from where it was.</summary>
        private const float ResumeDistance = 1.5f;
        private const float GlideTime = 0.35f;

        /// <summary>
        /// Private CameraControl fields. In their own class so a changed game only fails here, inside the patch's
        /// try/catch, when the camera is first used — not when anything touches <see cref="Input"/> / <see cref="Speed"/>.
        /// </summary>
        private static class Refs
        {
            public static readonly AccessTools.FieldRef<CameraControl, Vector3> PositionOffset =
                AccessTools.FieldRefAccess<CameraControl, Vector3>("position_offset");
            public static readonly AccessTools.FieldRef<CameraControl, int> PlayerControlDisableLevel =
                AccessTools.FieldRefAccess<CameraControl, int>("PlayerControlDisableLevel");
            public static readonly AccessTools.FieldRef<CameraControl, SmartCamera> SmartCamera =
                AccessTools.FieldRefAccess<CameraControl, SmartCamera>("m_smartCamera");
            public static readonly AccessTools.FieldRef<CameraControl, List<GameObject>> FollowingUnits =
                AccessTools.FieldRefAccess<CameraControl, List<GameObject>>("m_FollowingUnits");
        }

        /// <summary>Stick input for this frame (x right, y up). Set by the world scheme, cleared after use.</summary>
        public static Vector2 Input;

        /// <summary>Multiplier on the game's keyboard pan speed at full stick deflection.</summary>
        public static float Speed = 1.5f;

        /// <summary>Follow-camera toggle (R3). Plugin keeps it in the config so it survives restarts.</summary>
        public static bool FollowOn;

        /// <summary>Follow only acts on the controller; with mouse/keyboard the game's own rules apply.</summary>
        public static bool OnGamepad;

        private static bool s_LookingAround;
        private static Vector3 s_LookAnchor;

        /// <summary>The follow camera is paused because the stick moved the camera away (it stays there).</summary>
        public static bool LookingAround => FollowOn && s_LookingAround;

        /// <summary>Ends looking around: glide back to the party and keep following.</summary>
        public static void Recenter()
        {
            s_LookingAround = false;
            CameraControl cam = CameraControl.Instance;
            if (cam != null)
            {
                StartGlide(cam);
            }
        }

        /// <summary>R3: flips the toggle; returns the new state.</summary>
        public static bool ToggleFollow()
        {
            FollowOn = !FollowOn;
            s_LookingAround = false;
            CameraControl cam = CameraControl.Instance;
            if (cam != null)
            {
                if (FollowOn)
                {
                    // Glide onto the party first rather than jumping.
                    StartGlide(cam);
                }
                else
                {
                    cam.CancelFollow();
                }
            }
            return FollowOn;
        }

        [HarmonyPatch(typeof(CameraControl), nameof(CameraControl.DoUpdate))]
        [HarmonyPrefix]
        private static void BeforeDoUpdate(CameraControl __instance)
        {
            Vector2 input = Input;
            Input = Vector2.zero;
            if (!Compat.Features.On(Compat.Features.Camera) || !Application.isPlaying || GameState.IsLoading || !GameState.ApplicationIsFocused)
            {
                return;
            }
            try
            {
                if (Refs.PlayerControlDisableLevel(__instance) != 0)
                {
                    return;
                }
                if (input != Vector2.zero)
                {
                    Pan(__instance, input);
                    if (!s_LookingAround)
                    {
                        s_LookingAround = true;
                        TryGetSelectionCentre(out s_LookAnchor);
                    }
                    return;
                }
                if (FollowOn && OnGamepad)
                {
                    KeepFollowing(__instance);
                }
            }
            catch (System.Exception e)
            {
                Compat.Features.Off(Compat.Features.Camera, "camera pan/follow failed: " + e);
            }
        }

        private static void Pan(CameraControl cam, Vector2 input)
        {
            Camera main = Camera.main;
            if (main == null)
            {
                return;
            }
            SmartCamera smart = Refs.SmartCamera(cam);
            if (smart != null)
            {
                smart.OnCameraPan();
            }
            cam.CancelFollow();
            cam.ResetAtEdges();
            // Same speed formula as CameraControl.CameraMoveDelta, scaled by stick deflection per axis.
            float delta = cam.MoveAmount * 30f * Time.unscaledDeltaTime * GameState.Option.ScrollSpeed * Speed;
            Refs.PositionOffset(cam) += (main.transform.right * input.x + main.transform.up * input.y) * delta;
        }

        private static void KeepFollowing(CameraControl cam)
        {
            if (Cutscene.CutsceneActive)
            {
                return;
            }
            if (s_LookingAround)
            {
                // Looked around with the stick: stay there until the party moves or another character is selected.
                if (TryGetSelectionCentre(out Vector3 centre) && (centre - s_LookAnchor).sqrMagnitude > ResumeDistance * ResumeDistance)
                {
                    s_LookingAround = false;
                    StartGlide(cam);
                }
                return;
            }
            if (cam.InterpolatingToTarget)
            {
                // Our glide back, or a scripted camera move of the game's: let it finish.
                return;
            }
            List<GameObject> follow = Refs.FollowingUnits(cam);
            if (follow == null)
            {
                return;
            }
            follow.Clear();
            foreach (GameObject member in PartyMemberAI.SelectedPartyMembers)
            {
                if (member != null)
                {
                    follow.Add(member);
                }
            }
        }

        private static void StartGlide(CameraControl cam)
        {
            if (TryGetSelectionCentre(out Vector3 centre))
            {
                cam.FocusOnPoint(centre, GlideTime);
            }
        }

        private static bool TryGetSelectionCentre(out Vector3 centre)
        {
            centre = Vector3.zero;
            int n = 0;
            foreach (GameObject member in PartyMemberAI.SelectedPartyMembers)
            {
                if (member != null)
                {
                    centre += member.transform.position;
                    n++;
                }
            }
            if (n == 0)
            {
                return false;
            }
            centre /= n;
            return true;
        }
    }
}
