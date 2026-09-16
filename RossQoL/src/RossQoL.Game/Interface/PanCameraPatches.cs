using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Interface
{
    /// <summary>
    /// How far the camera is currently panned away from where the character
    /// looks. Cleared the moment the key is released, so the view snaps back.
    /// </summary>
    internal static class PanCameraState
    {
        public static float Yaw;
        public static float Pitch;
        public static bool Panning;

        public static bool KeyHeld()
        {
            var key = PanCameraConfig.PanKey?.Value;
            if (key == null || key.Value.MainKey == KeyCode.None) return false;

            // ZInput, not Input: it respects the game's own "is the player
            // typing" state, so panning never fires while a text box has focus.
            if (!ZInput.GetKey(key.Value.MainKey)) return false;
            foreach (var modifier in key.Value.Modifiers)
                if (!ZInput.GetKey(modifier)) return false;

            return true;
        }

        public static void Add(Vector2 delta)
        {
            float limit = PanCameraConfig.PanMaxPitch?.Value ?? 70f;
            Yaw += delta.x;
            Pitch = Mathf.Clamp(Pitch + delta.y, -limit, limit);
            Panning = true;
        }

        public static void Clear()
        {
            Yaw = 0f;
            Pitch = 0f;
            Panning = false;
        }
    }

    /// <summary>
    /// PlayerController.LateUpdate turns the character with the mouse. While
    /// the pan key is held the mouse moves only the camera: the delta is kept
    /// here and the character is told the mouse did not move, which is what
    /// vanilla itself does whenever input is blocked.
    /// </summary>
    [HarmonyPatch(typeof(PlayerController), "LateUpdate")]
    internal static class PanCameraInputPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(PlayerController), "LateUpdate", PanCameraFeature.FeatureName);

        private static bool Prefix(PlayerController __instance)
        {
            if (PanCameraFeature.Instance?.IsActive != true)
            {
                if (PanCameraState.Panning) PanCameraState.Clear();
                return true;
            }

            try
            {
                if (!PanCameraState.KeyHeld())
                {
                    if (PanCameraState.Panning) PanCameraState.Clear();
                    return true;
                }

                var character = __instance.m_character;
                if (character == null || !ZInput.IsMouseActive()) return true;

                // Both are static settings on PlayerController, not per-instance.
                var delta = ZInput.GetMouseDelta() * PlayerController.m_mouseSens;
                if (PlayerController.m_invertMouse) delta.y *= -1f;

                PanCameraState.Add(delta);
                character.SetMouseLook(Vector2.zero);
                return false;
            }
            catch (Exception ex)
            {
                PanCameraState.Clear();
                RossQoLPlugin.Log.LogError($"PanCamera: panning failed, mouse look restored: {ex}");
                return true;
            }
        }
    }

    /// <summary>
    /// GameCamera.GetCameraPosition places the camera behind the player's eye
    /// and looking where the eye looks. While panning, that direction is
    /// turned by the panned angle and the camera is put back behind it, using
    /// vanilla's own collision check so it still avoids walls.
    /// </summary>
    [HarmonyPatch(typeof(GameCamera), "GetCameraPosition")]
    internal static class PanCameraPositionPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(GameCamera), "GetCameraPosition", PanCameraFeature.FeatureName);

        private static void Postfix(GameCamera __instance, ref Vector3 pos, ref Quaternion rot)
        {
            if (PanCameraFeature.Instance?.IsActive != true || !PanCameraState.Panning) return;

            try
            {
                var player = Player.m_localPlayer;
                if (player == null || player.InIntro()) return;

                rot = Quaternion.AngleAxis(PanCameraState.Yaw, Vector3.up) * rot
                      * Quaternion.AngleAxis(PanCameraState.Pitch, Vector3.right);

                var eye = __instance.GetOffsetedEyePos();
                var end = eye + rot * Vector3.back * __instance.m_distance;
                __instance.CollideRay2(player.m_eye.position, eye, ref end);

                float liquid = Floating.GetLiquidLevel(end);
                if (end.y < liquid + __instance.m_minWaterDistance) end.y = liquid + __instance.m_minWaterDistance;

                pos = end;
            }
            catch (Exception ex)
            {
                PanCameraState.Clear();
                RossQoLPlugin.Log.LogError($"PanCamera: placing the panned camera failed: {ex}");
            }
        }
    }
}
