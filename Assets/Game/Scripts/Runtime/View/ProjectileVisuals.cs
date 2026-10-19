using System.Collections.Generic;
using Deadlock.Combat;
using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// Tracer lines and impact marks for <see cref="ProjectileSystem"/>. Purely visual; pooled.
    /// </summary>
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

        struct Tracer
        {
            public LineRenderer Line;
            public Vector3 Origin;
        }

        readonly Dictionary<int, Tracer> tracers = new Dictionary<int, Tracer>();
        readonly Stack<LineRenderer> linePool = new Stack<LineRenderer>();
        readonly Queue<(Transform mark, float expires)> impacts = new Queue<(Transform, float)>();

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

        void OnSpawn(int id, Vector3 origin)
        {
            LineRenderer line = linePool.Count > 0 ? linePool.Pop() : CreateLine();
            line.gameObject.SetActive(true);
            line.SetPosition(0, origin);
            line.SetPosition(1, origin);
            tracers[id] = new Tracer { Line = line, Origin = origin };
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
                return;
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

        void SpawnImpact(Vector3 point, Vector3 normal, Material material)
        {
            if (material == null)
                return;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "Impact";
            quad.transform.SetParent(transform, true);
            quad.transform.SetPositionAndRotation(point + normal * 0.01f, Quaternion.LookRotation(-normal));
            quad.transform.localScale = Vector3.one * impactSize;
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            impacts.Enqueue((quad.transform, Time.time + impactLifetime));
        }
    }
}
