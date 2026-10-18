using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The one camera. Menus get a slow cinematic drift along the river beside the player's bridge;
    /// in a duel it sits at the player's eye, looking down-lane, with the field of view set by the
    /// duel (the bare eye, or the scope's magnification). Between the shoulder view and the scope
    /// the eye view shows the player's rifle in first person as it comes up to the eye.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public enum Mode { Menu, Eye, Director }

        public Camera Cam = null!;
        public Mode Current = Mode.Menu;
        public Vector3 Eye;
        /// <summary>Look direction in degrees: yaw clockwise from +z, pitch up.</summary>
        public float Yaw, Pitch;
        /// <summary>Vertical field of view in the eye mode, degrees.</summary>
        public float Fov = EyeFov;
        public Vector3 MenuFocus;
        /// <summary>
        /// At the bare eye the camera sits over the shooter's shoulder, so you see your own sniper
        /// lying at the edge with the river beyond; the scope puts it back at the eye.
        /// </summary>
        public bool ThirdPerson;
        /// <summary>
        /// Over the shoulder, the point the eye's line of sight lands on: the camera looks straight at
        /// it, so the screen's centre is what the scope opens on.
        /// </summary>
        public Vector3? AimPoint;
        /// <summary>
        /// Between the shoulder and the eye while the rifle comes up or goes down: 1 at the
        /// shoulder, 0 at the eye. The camera glides along, still looking at AimPoint, so the point
        /// under the crosshair stays in the middle of the screen all the way into the scope.
        /// </summary>
        public float Away;
        /// <summary>The player's own body, hidden whenever the camera is at the eye.</summary>
        public GameObject? Body;
        /// <summary>Over-the-shoulder offset in the camera's frame: right, up, back (metres).</summary>
        public static readonly Vector3 ShoulderOffset = new Vector3(0.85f, 0.75f, -2.4f);
        /// <summary>
        /// At the eye (not third person), how far the rifle is raised: 0 shouldered with the eye off
        /// the scope, 1 at the eyepiece, where the scope view takes over and the rifle hides.
        /// </summary>
        public float Raise = 1;
        /// <summary>Recoil climb in radians, for the first-person rifle.</summary>
        public float Kick;
        Viewmodel? viewmodel;
        /// <summary>The first-person rifle's furniture colour (the loadout's finish), or the default.</summary>
        public Color? RifleFinish;
        /// <summary>The first-person rifle's model (the loadout's rifle), or the default.</summary>
        public string? RifleModel;

        /// <summary>Drops the first-person rifle so the next shoulder builds it in the current finish.</summary>
        public void DropViewmodel()
        {
            if (viewmodel == null) return;
            Destroy(viewmodel.gameObject);
            viewmodel = null;
        }
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
            Sound.AmbienceOn = Current != Mode.Menu;
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
            else if (Current == Mode.Eye)
            {
                var rot = Quaternion.Euler(-Pitch, Yaw, 0);
                // Level the offset with the yaw only, so looking up or down doesn't swing the camera
                // into the deck.
                float away = ThirdPerson ? 1 : Away;
                var pos = Vector3.Lerp(Eye, Eye + Quaternion.Euler(0, Yaw, 0) * ShoulderOffset, away);
                Cam.transform.position = pos;
                Cam.transform.rotation = away > 0 && AimPoint is Vector3 at && (at - pos).sqrMagnitude > 1
                    ? Quaternion.LookRotation(at - pos) : rot;
                Cam.fieldOfView = Fov;
            }
            // Gliding in, the body stays until the camera is nearly at the eye, then the rifle takes over.
            bool behind = ThirdPerson || Away > 0.35f;
            if (Body != null) Body.SetActive(Current != Mode.Eye || behind);
            // First person: the rifle in front of the eye until the scope takes over.
            bool rifle = Current == Mode.Eye && !behind && Raise < 0.999f;
            if (rifle && viewmodel == null)
            {
                viewmodel = Viewmodel.Create(Cam.transform, RifleModel);
                if (RifleFinish is Color finish) Figure.PaintRifle(viewmodel.gameObject, finish);
                Arena.TrimSoon();
            }
            if (viewmodel != null)
            {
                if (viewmodel.gameObject.activeSelf != rifle) viewmodel.gameObject.SetActive(rifle);
                if (rifle) viewmodel.Pose(Raise, Kick, clock);
            }
        }
    }
}
