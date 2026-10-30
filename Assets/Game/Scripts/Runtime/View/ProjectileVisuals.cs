using System.Collections.Generic;
using Deadlock.Combat;
using Deadlock.UI;
using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// Tracer lines and impact marks for <see cref="ProjectileSystem"/>. Purely visual; pooled.
    /// <para>
    /// The player's own shots (from the hip): a short glowing dash flies on screen from the gun's muzzle, as it is
    /// drawn this frame (with the gun's kick: where the flash is), straight to where that shot really goes: its real
    /// end (its first hit, else the end of its range), as it shows on screen this frame. So each dash shows its own
    /// shot's spread and recoil (a shot with none ends at the crosshair's centre). Both ends are taken again every
    /// frame, so the dash stays on the line from the muzzle to the shot's end whatever the kick or the camera's recoil
    /// does; it is placed in the world at <see cref="DrawDepth"/> in front of the camera, which projects back onto
    /// exactly those pixels. It is <see cref="DashPoints"/> long near the gun and shorter toward the end, shows for
    /// <see cref="travelTime"/> (0.05 s; 0.05–0.08), eases out, never passes the shot's end, and over the last part
    /// shrinks into it and narrows. It is a drawing only: the hit, damage, speed, direction and range are the
    /// projectile's, read and never changed (<see cref="ProjectileSystem.TryPredictEnd"/>).
    /// While aimed down the sights the muzzle is under the sights, mid-screen: no dash is drawn (the flash only).
    /// </para>
    /// <para>
    /// Development: <see cref="DebugAimLine"/> (or DEADLOCK_DEBUG_AIM_LINE=1 at launch) draws a thin line from the
    /// muzzle to the crosshair's centre every frame, to check the two points.
    /// </para>
    /// Other shooters' tracers follow their projectile as before.
    /// </summary>
    [DefaultExecutionOrder(10000)] // after the gun's kick and the camera's recoil: the ends are where they are drawn
    public sealed class ProjectileVisuals : MonoBehaviour
    {
        [SerializeField] ProjectileSystem projectiles;
        [SerializeField] Material tracerMaterial;
        [SerializeField] Material impactMaterial;
        [SerializeField] Material targetImpactMaterial;
        [SerializeField, Min(0.1f)] float tracerLength = 4f;
        [SerializeField, Min(0.001f)] float tracerWidth = 0.025f;
        [SerializeField, Min(0.01f)] float impactSize = 0.12f;
        [SerializeField, Min(0.1f)] float impactLifetime = 6f;
        [SerializeField, Min(1)] int maxImpacts = 48;
        [Tooltip("Own shots: seconds the dash is shown, from the muzzle to the shot's end (display only).")]
        [SerializeField, Range(MinTravelTime, MaxTravelTime)] float travelTime = DefaultTravelTime;
        [SerializeField, Min(1)] int maxOwnTracers = DefaultMaxOwnTracers;

        public const float DefaultTravelTime = 0.05f, MinTravelTime = 0.05f, MaxTravelTime = 0.08f;
        /// <summary>
        /// Part of <see cref="travelTime"/> at the end in which the dash shrinks into the shot's end and narrows: the head
        /// arrives at 0.65 of the time (0.0325 s of 0.05 s), so at 60 fps the third frame already shows it at the end.
        /// </summary>
        public const float FadePart = 0.35f;
        /// <summary>Length of the dash on screen near the gun (points); it shortens to <see cref="FarDashPart"/> of that at the shot's end.</summary>
        public const float DashPoints = 14f;
        public const float FarDashPart = 0.55f;
        /// <summary>Where on screen the dash's tail first is (part of the way from the muzzle to the shot's end): just past the flash.</summary>
        public const float StartPart = 0.05f;
        /// <summary>Width of the dash on screen (points).</summary>
        public const float WidthPoints = 1.3f;
        public const int DefaultMaxOwnTracers = 6;
        /// <summary>Depth in front of the camera (m) the own dashes and the debug line are drawn at (screen-exact).</summary>
        public const float DrawDepth = 1f;

        /// <summary>Development check: a thin line from the muzzle to the crosshair's centre, every frame.</summary>
        public static bool DebugAimLine;

        struct Tracer
        {
            public LineRenderer Line;
            public Vector3 Origin;
        }

        /// <summary>An own shot's dash, timed by itself; drawn on screen from the muzzle to the shot's real end.</summary>
        sealed class OwnTracer
        {
            public int Id;
            public LineRenderer Line;
            public float Start;
            /// <summary>Where the shot really ends (its first hit, else the end of its range); the dash heads there.</summary>
            public Vector3 End;
            public bool HasEnd;
            /// <summary>Muzzle and the shot's end on screen (pixels) as used for the last drawing.</summary>
            public Vector2 MuzzleOnScreen, AimOnScreen;
            /// <summary>Dash tail and head on screen (pixels) as last drawn.</summary>
            public Vector2 TailOnScreen, HeadOnScreen;
        }

        WeaponViewModel viewModel;
        HudController hud;
        LineRenderer debugLine;

        readonly Dictionary<int, Tracer> tracers = new Dictionary<int, Tracer>();
        readonly List<OwnTracer> own = new List<OwnTracer>();
        readonly Stack<LineRenderer> linePool = new Stack<LineRenderer>();
        readonly Queue<(Transform mark, float expires)> impacts = new Queue<(Transform, float)>();

        public float TravelTime
        {
            get => travelTime;
            set => travelTime = Mathf.Clamp(value, MinTravelTime, MaxTravelTime);
        }

        public float FadeTime => travelTime * FadePart;

        /// <summary>Screen pixels per point (UI points: about 3 on an iPhone 16 Pro).</summary>
        public static float PixelsPerPoint => Screen.dpi > 0f ? Screen.dpi / 160f : 1f;

        /// <summary>The debug line's ends on screen (pixels) as last drawn; false when it is off.</summary>
        public bool TryGetDebugLine(out Vector2 muzzle, out Vector2 aim)
        {
            muzzle = debugMuzzle;
            aim = debugAim;
            return debugLine != null && debugLine.gameObject.activeSelf;
        }

        Vector2 debugMuzzle, debugAim;

        /// <summary>The line drawn for a projectile (tests): tail, head, world space.</summary>
        public bool TryGetTracer(int id, out Vector3 tail, out Vector3 head)
        {
            tail = head = default;
            var o = own.Find(t => t.Id == id);
            if (o != null)
            {
                tail = o.Line.GetPosition(0);
                head = o.Line.GetPosition(1);
                return true;
            }
            if (!tracers.TryGetValue(id, out var t))
                return false;
            tail = t.Line.GetPosition(0);
            head = t.Line.GetPosition(1);
            return true;
        }

        /// <summary>An own dash as last drawn, on screen (pixels): muzzle, the shot's end, tail, head.</summary>
        public bool TryGetOwnDash(int id, out Vector2 muzzle, out Vector2 aim, out Vector2 tail, out Vector2 head)
        {
            muzzle = aim = tail = head = default;
            var o = own.Find(t => t.Id == id);
            if (o == null)
                return false;
            muzzle = o.MuzzleOnScreen;
            aim = o.AimOnScreen;
            tail = o.TailOnScreen;
            head = o.HeadOnScreen;
            return true;
        }

        /// <summary>Where an own shot really ends (its first hit, else the end of its range): where its dash heads.</summary>
        public bool TryGetOwnTracerEnd(int id, out Vector3 end)
        {
            end = default;
            var o = own.Find(t => t.Id == id);
            if (o == null)
                return false;
            end = o.End;
            return true;
        }

        /// <summary>Width of an own shot's dash now (world units at <see cref="DrawDepth"/>; it narrows at the end).</summary>
        public float OwnTracerWidth(int id)
        {
            var o = own.Find(t => t.Id == id);
            return o != null ? o.Line.endWidth : 0f;
        }

        /// <summary>Ids of the projectiles with a tracer drawn now (tests).</summary>
        public IEnumerable<int> TracerIds
        {
            get
            {
                foreach (var o in own)
                    yield return o.Id;
                foreach (int id in tracers.Keys)
                    yield return id;
            }
        }

        public int OwnTracerCount => own.Count;

        void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (System.Environment.GetEnvironmentVariable("DEADLOCK_DEBUG_AIM_LINE") == "1")
                DebugAimLine = true;
#endif
        }

        void OnEnable()
        {
            projectiles.Spawned += OnSpawn;
            projectiles.Moved += OnMove;
            projectiles.Expired += OnExpire;
            projectiles.Hit += OnHit;
        }

        void OnDisable()
        {
            projectiles.Spawned -= OnSpawn;
            projectiles.Moved -= OnMove;
            projectiles.Expired -= OnExpire;
            projectiles.Hit -= OnHit;
        }

        void Find()
        {
            if (viewModel == null)
                viewModel = FindAnyObjectByType<WeaponViewModel>();
            if (hud == null)
                hud = FindAnyObjectByType<HudController>();
        }

        /// <summary>The crosshair's centre on screen (pixels): the HUD's, else the screen's centre.</summary>
        Vector2 AimPoint() => hud != null ? hud.CrosshairScreenPoint : new Vector2(Screen.width, Screen.height) * 0.5f;

        void OnSpawn(int id, Vector3 origin)
        {
            Find();
            var cam = viewModel != null ? viewModel.WorldCamera : null;
            bool ownShot = cam != null && (origin - cam.transform.position).sqrMagnitude < 0.05f * 0.05f;
            if (ownShot && viewModel.ShowsAdsImage)
                return; // aimed: the flash only (a dash from under the sights would sit on the sight picture)
            if (ownShot && viewModel.TryGetMuzzleScreenPoint(out _))
            {
                if (own.Count >= maxOwnTracers)
                    Release(own[0]);
                bool hasEnd = projectiles.TryPredictEnd(id, out Vector3 end, out _);
                var o = new OwnTracer { Id = id, Line = TakeLine(), Start = Time.time, End = end, HasEnd = hasEnd };
                own.Add(o);
                Draw(o, 0f);
                return;
            }
            if (ownShot)
                return; // gun not drawn (switching): no tracer
            LineRenderer line = TakeLine();
            line.SetPosition(0, origin);
            line.SetPosition(1, origin);
            tracers[id] = new Tracer { Line = line, Origin = origin };
        }

        LineRenderer TakeLine()
        {
            LineRenderer line = linePool.Count > 0 ? linePool.Pop() : CreateLine();
            line.startWidth = tracerWidth * 0.4f;
            line.endWidth = tracerWidth;
            line.gameObject.SetActive(true);
            return line;
        }

        void OnMove(int id, Vector3 head)
        {
            if (tracers.TryGetValue(id, out Tracer tracer))
                SetSegment(tracer, head);
        }

        void OnHit(ProjectileHit hit)
        {
            OnExpire(hit.ProjectileId);
            SpawnImpact(hit.Point, hit.Normal, hit.HitDamageable ? targetImpactMaterial : impactMaterial);
        }

        void OnExpire(int id)
        {
            if (!tracers.TryGetValue(id, out Tracer tracer))
                return; // own shots' dashes run on their own time
            tracers.Remove(id);
            tracer.Line.gameObject.SetActive(false);
            linePool.Push(tracer.Line);
        }

        void SetSegment(Tracer tracer, Vector3 head)
        {
            Vector3 toHead = head - tracer.Origin;
            float length = Mathf.Min(toHead.magnitude, tracerLength);
            Vector3 tail = head - toHead.normalized * length;
            tracer.Line.SetPosition(0, tail);
            tracer.Line.SetPosition(1, head);
        }

        void LateUpdate()
        {
            for (int i = own.Count - 1; i >= 0; i--)
            {
                var o = own[i];
                float age = Time.time - o.Start;
                if (age >= travelTime)
                    Release(o);
                else
                    Draw(o, age);
            }
            DrawDebugLine();
        }

        /// <summary>A screen point (pixels) in the world at <see cref="DrawDepth"/>: it projects back onto that pixel.</summary>
        static Vector3 AtDepth(Camera cam, Vector2 screen) => cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, DrawDepth));

        /// <summary>World width at <see cref="DrawDepth"/> that shows as <paramref name="points"/> on screen.</summary>
        static float WidthAtDepth(Camera cam, float points) =>
            points * PixelsPerPoint * 2f * DrawDepth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, cam.pixelHeight);

        /// <summary>
        /// Where the shot's real end shows on screen now (pixels): it carries the shot's spread and recoil. The crosshair's
        /// centre if the end is unknown or not in front of the camera.
        /// </summary>
        Vector2 EndOnScreen(Camera cam, OwnTracer o)
        {
            if (o.HasEnd)
            {
                Vector3 p = cam.WorldToScreenPoint(o.End);
                if (p.z > DrawDepth)
                    return p;
            }
            return AimPoint();
        }

        /// <summary>
        /// The dash at <paramref name="age"/> seconds, on the screen line from the muzzle (now) to the shot's end (now):
        /// its head starts a dash past the flash and eases out to the end by the last <see cref="FadePart"/>; its tail is
        /// a dash behind (shorter toward the end), never back at the muzzle; then the tail catches up and it narrows.
        /// </summary>
        void Draw(OwnTracer o, float age)
        {
            var cam = viewModel != null ? viewModel.WorldCamera : null;
            if (cam == null || !viewModel.TryGetMuzzleScreenPoint(out Vector2 muzzle))
            {
                o.Line.startWidth = o.Line.endWidth = 0f;
                return;
            }
            Vector2 aim = EndOnScreen(cam, o);
            float length = Mathf.Max(1f, Vector2.Distance(muzzle, aim));
            float Dash(float at) => Mathf.Min(1f, DashPoints * PixelsPerPoint * Mathf.Lerp(1f, FarDashPart, at) / length);
            float u = age / Mathf.Max(0.001f, travelTime);
            float travel = Mathf.Clamp01(u / (1f - FadePart));
            float dashAt0 = Dash(StartPart);
            float headAt = Mathf.Min(1f, StartPart + dashAt0 + (1f - StartPart - dashAt0) * (1f - (1f - travel) * (1f - travel)));
            float tailAt = Mathf.Max(StartPart, headAt - Dash(headAt));
            float width = 1f;
            if (u > 1f - FadePart)
            {
                float f = Mathf.Clamp01((u - (1f - FadePart)) / FadePart);
                tailAt = Mathf.Lerp(tailAt, 1f, f);
                width = 1f - f;
            }
            o.MuzzleOnScreen = muzzle;
            o.AimOnScreen = aim;
            o.TailOnScreen = Vector2.Lerp(muzzle, aim, tailAt);
            o.HeadOnScreen = Vector2.Lerp(muzzle, aim, headAt);
            o.Line.SetPosition(0, AtDepth(cam, o.TailOnScreen));
            o.Line.SetPosition(1, AtDepth(cam, o.HeadOnScreen));
            float w = WidthAtDepth(cam, WidthPoints) * width;
            o.Line.startWidth = w * 0.5f;
            o.Line.endWidth = w;
        }

        void DrawDebugLine()
        {
            Find();
            var cam = viewModel != null ? viewModel.WorldCamera : null;
            bool show = DebugAimLine && cam != null && viewModel.TryGetMuzzleScreenPoint(out debugMuzzle);
            if (!show)
            {
                if (debugLine != null)
                    debugLine.gameObject.SetActive(false);
                return;
            }
            if (debugLine == null)
            {
                debugLine = CreateLine();
                debugLine.name = "DebugAimLine";
            }
            debugLine.gameObject.SetActive(true);
            debugAim = AimPoint();
            debugLine.SetPosition(0, AtDepth(cam, debugMuzzle));
            debugLine.SetPosition(1, AtDepth(cam, debugAim));
            debugLine.startWidth = debugLine.endWidth = WidthAtDepth(cam, 0.8f);
        }

        void Release(OwnTracer o)
        {
            own.Remove(o);
            o.Line.gameObject.SetActive(false);
            linePool.Push(o.Line);
        }

        LineRenderer CreateLine()
        {
            var go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = tracerMaterial;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = tracerWidth * 0.4f;
            line.endWidth = tracerWidth;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        void Update()
        {
            while (impacts.Count > 0 && (impacts.Peek().expires <= Time.time || impacts.Count > maxImpacts))
            {
                var (mark, _) = impacts.Dequeue();
                if (mark != null)
                    Destroy(mark.gameObject);
            }
        }

        static Mesh quadMesh;

        /// <summary>1 x 1 quad facing -Z (same as Unity's built-in quad), shared by every impact mark.</summary>
        static Mesh QuadMesh
        {
            get
            {
                if (quadMesh != null)
                    return quadMesh;
                quadMesh = new Mesh { name = "ImpactQuad" };
                quadMesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) });
                quadMesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) });
                quadMesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
                quadMesh.SetTriangles(new[] { 0, 3, 1, 3, 0, 2 }, 0);
                quadMesh.RecalculateBounds();
                return quadMesh;
            }
        }

        void SpawnImpact(Vector3 point, Vector3 normal, Material material)
        {
            if (material == null)
                return;
            // Built directly (not GameObject.CreatePrimitive): a primitive quad comes with a MeshCollider, and in
            // device builds the unused MeshCollider class is stripped, so CreatePrimitive logged an error per impact.
            var quad = new GameObject("Impact");
            quad.AddComponent<MeshFilter>().sharedMesh = QuadMesh;
            var renderer = quad.AddComponent<MeshRenderer>();
            quad.transform.SetParent(transform, true);
            quad.transform.SetPositionAndRotation(point + normal * 0.01f, Quaternion.LookRotation(-normal));
            quad.transform.localScale = Vector3.one * impactSize;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            impacts.Enqueue((quad.transform, Time.time + impactLifetime));
        }
    }
}
