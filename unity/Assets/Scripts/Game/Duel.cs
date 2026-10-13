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
    /// and scope screens, and shows each shot (tracer, splash, hit call). Ported from the web
    /// client's duelMode.ts and shooter.ts.
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

        int? lookPointer;
        Vector2 lookLast;
        bool zoomDragging;
        bool director;

        public static Duel Begin(GameConfig cfg, UiHost ui, CameraRig rig, Arena arena, string playerName, string rifleId, BotDifficulty bot)
        {
            var d = new GameObject("Duel").AddComponent<Duel>();
            d.cfg = cfg;
            d.ui = ui;
            d.rig = rig;
            d.arena = arena;
            d.map = arena.Map;
            uint seed = (uint)(DateTime.UtcNow.Ticks ^ UnityEngine.Random.Range(0, int.MaxValue)) | 1;
            var botRifle = cfg.Rifles.Count > 1 ? cfg.Rifles[1].Id : rifleId;
            d.State = Match.CreateMatch(seed, arena.Map.Id, new PerSide<PlayerSetup>(
                new PlayerSetup(playerName, rifleId),
                new PlayerSetup(BotName(bot), botRifle, bot)), 0, cfg);
            d.zoom = (int)Math.Round((cfg.Scope.MinZoom + cfg.Scope.MaxZoom) / 2);
            d.BindScreens();
            d.EnterPhase();
            d.LookAtEnemy();
            d.ShowHud();
            return d;
        }

        static string BotName(BotDifficulty d) => d switch
        {
            BotDifficulty.Easy => "RECRUIT",
            BotDifficulty.Hard => "GHOSTWIND",
            _ => "VIPER",
        };

        // ---- screens -------------------------------------------------------

        void BindScreens()
        {
            var hud = ui.Get("Hud");
            hud.On("scope", () => SetScoped(true));
            hud.On("fire", Fire);
            hud.On("lrf", Laser);
            hud.On("dope", () => Call(Solution()));
            hud.On("pause", Quit);
            hud.On("prone", () => Call("Stance is fixed on this map"));
            hud.On("crouch", () => Call("Stance is fixed on this map"));
            hud.OnHold("breath", SetBreath);
            Look(hud.Root);

            var scope = ui.Get("Scope");
            scope.On("exit", () => SetScoped(false));
            scope.On("fire", Fire);
            scope.On("elevUp", () => Dial(ref elevMrad, 1, cfg.Scope.MaxElevationMrad, -2));
            scope.On("elevDown", () => Dial(ref elevMrad, -1, cfg.Scope.MaxElevationMrad, -2));
            scope.On("windRight", () => Dial(ref windMrad, 1, cfg.Scope.MaxWindageMrad, -cfg.Scope.MaxWindageMrad));
            scope.On("windLeft", () => Dial(ref windMrad, -1, cfg.Scope.MaxWindageMrad, -cfg.Scope.MaxWindageMrad));
            scope.OnHold("breath", SetBreath);
            Look(scope.Root);
            if (scope.Has("zoomTrack"))
            {
                var track = scope["zoomTrack"];
                track.RegisterCallback<PointerDownEvent>(e => { zoomDragging = true; track.CapturePointer(e.pointerId); ZoomFrom(track, e.localPosition); e.StopPropagation(); });
                track.RegisterCallback<PointerMoveEvent>(e => { if (zoomDragging) ZoomFrom(track, e.localPosition); });
                track.RegisterCallback<PointerUpEvent>(e => { zoomDragging = false; track.ReleasePointer(e.pointerId); });
            }
        }

        void ZoomFrom(VisualElement track, Vector3 local)
        {
            float h = Mathf.Max(1, track.layout.height);
            float f = 1 - Mathf.Clamp01(local.y / h);
            zoom = (int)Math.Round(cfg.Scope.MinZoom + f * (cfg.Scope.MaxZoom - cfg.Scope.MinZoom));
        }

        /// <summary>Drag on empty screen to aim; scaled to the field of view so the scope stays usable.</summary>
        void Look(VisualElement root)
        {
            root.pickingMode = PickingMode.Position;
            root.RegisterCallback<PointerDownEvent>(e =>
            {
                for (var t = e.target as VisualElement; t != null && t != root; t = t.parent)
                    if (t.ClassListContains(Layout.ActionClass)) return;
                lookPointer = e.pointerId;
                lookLast = e.position;
                root.CapturePointer(e.pointerId);
            });
            root.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (lookPointer != e.pointerId) return;
                Vector2 p = e.position;
                var d = p - lookLast;
                lookLast = p;
                double radPerUnit = Fov() * Mathf.Deg2Rad / UiHost.RefHeight * 0.9;
                yaw += d.x * radPerUnit;
                pitch = Math.Max(-0.3, Math.Min(0.3, pitch + d.y * radPerUnit));
            });
            root.RegisterCallback<PointerUpEvent>(e =>
            {
                if (lookPointer != e.pointerId) return;
                lookPointer = null;
                root.ReleasePointer(e.pointerId);
            });
        }

        void ShowHud()
        {
            scoped = false;
            ui.Show("Hud");
        }

        void SetScoped(bool on)
        {
            scoped = on;
            ui.Show(on ? "Scope" : "Hud");
        }

        void Dial(ref double value, int clicks, double max, double min)
        {
            double click = cfg.Scope.ClickMrad;
            double v = Math.Round(value / click + clicks) * click;
            value = Math.Min(max, Math.Max(min, Math.Round(v, 4)));
            Haptics.Click();
        }

        void SetBreath(bool hold) =>
            breath = hold ? Sway.HoldBreath(breath, time, cfg) : Sway.ReleaseBreath(breath, time, cfg);

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
                Call(s.Actor == Me ? "Your shot. Dial, hold your breath, squeeze." : "Enemy shot. Stay still.");
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
            if (mine)
            {
                recoil = 0.004;
                Haptics.Recoil();
            }
            else if (o.Verdict != Verdict.Miss) Haptics.Struck(o.Verdict == Verdict.Kill);
            else if (o.NearMiss) Haptics.Snap();

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
            if (cinematic) PlayBulletCam(o, before);
        }

        /// <summary>Every hit you land replays from behind the bullet; the match waits for it.</summary>
        void PlayBulletCam(ShotOutcome o, MatchState before)
        {
            var next = resume;
            resume = () => { };
            resumeAt = double.MaxValue;
            director = true;
            var k = ui.Show("Killcam");
            var a = before.Players[Me];
            var b = before.Players[Side.B];
            k.Text("who", $"ROUND {before.Round + 1} · {a.Name} → {b.Name}");
            var last = o.Trajectory.Points[o.Trajectory.Points.Count - 1];
            k.Text("tof", $"{o.Impact.T:0.00} s");
            k.Text("energy", $"{o.Impact.Energy / 1000:0.0} kJ");
            k.Text("velocity", $"{o.Impact.Speed:0} m/s");
            k.Text("drop", $"{Math.Abs(last.Y):0.00} m");
            k.Text("drift", $"{Math.Abs(last.Z):0.00} m {(last.Z >= 0 ? "R" : "L")}");
            double dist = Math.Sqrt(Sq(o.Impact.Point.X - o.Eye.X) + Sq(o.Impact.Point.Z - o.Eye.Z));
            var cam = BulletCam.Play(o, rig, arena.Root, (f, speed) =>
            {
                k.Fill("progress", f);
                if (k.Has("knob")) k["knob"].style.left = Length.Percent(f * 100);
                k.Text("speed", $"{speed:0.00}×");
                k.Text("distance", $"{dist * f:0} / {dist:0} m");
            }, () =>
            {
                director = false;
                SetScoped(scoped);
                resume = next;
                resumeAt = time;
            });
            k.On("skip", cam.Finish);
            k.On("pause", () => cam.Paused = !cam.Paused);
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
            lastShotAt = time;
            var aim = TrueAim();
            Apply(MatchInput.FireShot(Me, new FireInput
            {
                Yaw = aim.Yaw,
                Pitch = aim.Pitch,
                DialElevationMrad = elevMrad,
                DialWindageMrad = windMrad,
            }, time));
        }

        void Laser()
        {
            var e = State.Players[Side.B];
            var targets = new List<Silhouette> { Combat.SilhouetteFor("B", e.Position, e.Stance, Combat.FacingYaw(Side.B), cfg) };
            var d = Hits.Rangefind(Eye(), TrueAim(), targets, map.Ground, 3000, map.Cover);
            Call(d.HasValue ? $"Laser: {d.Value:0} m" : "Laser: no return");
            if (d.HasValue)
            {
                ui.Get("Scope").Text("lrf", $"LRF {d.Value:0} m");
                ui.Get("Hud").Text("markerRange", $"{d.Value:0} m");
            }
        }

        string Solution()
        {
            var e = State.Players[Side.B];
            double range = Math.Abs(e.Position.Z - State.Players[Me].Position.Z);
            return $"Range {range:0} m · dial {Fmt(elevMrad, "U", "D")} · {Fmt(windMrad, "R", "L")}";
        }

        static string Fmt(double mrad, string pos, string neg) =>
            Math.Abs(mrad) < 0.05 ? "0.0" : $"{(mrad > 0 ? pos : neg)} {Math.Abs(mrad):0.0}";

        // ---- frame ---------------------------------------------------------

        void Update()
        {
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

            var aim = TrueAim();
            if (!director) rig.Current = CameraRig.Mode.Eye;
            rig.Eye = Library.ToUnity(Eye());
            rig.Yaw = (float)(aim.Yaw * Mathf.Rad2Deg);
            rig.Pitch = (float)(aim.Pitch * Mathf.Rad2Deg);
            rig.Fov = (float)Fov();
            rig.ThirdPerson = !scoped;

            var wind = Match.ActiveConditions(State).Wind;
            arena.SetWind(wind);
            SyncEnemy();
            SyncPlayer();
            RenderHud(wind);
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

            var hud = ui.Get("Hud");
            hud.Text("turn", turn);
            hud.Color("turn", turnColor);
            hud.Text("timer", clock);
            hud.Text("round", Match.IsSuddenDeath(s, cfg) ? $"ROUND {s.Round + 1} · SUDDEN DEATH" : $"ROUND {s.Round + 1} · FIRST TO {cfg.Rules.KillsToWin}");
            hud.Text("enemy", s.Players.B.Name);
            hud.Text("hint", hintTimer > 0 ? hint : "Drag to aim. Scope in to dial and fire.");
            hud.Text("cardDial", $"{Fmt(elevMrad, "UP", "DN")} · {Fmt(windMrad, "R", "L")}");
            double range = Math.Abs(s.Players.B.Position.Z - s.Players.A.Position.Z);
            hud.Text("cardTarget", $"{range:0} m");
            hud.Text("cardAngle", $"{pitch * Mathf.Rad2Deg:+0.0;−0.0}°");
            float heading = Mathf.Repeat((float)(yaw * Mathf.Rad2Deg), 360);
            hud.Text("heading", $"{heading:000}°");
            hud["fireBtn"].style.opacity = can ? 1 : 0.45f;

            // Wind in the three lane zones, as speed across the lane and an arrow showing where it blows.
            for (int i = 0; i < 3 && i < wind.Zones.Count; i++)
            {
                var w = wind.Zones[i];
                float speed = (float)Math.Sqrt(w.X * w.X + w.Z * w.Z);
                hud.Text("wind" + i, speed.ToString("0.0"));
                if (hud.Has("windArrow" + i))
                {
                    // Arrow art points right at 90°; screen angle of the wind relative to our facing.
                    var u = Library.ToUnity(w);
                    float ang = Mathf.Atan2(u.x, u.z) * Mathf.Rad2Deg - heading;
                    hud["windArrow" + i].style.rotate = new Rotate(Angle.Degrees(ang));
                }
            }

            var scope = ui.Get("Scope");
            scope.Text("turn", turn);
            scope.Color("turn", turnColor);
            scope.Text("timer", clock);
            scope.Text("score", score);
            scope.Text("elev", elevMrad == 0 ? "0.0" : $"{(elevMrad > 0 ? "U" : "D")} {Math.Abs(elevMrad):0.0}");
            scope.Text("windage", Fmt(windMrad, "R", "L"));
            scope.Text("zoom", zoom.ToString());
            float zf = (float)((zoom - cfg.Scope.MinZoom) / (cfg.Scope.MaxZoom - cfg.Scope.MinZoom));
            if (scope.Has("zoomFill")) scope["zoomFill"].style.height = Length.Percent(zf * 100);
            if (scope.Has("zoomKnob")) scope["zoomKnob"].style.bottom = new StyleLength(new Length(zf * 100, LengthUnit.Percent));
            if (scope.Has("zoomKnob")) scope["zoomKnob"].style.marginBottom = -14;
            var mid = wind.Zones.Count > 1 ? wind.Zones[wind.Zones.Count / 2] : wind.Zones.Count > 0 ? wind.Zones[0] : new Vec3(0, 0, 0);
            scope.Text("windRead", $"{Math.Sqrt(mid.X * mid.X + mid.Z * mid.Z):0.0} m/s");
            scope.Text("solutionLabel", $"Range {range:0} m");
            scope.Text("solution", hintTimer > 0 ? hint : "");
            double bf = Sway.BreathFactor(breath, time, cfg);
            float steady = Mathf.Clamp01((float)(1.6 - bf) / 1.2f);
            scope.Fill("breath", steady);
            scope.Text("breathText", breath.Holding ? (bf < 1 ? "STEADY" : "STRAINING") : bf > 1.05 ? "RECOVERING" : "BREATHE");
            scope["fireBtn"].style.opacity = can ? 1 : 0.45f;
        }
    }
}
