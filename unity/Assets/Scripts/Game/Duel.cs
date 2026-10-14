using System;
using System.Collections.Generic;
using SniperDuel.Ballistics;
using SniperDuel.Game.Ui;
using SniperDuel.Game.World;
using SniperDuel.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game
{
    /// <summary>
    /// A duel against the bot on one map: runs the match reducer and the bot's plan on the match
    /// clock, aims the camera from the player's eye (with sway, breath and recoil), drives the HUD
    /// and scope screens, and shows each shot (tracer, splash, hit call). Scoping in raises the
    /// rifle to the eye in first person before the scope view opens. Ported from the web client's
    /// duelMode.ts and shooter.ts.
    /// </summary>
    public sealed class Duel : MonoBehaviour
    {
        const float CycleSeconds = 1.4f;
        const Side Me = Side.A;

        GameConfig cfg = null!;
        UiHost ui = null!;
        CameraRig rig = null!;
        Arena arena = null!;
        DuelMap map = null!;

        public MatchState State = null!;
        public event Action<MatchState>? Finished;
        /// <summary>Your last shot and last hit of the match, with bullet cam captions, for a replay from the results.</summary>
        public ShotOutcome? LastShot { get; private set; }
        public string LastShotWho { get; private set; } = "";
        public ShotOutcome? LastHit { get; private set; }
        public string LastHitWho { get; private set; } = "";

        double time;
        readonly List<MatchInput> botQueue = new List<MatchInput>();
        string phaseKey = "";
        double resumeAt = -1;
        Action? resume;

        // Aim, rules frame (rad).
        double yaw, pitch;
        bool scoped;
        int zoom = 10;
        double elevMrad, windMrad;
        BreathState breath = Sway.InitialBreath();
        readonly uint swaySeed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
        double recoil;
        double lastShotAt = -10;
        string hint = "";
        float hintTimer;
        Stance enemyStanceShown = (Stance)(-1);
        Vec3 enemyPosShown;
        Stance playerStanceShown = (Stance)(-1);
        Vec3 playerPosShown;

        /// <summary>Side focus (parallax) knob position, 0 to ScopeDials.FocusMax; starts near, so the far bridge is soft until it is set.</summary>
        double focus = 2;
        string laserText = "--- m";

        /// <summary>The finger aiming by dragging the screen, and the screen root that holds it.</summary>
        int? lookPointer;
        VisualElement? lookRoot;
        Vector2 lookLast;
        /// <summary>The thumb on the scope's fire button: holding breath, fine-aiming, firing on release.</summary>
        int? firePointer;
        VisualElement? firePad, fireButton;
        Vector2 fireLast;
        bool fireCancel;
        float fireDownAt = -10;
        bool breathSpent;
        /// <summary>The fire button's how-to shows until it is first used.</summary>
        static bool fireTaught;
        /// <summary>With the hold-to-fire setting, how long the fire button must be held.</summary>
        const float FireHoldSeconds = 0.3f;
        /// <summary>Aim per thumb movement on the fire button, relative to dragging the screen.</summary>
        const float FineAim = 0.3f;
        /// <summary>How far the thumb can wander from the fire button's centre, in button radii, before release cancels.</summary>
        const float FireReach = 1.45f;
        bool director;
        bool paused, forfeitArmed;

        /// <summary>Seconds to bring the rifle from the shoulder to the eye, and to lower it again.</summary>
        const float RaiseSeconds = 0.42f, LowerSeconds = 0.26f;
        /// <summary>0 rifle shouldered (or third person), 1 eye at the scope.</summary>
        float raise;
        /// <summary>+1 raising to the scope, -1 lowering out of it, 0 still.</summary>
        int raising;
        /// <summary>The eye settling into the eyepiece after the raise (0 to 1).</summary>
        float scopeOpen = 1;
        /// <summary>Field of view on screen, eased toward the aim's so zoom and scope-in glide.</summary>
        float shownFov = -1;
        SwayOffset lastSway;
        Vector2 eyeShown;

        /// <summary>The rifle the bot takes into a duel.</summary>
        public static string BotRifle(GameConfig cfg, BotDifficulty bot, string playerRifle) => cfg.Rifles.Count > 1 ? cfg.Rifles[1].Id : playerRifle;

        /// <summary>A fresh match seed (the versus card rolls it so it can show the match's conditions).</summary>
        public static uint NewSeed() => (uint)(DateTime.UtcNow.Ticks ^ UnityEngine.Random.Range(0, int.MaxValue)) | 1;

        public static Duel Begin(GameConfig cfg, UiHost ui, CameraRig rig, Arena arena, string playerName, string rifleId, BotDifficulty bot, uint seed)
        {
            var d = new GameObject("Duel").AddComponent<Duel>();
            d.cfg = cfg;
            d.ui = ui;
            d.rig = rig;
            d.arena = arena;
            d.map = arena.Map;
            var botRifle = BotRifle(cfg, bot, rifleId);
            d.State = Match.CreateMatch(seed, arena.Map.Id, new PerSide<PlayerSetup>(
                new PlayerSetup(playerName, rifleId),
                new PlayerSetup(Catalog.BotName(bot), botRifle, bot)), 0, cfg);
            d.zoom = (int)Math.Round((cfg.Scope.MinZoom + cfg.Scope.MaxZoom) / 2);
            d.BindScreens();
            d.EnterPhase();
            d.LookAtEnemy();
            d.ShowHud();
            return d;
        }

        // ---- screens -------------------------------------------------------

        void BindScreens()
        {
            // Outside the scope: turn the knobs on the rifle's scope, drag elsewhere to look, scope in.
            var hud = ui.Get("Hud");
            hud.On("scope", () => SetScoped(true));
            hud.On("pause", () => SetPaused(true));
            hud.On("resume", () => SetPaused(false));
            hud.On("forfeit", () =>
            {
                // A second tap confirms: a stray one mustn't throw the match.
                if (!forfeitArmed)
                {
                    forfeitArmed = true;
                    hud.Text("forfeit", "TAP AGAIN TO FORFEIT");
                    hud.Text("pauseNote", "Forfeiting counts as a loss.");
                    return;
                }
                SetPaused(false);
                Quit();
            });
            // The marker shows a range only once the laser has measured one.
            hud.Text("markerRange", "");
            Look(hud.Root);
            ApplyHudSettings(hud, ui.Get("Scope"));
            HudLayoutMenu.ApplyTo(hud, Settings.HudScales[Settings.HudScale] / 100f);
            ScopeDials.MinElevation = -2;
            ScopeDials.MaxElevation = cfg.Scope.MaxElevationMrad;
            ScopeDials.MaxWindage = cfg.Scope.MaxWindageMrad;
            ScopeDials.MinZoom = (int)cfg.Scope.MinZoom;
            ScopeDials.MaxZoom = (int)cfg.Scope.MaxZoom;
            ScopeDials.Turned = Turn;

            // Inside: one fire button (hold breath, fine-aim, release to fire), the laser, scope out.
            var scope = ui.Get("Scope");
            scope.On("exit", () => SetScoped(false));
            scope.On("lrf", Laser);
            Look(scope.Root);
            HudLayoutMenu.ApplyTo(scope);
            if (scope.Has("fireRing")) FirePad(scope["fireRing"]);
        }

        /// <summary>A knob on the scope turned by some detents: a click and a buzz for each move.</summary>
        void Turn(ScopeDials.Dial dial, int clicks)
        {
            switch (dial)
            {
                case ScopeDials.Dial.Elevation:
                    Dial(ref elevMrad, clicks, cfg.Scope.MaxElevationMrad, -2, true);
                    break;
                case ScopeDials.Dial.Windage:
                    Dial(ref windMrad, clicks, cfg.Scope.MaxWindageMrad, -cfg.Scope.MaxWindageMrad, false);
                    break;
                case ScopeDials.Dial.Zoom:
                {
                    int was = zoom;
                    zoom = (int)Math.Max(cfg.Scope.MinZoom, Math.Min(cfg.Scope.MaxZoom, zoom + clicks));
                    if (zoom == was) break;
                    Sound.Zoom();
                    Haptics.Click();
                    break;
                }
                case ScopeDials.Dial.Focus:
                {
                    double was = focus;
                    focus = Math.Max(0, Math.Min(ScopeDials.FocusMax, Math.Round(focus * 10 + clicks) / 10));
                    if (focus == was) break;
                    Sound.Zoom();
                    Haptics.Click();
                    break;
                }
            }
        }

        /// <summary>
        /// Drag on empty screen to aim: up looks up, left looks left. The first finger down keeps
        /// the aim until it lifts (or the system cancels it); a second finger landing on empty
        /// screen is ignored, so a thumb that misses a button can't steal the aim.
        /// </summary>
        void Look(VisualElement root)
        {
            root.pickingMode = PickingMode.Position;
            root.RegisterCallback<PointerDownEvent>(e =>
            {
                // Screens outlive a duel; a finished one (destroyed) leaves the input alone.
                if (!this || paused) return;
                for (var t = e.target as VisualElement; t != null && t != root; t = t.parent)
                    if (t.ClassListContains(Layout.ActionClass)) return;
                // Still held by a live finger: keep it.
                if (lookPointer != null && lookRoot != null && lookRoot.HasPointerCapture(lookPointer.Value)) return;
                lookPointer = e.pointerId;
                lookRoot = root;
                lookLast = e.position;
                root.CapturePointer(e.pointerId);
            });
            root.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (lookPointer != e.pointerId || lookRoot != root) return;
                Vector2 p = e.position;
                Steer(p - lookLast, 1);
                lookLast = p;
            });
            root.RegisterCallback<PointerUpEvent>(e => EndLook(root, e.pointerId, true));
            root.RegisterCallback<PointerCancelEvent>(e => EndLook(root, e.pointerId, true));
            root.RegisterCallback<PointerCaptureOutEvent>(e => EndLook(root, e.pointerId, false));
        }

        void EndLook(VisualElement root, int pointerId, bool release)
        {
            if (lookPointer != pointerId || lookRoot != root) return;
            lookPointer = null;
            lookRoot = null;
            if (release && root.HasPointerCapture(pointerId)) root.ReleasePointer(pointerId);
        }

        /// <summary>Lets go of any aiming finger (the screen it was on is going away).</summary>
        void DropLook()
        {
            int? id = lookPointer;
            var root = lookRoot;
            lookPointer = null;
            lookRoot = null;
            if (id != null && root != null && root.HasPointerCapture(id.Value)) root.ReleasePointer(id.Value);
        }

        /// <summary>
        /// Turns the aim by a drag in UI units, scaled to the field of view so the scope stays
        /// usable. Screen y runs down, so dragging up (negative y) raises the view.
        /// </summary>
        void Steer(Vector2 d, float gain)
        {
            double radPerUnit = SensitivityFov() * Mathf.Deg2Rad / UiHost.RefHeight * 0.9
                * (scoped ? Settings.ScopeSensitivity : Settings.LookSensitivity) * gain;
            if (scoped && Settings.InvertScopeDrag) d = -d;
            if (Settings.InvertLookY) d.y = -d.y;
            yaw += d.x * radPerUnit;
            pitch = Math.Max(-0.3, Math.Min(0.3, pitch - d.y * radPerUnit * Settings.VerticalSensitivity));
        }

        /// <summary>
        /// The scope's one fire button. Press and hold: the breath is held (the ring drains).
        /// Sliding the same thumb nudges the aim, slowly, for the last fine correction. Lifting
        /// fires. Sliding well off the button arms a cancel: lifting there doesn't shoot, sliding
        /// back re-arms. The thumb is captured, so drifting never drops the breath.
        /// </summary>
        void FirePad(VisualElement pad)
        {
            firePad = pad;
            pad.pickingMode = PickingMode.Position;
            pad.AddToClassList(Layout.ActionClass);
            var button = fireButton = ui.Get("Scope").Has("fireBtn") ? ui.Get("Scope")["fireBtn"] : pad;
            pad.RegisterCallback<PointerDownEvent>(e =>
            {
                e.StopPropagation();
                if (!this || firePointer != null) return;
                firePointer = e.pointerId;
                fireLast = e.position;
                fireCancel = false;
                fireDownAt = Time.unscaledTime;
                fireTaught = true;
                pad.CapturePointer(e.pointerId);
                button.style.scale = new Scale(new Vector2(0.94f, 0.94f));
                SetBreath(true);
            });
            pad.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (firePointer != e.pointerId) return;
                Vector2 p = e.position;
                var box = pad.worldBound;
                bool off = (p - box.center).magnitude > box.width * 0.5f * FireReach;
                if (off != fireCancel)
                {
                    fireCancel = off;
                    Haptics.Click();
                }
                if (!off) Steer(p - fireLast, FineAim);
                fireLast = p;
            });
            pad.RegisterCallback<PointerUpEvent>(e => EndFire(e.pointerId, shoot: true));
            pad.RegisterCallback<PointerCancelEvent>(e => EndFire(e.pointerId, shoot: false));
            pad.RegisterCallback<PointerCaptureOutEvent>(e => EndFire(e.pointerId, shoot: false));

            void EndFire(int id, bool shoot)
            {
                if (firePointer != id) return;
                firePointer = null;
                if (pad.HasPointerCapture(id)) pad.ReleasePointer(id);
                button.style.scale = new Scale(Vector2.one);
                bool cancelled = fireCancel;
                fireCancel = false;
                // The shot goes while the breath is still held; then the breath is let go.
                if (shoot && !cancelled) Fire();
                else if (shoot) Call("Shot cancelled");
                SetBreath(false);
            }
        }

        /// <summary>Lets go of the fire button without shooting (scoping out, the screen changing).</summary>
        void DropFire()
        {
            int? id = firePointer;
            firePointer = null;
            fireCancel = false;
            if (id != null && firePad != null && firePad.HasPointerCapture(id.Value)) firePad.ReleasePointer(id.Value);
            if (fireButton != null) fireButton.style.scale = new Scale(Vector2.one);
            SetBreath(false);
        }

        /// <summary>
        /// Field of view the look drag is scaled to: the real one, or with soft scope scaling one
        /// that shrinks less with zoom so high magnification turns faster.
        /// </summary>
        double SensitivityFov() =>
            scoped && Settings.ScopeScaling == 1 ? Math.Pow(Fov(), 0.8) * Math.Pow(cfg.Scope.EyeFovDeg, 0.2) : Fov();

        /// <summary>The HUD options from Settings: which panels show, opacity and scale.</summary>
        static void ApplyHudSettings(UiScreen hud, UiScreen scope)
        {
            foreach (var s in new[] { hud, scope })
            {
                s.Show("windBox", Settings.WindArrows);
                s.Show("dialled", Settings.RangeCard);
            }
            foreach (var card in new[] { "cardElevBox", "cardWindBox", "cardZoomBox", "cardFocusBox" })
                hud.Show(card, Settings.RangeCard);
            hud.Show("marker", Settings.RangeMarker);
            hud.Show("crosshair", Settings.Crosshair);
            hud.Root.style.opacity = Settings.HudOpacity;
            // Each group shrinks toward the corner or edge it is anchored to, so corners stay put.
            float scale = Settings.HudScales[Settings.HudScale] / 100f;
            foreach (var layer in hud.Root.Children())
            foreach (var top in layer.Children())
            {
                float x = Origin(top.style.left, top.style.right, top.style.width, UiHost.RefWidth);
                float y = Origin(top.style.top, top.style.bottom, top.style.height, UiHost.RefHeight);
                top.style.transformOrigin = new TransformOrigin(Length.Percent(x), Length.Percent(y), 0);
                top.style.scale = new Scale(new Vector2(scale, scale));
            }
        }

        /// <summary>
        /// Where along one axis a HUD group should shrink toward, in percent: the far edge when it is
        /// anchored there, the near edge when it sits in the near third, else its middle.
        /// </summary>
        static float Origin(StyleLength near, StyleLength far, StyleLength size, float span)
        {
            bool n = near.keyword == StyleKeyword.Undefined, f = far.keyword == StyleKeyword.Undefined;
            if (n && f) return 50;
            if (f) return 100;
            if (!n) return 50;
            if (near.value.unit != LengthUnit.Pixel) return 0;
            float centre = near.value.value + (size.keyword == StyleKeyword.Undefined && size.value.unit == LengthUnit.Pixel ? size.value.value / 2 : 0);
            return centre < span / 3 ? 0 : centre > span * 2 / 3 ? 100 : 50;
        }

        void ShowHud()
        {
            scoped = false;
            raise = 0;
            raising = 0;
            ui.Show("Hud");
        }

        /// <summary>
        /// Scoping in raises the rifle to the eye in first person, then opens the scope view;
        /// scoping out lowers it and returns to the shoulder view. Already there, it just shows the
        /// screen again (after a bullet cam).
        /// </summary>
        void SetScoped(bool on)
        {
            if (on && (scoped || raising > 0))
            {
                if (scoped) ui.Show("Scope");
                return;
            }
            if (!on && !scoped && raising <= 0)
            {
                if (raise <= 0) ui.Show("Hud");
                return;
            }
            // The screen changes under any finger still down: nothing carries over, and the breath goes.
            DropLook();
            DropFire();
            scoped = false;
            raising = on ? 1 : -1;
            Sound.Raise(on);
            ui.HideAll();
        }

        /// <summary>Moves the raise along; at the top the scope opens, at the bottom the HUD returns.</summary>
        void Raise(float dt)
        {
            if (raising == 0) return;
            raise = Mathf.Clamp01(raise + raising * dt / (raising > 0 ? RaiseSeconds : LowerSeconds));
            if (raising > 0 && raise >= 1)
            {
                raising = 0;
                scoped = true;
                scopeOpen = 0;
                // The picture settles in from a touch wider as the eye finds the exit pupil.
                shownFov = (float)Fov() * 1.35f;
                if (!director) ui.Show("Scope");
            }
            else if (raising < 0 && raise <= 0)
            {
                raising = 0;
                if (!director) ui.Show("Hud");
            }
        }

        void Dial(ref double value, int clicks, double max, double min, bool elevation)
        {
            double click = cfg.Scope.ClickMrad;
            double v = Math.Round(value / click + clicks) * click;
            double was = value;
            value = Math.Min(max, Math.Max(min, Math.Round(v, 4)));
            Sound.Turret(elevation, value != was);
            Haptics.Click();
        }

        void SetBreath(bool hold)
        {
            bool was = breath.Holding;
            float strain = Strain();
            breath = hold ? Sway.HoldBreath(breath, time, cfg) : Sway.ReleaseBreath(breath, time, cfg);
            // Only a real change makes a sound (scoping out lets go of a breath that was never held).
            if (breath.Holding != was) Sound.Breath(breath.Holding, strain);
        }

        /// <summary>How strained the held breath is: 0 steady, 1 at the worst shake.</summary>
        float Strain()
        {
            var hb = cfg.Sway.HoldBreath;
            double bf = Sway.BreathFactor(breath, time, cfg);
            return Mathf.Clamp01((float)((bf - hb.SteadyFactor) / Math.Max(0.01, hb.MaxStrainFactor - hb.SteadyFactor)));
        }

        void Quit()
        {
            Apply(MatchInput.ForfeitBy(Me, time));
        }

        void Call(string text)
        {
            hint = text;
            hintTimer = 4;
        }

        // ---- match ---------------------------------------------------------

        void Apply(MatchInput input)
        {
            var r = Match.ApplyInput(State, input, cfg);
            if (r.Error != null)
            {
                if (input.Side == Me && input.Type != MatchInputType.Tick) Call(r.Error);
                return;
            }
            var before = State;
            State = r.State;
            if (r.Outcome != null) OnShot(before, r.Outcome);
            else CheckPhase();
        }

        void CheckPhase()
        {
            if (resume != null) return;
            string key = $"{State.Round}:{State.Pair}:{State.Step}:{State.Phase}";
            if (key != phaseKey) EnterPhase();
        }

        void EnterPhase()
        {
            var s = State;
            phaseKey = $"{s.Round}:{s.Pair}:{s.Step}:{s.Phase}";
            botQueue.Clear();
            if (s.Players.B.Bot != null) botQueue.AddRange(Bot.Plan(s, Side.B, cfg));
            botQueue.Sort((a, b) => a.Time.CompareTo(b.Time));
            if (s.Phase == PhaseKind.Setup)
            {
                // Fixed spots: take ours, prone, and get on with it.
                var spot = map.Spots[Me][0];
                Apply(MatchInput.PlaceAt(Me, spot, Match.ClampStance(s, Stance.Prone, cfg), time));
                Apply(MatchInput.ReadyUp(Me, time));
                Call("Find the enemy on the far bridge. Read the flags.");
            }
            else if (s.Phase == PhaseKind.Shot)
            {
                Call(s.Actor == Me ? "Your shot. Set the dials, scope in, hold fire and let go." : "Enemy shot. Stay still.");
            }
            else if (s.Phase == PhaseKind.Move && s.Actor == Me)
            {
                // No moves on a fixed map: pass straight away.
                Apply(MatchInput.ReadyUp(Me, time));
            }
        }

        void OnShot(MatchState before, ShotOutcome o)
        {
            var shooter = before.Actor ?? Me;
            bool mine = shooter == Me;
            float tof = (float)o.Impact.T;
            bool cinematic = mine && o.Verdict != Verdict.Miss && o.Trajectory.Points.Count > 1;
            World.Effects.Shot(o, mine, arena, tracer: !cinematic);
            float range = (float)Math.Sqrt(Sq(o.Impact.Point.X - o.Eye.X) + Sq(o.Impact.Point.Z - o.Eye.Z));
            var surface = Sound.SurfaceOf(o, arena.Map);
            if (mine)
            {
                recoil = 0.004;
                Haptics.Recoil();
                Sound.FireMine();
                // The bullet cam plays its own impact.
                if (!cinematic) Sound.Landed(range, tof, surface, o.Seed);
            }
            else
            {
                // The report comes from the shooter; the strike, if it lands near, from beside us.
                var eye = Eye();
                float shooterM = (float)Math.Sqrt(Sq(o.Eye.X - eye.X) + Sq(o.Eye.Y - eye.Y) + Sq(o.Eye.Z - eye.Z));
                float impactM = (float)Math.Sqrt(Sq(o.Impact.Point.X - eye.X) + Sq(o.Impact.Point.Y - eye.Y) + Sq(o.Impact.Point.Z - eye.Z));
                Sound.FireEnemy(shooterM, tof, o.NearMiss, o.Verdict != Verdict.Miss, impactM, surface, o.Seed);
                if (o.Verdict != Verdict.Miss) Haptics.Struck(o.Verdict == Verdict.Kill);
                else if (o.NearMiss) Haptics.Snap();
            }

            string dist = Math.Round(Math.Sqrt(Sq(o.Impact.Point.X - o.Eye.X) + Sq(o.Impact.Point.Z - o.Eye.Z))).ToString("0");
            string text;
            if (o.Verdict == Verdict.Kill) text = mine ? $"KILL · {o.Zone} at {dist} m, {o.Impact.T:0.00} s" : "You were killed.";
            else if (o.Verdict == Verdict.Wound) text = mine ? $"HIT · {o.Zone} wound at {dist} m" : "You're hit. Your aim will shake.";
            else if (mine && o.Call.HasValue) text = "Miss · " + SpotterCall(o.Call.Value.UpMrad, o.Call.Value.RightMrad);
            else text = mine ? "Miss." : o.NearMiss ? "Snap! It passed within a metre." : "Enemy fired and missed.";

            // Let the bullet land before the result shows and the phase moves on.
            resume = () =>
            {
                Call(text);
                if (State.Phase == PhaseKind.RoundOver) Pause(3, () => Apply(MatchInput.NextRound(time)));
                else if (State.Phase == PhaseKind.MatchOver) Pause(2.5, () => Finished?.Invoke(State));
                else CheckPhase();
            };
            resumeAt = time + (mine ? tof + 0.6 : 0.4);
            if (mine && o.Trajectory.Points.Count > 1)
            {
                LastShot = o;
                LastShotWho = $"ROUND {before.Round + 1} · {before.Players[Me].Name} → {before.Players[Side.B].Name}";
            }
            if (cinematic) PlayBulletCam(o, before);
        }

        /// <summary>Every hit you land replays from behind the bullet; the match waits for it.</summary>
        void PlayBulletCam(ShotOutcome o, MatchState before)
        {
            var next = resume;
            resume = () => { };
            resumeAt = double.MaxValue;
            director = true;
            LastHit = o;
            LastHitWho = LastShotWho;
            Replay(ui, rig, arena, o, LastHitWho, () =>
            {
                director = false;
                SetScoped(scoped);
                resume = next;
                resumeAt = time;
            });
        }

        /// <summary>Plays the bullet cam for one shot on the Killcam screen; done runs when it ends or is skipped.</summary>
        public static BulletCam Replay(UiHost ui, CameraRig rig, Arena arena, ShotOutcome o, string who, Action done)
        {
            var k = ui.Show("Killcam");
            k.Text("who", who);
            var last = o.Trajectory.Points[o.Trajectory.Points.Count - 1];
            k.Text("tof", $"{o.Impact.T:0.00} s");
            k.Text("energy", $"{o.Impact.Energy / 1000:0.0} kJ");
            k.Text("velocity", $"{o.Impact.Speed:0} m/s");
            k.Text("drop", $"{Math.Abs(last.Y):0.00} m");
            k.Text("drift", $"{Math.Abs(last.Z):0.00} m {(last.Z >= 0 ? "R" : "L")}");
            double dist = Math.Sqrt(Sq(o.Impact.Point.X - o.Eye.X) + Sq(o.Impact.Point.Z - o.Eye.Z));
            k.Text("lane", $"{dist:0} m");
            var cam = BulletCam.Play(o, rig, arena.Root, (f, speed) =>
            {
                k.Fill("progress", f);
                if (k.Has("knob")) k["knob"].style.left = Length.Percent(f * 100);
                k.Text("speed", $"{speed:0.00}×");
                k.Text("distance", $"{dist * f:0} / {dist:0} m");
            }, done, arena.Enemy);
            k.On("skip", cam.Finish);
            k.On("pause", () => cam.Paused = !cam.Paused);
            return cam;
        }

        void Pause(double seconds, Action next)
        {
            resumeAt = time + seconds;
            resume = next;
        }

        static double Sq(double v) => v * v;

        static string SpotterCall(double up, double right)
        {
            var parts = new List<string>();
            if (Math.Abs(up) >= 0.1) parts.Add($"{Math.Abs(up):0.0} {(up > 0 ? "high" : "low")}");
            if (Math.Abs(right) >= 0.1) parts.Add($"{Math.Abs(right):0.0} {(right > 0 ? "right" : "left")}");
            return parts.Count > 0 ? string.Join(", ", parts) + " (mil)" : "just off the edge";
        }

        // ---- aiming --------------------------------------------------------

        Vec3 Eye()
        {
            var p = State.Players[Me];
            return Combat.EyeOf(p.Position, p.Stance, cfg);
        }

        double Fov() => scoped ? cfg.Scope.FovAt1xDeg / zoom : cfg.Scope.EyeFovDeg;

        Aim TrueAim()
        {
            var p = State.Players[Me];
            var s = Sway.Offset(new SwayInput
            {
                Stance = p.Stance,
                Bipod = p.Stance == Stance.Prone,
                Breath = breath,
                Seed = swaySeed,
                Penalty = p.Wounds > 0 ? cfg.Player.Wound.SwayFactor : 1,
            }, time, cfg);
            lastSway = s;
            return new Aim { Yaw = yaw + s.Right / 1000, Pitch = pitch + s.Up / 1000 + recoil };
        }

        void LookAtEnemy()
        {
            var eye = Eye();
            var e = State.Players[Side.B];
            var target = Combat.EyeOf(e.Position, e.Stance, cfg);
            yaw = Math.Atan2(target.X - eye.X, -(target.Z - eye.Z));
            pitch = Math.Atan2(target.Y - eye.Y, Math.Sqrt(Sq(target.X - eye.X) + Sq(target.Z - eye.Z)));
        }

        bool CanFire() => State.Phase == PhaseKind.Shot && State.Actor == Me && !State.Acted && resume == null;

        void Fire()
        {
            if (!CanFire())
            {
                Call("Wait for your shot");
                return;
            }
            if (time - lastShotAt < CycleSeconds) return;
            if (Settings.FireHold && Time.unscaledTime - fireDownAt < FireHoldSeconds)
            {
                Call("Hold the fire button to shoot");
                return;
            }
            lastShotAt = time;
            var aim = TrueAim();
            Apply(MatchInput.FireShot(Me, new FireInput
            {
                Yaw = aim.Yaw,
                Pitch = aim.Pitch,
                DialElevationMrad = elevMrad,
                DialWindageMrad = windMrad,
            }, time));
            if (scoped && Settings.UnscopeAfterShot) SetScoped(false);
        }

        /// <summary>The laser rangefinder: measures along the line of sight and shows it. It never dials anything.</summary>
        void Laser()
        {
            var e = State.Players[Side.B];
            var targets = new List<Silhouette> { Combat.SilhouetteFor("B", e.Position, e.Stance, Combat.FacingYaw(Side.B), cfg) };
            var d = Hits.Rangefind(Eye(), TrueAim(), targets, map.Ground, 3000, map.Cover);
            Call(d.HasValue ? $"Laser: {d.Value:0} m" : "Laser: no return");
            laserText = d.HasValue ? $"{d.Value:0} m" : "no return";
            if (d.HasValue) ui.Get("Hud").Text("markerRange", $"{d.Value:0} m");
        }

        static string Fmt(double mrad, string pos, string neg) =>
            Math.Abs(mrad) < 0.05 ? "0.0" : $"{(mrad > 0 ? pos : neg)} {Math.Abs(mrad):0.0}";

        // ---- frame ---------------------------------------------------------

        /// <summary>The pause menu: the match clock and the bot stop until RESUME.</summary>
        void SetPaused(bool on)
        {
            paused = on && State.Phase != PhaseKind.MatchOver;
            forfeitArmed = false;
            var hud = ui.Get("Hud");
            hud.Text("forfeit", "FORFEIT MATCH");
            hud.Text("pauseNote", "The clock is stopped. The bot waits for you.");
            hud.Show("pauseMenu", paused);
        }

        /// <summary>
        /// Leaving the app (a call, the home screen) opens the pause menu, so you come back to it rather
        /// than to a running clock. (In the scope the clock is already safe: frames stop in the
        /// background and a frame's step is capped at 0.1 s.)
        /// </summary>
        void OnApplicationPause(bool leaving)
        {
            if (!leaving || paused || director || scoped || raising != 0 || State.Phase == PhaseKind.MatchOver) return;
            SetPaused(true);
        }

        void Update()
        {
            if (paused) return;
            float dt = Mathf.Min(0.1f, Time.deltaTime);
            time += dt;
            recoil *= Math.Exp(-dt * 6);
            if (hintTimer > 0) hintTimer -= dt;

            if (resume != null && time >= resumeAt)
            {
                var r = resume;
                resume = null;
                r();
            }
            if (resume == null)
            {
                while (botQueue.Count > 0 && botQueue[0].Time <= time && resume == null)
                {
                    var next = botQueue[0];
                    botQueue.RemoveAt(0);
                    Apply(next);
                }
                if (resume == null && State.Phase != PhaseKind.MatchOver && time >= State.PhaseEnd) Apply(MatchInput.Tick(time));
            }

            Raise(dt);
            scopeOpen = Mathf.MoveTowards(scopeOpen, 1, dt / 0.22f);
            var aim = TrueAim();
            if (!director) rig.Current = CameraRig.Mode.Eye;
            rig.Eye = Library.ToUnity(Eye());
            rig.Yaw = (float)(aim.Yaw * Mathf.Rad2Deg);
            rig.Pitch = (float)(aim.Pitch * Mathf.Rad2Deg);
            rig.Fov = ShownFov(dt);
            rig.ThirdPerson = !scoped && raising == 0 && raise <= 0;
            rig.Raise = scoped ? 1 : raise;
            rig.Kick = (float)recoil;
            ScopeEye(dt);
            PostFx.Scope(scoped ? scopeOpen : 0);
            PostFx.Defocus(scoped && !director ? Defocus() * scopeOpen : 0);
            ScopeDials.Elevation = elevMrad;
            ScopeDials.Windage = windMrad;
            ScopeDials.Zoom = zoom;
            ScopeDials.Focus = focus;
            Steadiness();

            var wind = Match.ActiveConditions(State).Wind;
            arena.SetWind(wind);
            Sounds(wind);
            SyncEnemy();
            SyncPlayer();
            RenderHud(wind);
        }

        /// <summary>
        /// The field of view on screen: through the scope it glides to each new magnification (in
        /// log space, so every step feels alike); coming up to the eye it narrows a little as the
        /// shooter focuses down the rifle.
        /// </summary>
        float ShownFov(float dt)
        {
            float target = (float)Fov();
            if (!scoped) shownFov = Mathf.Lerp(target, target * 0.8f, Mathf.SmoothStep(0, 1, raise));
            else if (shownFov <= 0) shownFov = target;
            else shownFov = Mathf.Exp(Mathf.Lerp(Mathf.Log(shownFov), Mathf.Log(target), 1 - Mathf.Exp(-dt * 9)));
            return shownFov;
        }

        /// <summary>
        /// How soft the picture is with the side focus where it is: sharp within about 12% of the
        /// target's range, fully soft by a factor of about 2.5 off.
        /// </summary>
        float Defocus()
        {
            var a = State.Players[Me].Position;
            var b = State.Players[Side.B].Position;
            double range = Math.Max(20, Math.Sqrt(Sq(b.X - a.X) + Sq(b.Z - a.Z)));
            double set = Math.Min(20000, ScopeDials.FocusMetres(focus));
            return Mathf.Clamp01((float)((Math.Abs(Math.Log(set / range)) - 0.12) / 0.8));
        }

        /// <summary>
        /// The breath ring and the heart rate for the scope view. The ring drains over the steady
        /// part of a held breath (a buzz when it runs out) and refills as the shooter recovers; the
        /// pulse climbs with the strain and with a wound.
        /// </summary>
        void Steadiness()
        {
            var hb = cfg.Sway.HoldBreath;
            double bf = Sway.BreathFactor(breath, time, cfg);
            float recovering = Mathf.Clamp01((float)((bf - 1) / Math.Max(0.01, hb.MaxStrainFactor - 1)));
            float left;
            if (breath.Holding)
            {
                left = 1 - Mathf.Clamp01((float)((time - breath.Since) / Math.Max(0.1, hb.SteadySeconds)));
                if (left <= 0 && !breathSpent)
                {
                    breathSpent = true;
                    Haptics.Snap();
                }
            }
            else
            {
                left = 1 - recovering;
                breathSpent = false;
            }
            float strain = breath.Holding ? Strain() : recovering;
            ScopeGauges.Breath = left;
            ScopeGauges.Holding = breath.Holding;
            ScopeGauges.Cancelling = firePointer != null && fireCancel;
            ScopeGauges.Bpm = 66 + 56 * strain + (State.Players[Me].Wounds > 0 ? 14 : 0) + 1.5f * Mathf.Sin((float)time * 0.8f);
        }

        /// <summary>
        /// Where the eye sits behind the scope, for the scope shadow: it follows the rifle's wobble
        /// and recoil and wanders a little on its own; holding the breath steadies the head, so it
        /// drifts less and follows more smoothly.
        /// </summary>
        void ScopeEye(float dt)
        {
            bool held = breath.Holding;
            float wander = held ? 1.2f : 4.5f;
            float t = (float)time;
            var drift = new Vector2(Mathf.PerlinNoise(t * 0.35f, 3.1f) - 0.5f, Mathf.PerlinNoise(7.7f, t * 0.3f) - 0.5f) * 2 * wander;
            var target = new Vector2((float)(lastSway.Right * 28), (float)(-lastSway.Up * 28 - recoil * 6000)) + drift;
            eyeShown = Vector2.Lerp(eyeShown, target, 1 - Mathf.Exp(-dt * (held ? 4 : 10)));
            ScopeMask.EyeOffset = eyeShown;
            ScopeMask.Open = scopeOpen;
        }

        /// <summary>The wind bed follows the lane wind; the heart beats while the breath is held or recovering.</summary>
        void Sounds(LaneWind wind)
        {
            float sum = 0;
            foreach (var w in wind.Zones) sum += (float)Math.Sqrt(w.X * w.X + w.Z * w.Z);
            Sound.Wind = wind.Zones.Count > 0 ? sum / wind.Zones.Count : 0;
            float strain = Strain();
            if (breath.Holding) Sound.Heart(0.15f + 0.85f * strain);
            else if (Sway.BreathFactor(breath, time, cfg) > 1.05) Sound.Heart(strain);
        }

        void OnDestroy()
        {
            // Back to the bare eye for the menus.
            PostFx.Scope(0);
            PostFx.Defocus(0);
            rig.Raise = 1;
            if (ScopeDials.Turned?.Target == (object)this) ScopeDials.Turned = null;
        }

        void SyncEnemy()
        {
            var e = State.Players[Side.B];
            if (e.Stance == enemyStanceShown && e.Position.Equals(enemyPosShown) && arena.Enemy != null) return;
            enemyStanceShown = e.Stance;
            enemyPosShown = e.Position;
            arena.PlaceEnemy(e.Position, e.Stance, Side.B);
        }

        void SyncPlayer()
        {
            var p = State.Players[Me];
            if (p.Stance == playerStanceShown && p.Position.Equals(playerPosShown) && arena.Player != null) return;
            playerStanceShown = p.Stance;
            playerPosShown = p.Position;
            rig.Body = arena.PlacePlayer(p.Position, p.Stance, Me).gameObject;
        }

        void RenderHud(LaneWind wind)
        {
            var s = State;
            string turn = s.Phase switch
            {
                PhaseKind.Shot => s.Actor == Me ? "YOUR SHOT" : "ENEMY SHOT",
                PhaseKind.Setup => "SETUP",
                PhaseKind.RoundOver => "ROUND OVER",
                PhaseKind.MatchOver => "MATCH OVER",
                _ => "MOVE",
            };
            double left = Match.TimeLeft(s, time);
            string clock = double.IsInfinity(left) || s.Phase == PhaseKind.RoundOver || s.Phase == PhaseKind.MatchOver ? "--" : $"0:{Math.Ceiling(left):00}";
            string score = $"{s.Players.A.Kills} – {s.Players.B.Kills}";
            Color turnColor = s.Actor == Me ? new Color(0.95f, 0.70f, 0.24f) : new Color(0.90f, 0.28f, 0.23f);
            bool can = CanFire() && time - lastShotAt >= CycleSeconds;
            string elev = Fmt(elevMrad, "U", "D"), windage = Fmt(windMrad, "R", "L");
            string zoomText = $"{zoom}×", focusText = ScopeDials.FocusText(focus);

            // The mid-lane wind: speed, and an arrow showing where it blows relative to our facing.
            float heading = Mathf.Repeat((float)(yaw * Mathf.Rad2Deg), 360);
            var mid = wind.Zones.Count > 1 ? wind.Zones[wind.Zones.Count / 2] : wind.Zones.Count > 0 ? wind.Zones[0] : new Vec3(0, 0, 0);
            string windSpeed = $"{Math.Sqrt(mid.X * mid.X + mid.Z * mid.Z):0.0} m/s";
            var u = Library.ToUnity(mid);
            float windAngle = Mathf.Atan2(u.x, u.z) * Mathf.Rad2Deg - heading;

            foreach (var screen in new[] { ui.Get("Hud"), ui.Get("Scope") })
            {
                screen.Text("turn", turn);
                screen.Color("turn", turnColor);
                screen.Text("timer", clock);
                screen.Text("score", score);
                screen.Text("windSpeed", windSpeed);
                screen.Text("windZone", "WIND · MID");
                if (screen.Has("windArrow")) screen["windArrow"].style.rotate = new Rotate(Angle.Degrees(windAngle));
            }

            var hud = ui.Get("Hud");
            hud.Text("hint", hintTimer > 0 ? hint : "Drag the sky to look around");
            hud.Text("cardElev", elev);
            hud.Text("cardWind", windage);
            hud.Text("cardZoom", zoomText);
            hud.Text("cardFocus", focusText);
            Marker(hud);

            var scope = ui.Get("Scope");
            scope.Text("dialZoom", zoomText);
            scope.Text("dialElev", elev);
            scope.Text("dialWind", windage);
            scope.Text("dialFocus", focusText);
            scope.Text("lrf", laserText);
            scope.Text("solution", hintTimer > 0 ? hint : "");
            scope.Show("solution", hintTimer > 0);
            scope.Show("fireTip", !fireTaught && !director);
            scope.Show("fireCancel", firePointer != null && fireCancel);
            if (scope.Has("fireBtn")) scope["fireBtn"].style.opacity = firePointer != null && fireCancel ? 0.35f : can ? 1 : 0.5f;
        }

        /// <summary>The enemy marker over their head, wherever they are on screen; hidden when behind or off it.</summary>
        void Marker(UiScreen hud)
        {
            if (!hud.Has("marker")) return;
            var marker = hud["marker"];
            var root = marker.panel?.visualTree;
            var e = State.Players[Side.B];
            var head = Library.ToUnity(Combat.EyeOf(e.Position, e.Stance, cfg)) + Vector3.up * 0.6f;
            var vp = rig.Cam.WorldToViewportPoint(head);
            bool on = Settings.RangeMarker && root != null && marker.parent != null && vp.z > 0 && vp.x > 0.02f && vp.x < 0.98f && vp.y > 0.05f && vp.y < 0.95f;
            marker.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            var wb = root!.worldBound;
            var at = marker.parent!.WorldToLocal(new Vector2(wb.xMin + vp.x * wb.width, wb.yMin + (1 - vp.y) * wb.height));
            // The diamond's foot sits on the point; the range reads above it.
            marker.style.left = at.x;
            marker.style.top = at.y;
            marker.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
        }
    }
}
