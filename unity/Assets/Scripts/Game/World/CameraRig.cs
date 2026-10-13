using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The one camera. Menus get a slow cinematic drift along the river beside the player's bridge;
    /// in a duel it sits at the player's eye, looking down-lane, with the field of view set by the
    /// duel (the bare eye, or the scope's magnification).
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public enum Mode { Menu, Eye }

        public Camera Cam = null!;
        public Mode Current = Mode.Menu;
        public Vector3 Eye;
        /// <summary>Look direction in degrees: yaw clockwise from +z, pitch up.</summary>
        public float Yaw, Pitch;
        /// <summary>Vertical field of view in the eye mode, degrees.</summary>
        public float Fov = EyeFov;
        public Vector3 MenuFocus;
        float clock;

        public const float EyeFov = 55;

        public static CameraRig Create(Camera cam)
        {
            var rig = cam.gameObject.AddComponent<CameraRig>();
            rig.Cam = cam;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 6000;
            cam.clearFlags = CameraClearFlags.Skybox;
            return rig;
        }

        void LateUpdate()
        {
            clock += Time.deltaTime;
            if (Current == Mode.Menu)
            {
                // Low over the water off the near bridge, drifting and looking up the estuary.
                float t = clock * 0.02f;
                var pos = MenuFocus + new Vector3(-70 + Mathf.Sin(t) * 12, 9 + Mathf.Sin(t * 0.7f) * 1.5f, -60 + Mathf.Cos(t) * 8);
                Cam.transform.position = pos;
                var target = MenuFocus + new Vector3(10, 16, 260);
                Cam.transform.rotation = Quaternion.LookRotation(target - pos);
                Cam.fieldOfView = 50;
            }
            else
            {
                Cam.transform.position = Eye;
                Cam.transform.rotation = Quaternion.Euler(-Pitch, Yaw, 0);
                Cam.fieldOfView = Fov;
            }
        }
    }
}
