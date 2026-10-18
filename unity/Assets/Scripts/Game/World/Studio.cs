using SniperDuel.Rules;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// A small photo studio well below the map: the soldier standing in an outfit's colours, lit and
    /// filmed into a render texture for the Operators screen. Its camera sees only the studio layer
    /// (and the main camera never does); it renders only while the screen is open. Spin turns the
    /// figure for the PREVIEW turntable.
    /// </summary>
    public sealed class Studio : MonoBehaviour
    {
        const int Layer = 30;
        static Studio? instance;

        Camera cam = null!;
        Figure? figure;
        Color shown = Color.clear;
        string? shownSoldier;
        float yaw;

        public RenderTexture Texture { get; private set; } = null!;
        public bool Spin;

        public static Studio Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("Studio");
            go.transform.position = new Vector3(0, -2000, 0);
            instance = go.AddComponent<Studio>();
            instance.Build();
            return instance;
        }

        void Build()
        {
            Texture = new RenderTexture(512, 768, 24, RenderTextureFormat.ARGB32) { name = "Studio" };
            var camGo = new GameObject("Studio Camera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.05f, 4.6f);
            camGo.transform.LookAt(transform.position + new Vector3(0, 0.92f, 0));
            cam = camGo.AddComponent<Camera>();
            cam.targetTexture = Texture;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Opaque: a dark backdrop card, so the portrait never depends on the render texture's alpha.
            cam.backgroundColor = new Color(0.075f, 0.085f, 0.08f, 1);
            cam.fieldOfView = 28;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20;
            cam.allowHDR = false;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            cam.enabled = false;

            // A warm key from the front left and a cool rim from behind, besides the sun.
            Lamp("Key", new Vector3(-2.2f, 2.6f, 3f), new Color(1f, 0.93f, 0.82f), 5f);
            Lamp("Rim", new Vector3(1.8f, 2.2f, -2.4f), new Color(0.7f, 0.8f, 1f), 4f);

            if (Camera.main != null) Camera.main.cullingMask &= ~(1 << Layer);
        }

        void Lamp(string name, Vector3 at, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 9;
            l.color = color;
            l.intensity = intensity;
        }

        /// <summary>Starts or stops filming (only while the Operators screen is up).</summary>
        public bool Live
        {
            get => cam.enabled;
            set { cam.enabled = value; if (!value) Spin = false; }
        }

        /// <summary>Dresses the soldier in an outfit colour (rebuilding him only when it changes).</summary>
        public void Dress(Color tint, string? soldier = null)
        {
            if (figure != null && tint == shown && soldier == shownSoldier) return;
            if (figure != null) Destroy(figure.gameObject);
            figure = Figure.Create(Stance.Standing, 1.8f, transform, tint, false, soldier);
            shownSoldier = soldier;
            foreach (var t in figure.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            shown = tint;
            Debug.Log($"[Studio] dressed: {figure.GetComponentsInChildren<Renderer>().Length} renderers, bounds {Library.BoundsOf(figure.gameObject)}");
            yaw = 0;
            Pose();
        }

        void Pose()
        {
            if (figure != null) figure.transform.localRotation = Quaternion.Euler(0, 25 + yaw, 0);
        }

        void Update()
        {
            if (!cam.enabled || !Spin) return;
            yaw += 40 * Time.deltaTime;
            Pose();
        }
    }
}
