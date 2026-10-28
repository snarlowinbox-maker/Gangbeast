using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using BeefCity.Ragdoll;

namespace BeefCity.Core
{
    /// <summary>Backspace toggles a wireframe view of every ragdoll collider (the "hitbox debugger").</summary>
    public class HitboxDebugger : MonoBehaviour
    {
        public bool Show;

        static Material _mat;
        readonly List<Collider> _colliders = new List<Collider>();
        float _scanT;

        void OnEnable()
        {
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
        }

        void Update()
        {
            if (!Show) return;
            _scanT -= Time.unscaledDeltaTime;
            if (_scanT > 0f) return;
            _scanT = 0.5f;

            _colliders.Clear();
            foreach (RagdollRig rig in FindObjectsByType<RagdollRig>(FindObjectsSortMode.None))
                _colliders.AddRange(rig.GetComponentsInChildren<Collider>());
        }

        // Built-in render pipeline
        void OnRenderObject()
        {
            if (GraphicsSettings.currentRenderPipeline == null) Draw(Camera.current);
        }

        // URP / SRP
        void OnEndCamera(ScriptableRenderContext context, Camera cam)
        {
            Draw(cam);
        }

        void Draw(Camera cam)
        {
            if (!Show || cam == null || cam.cameraType != CameraType.Game) return;
            EnsureMaterial();
            _mat.SetPass(0);

            GL.PushMatrix();
            GL.Begin(GL.LINES);
            foreach (Collider c in _colliders)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                GL.Color(c.isTrigger ? new Color(1f, 0.3f, 0.3f) : new Color(0.2f, 1f, 0.3f));
                if (c is BoxCollider b) DrawBox(b);
                else if (c is SphereCollider s) DrawSphere(s);
                else if (c is CapsuleCollider cap) DrawCapsule(cap);
            }
            GL.End();
            GL.PopMatrix();
        }

        static void EnsureMaterial()
        {
            if (_mat != null) return;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull", (int)CullMode.Off);
            _mat.SetInt("_ZWrite", 0);
            _mat.SetInt("_ZTest", (int)CompareFunction.Always); // draw through walls
        }

        static void Line(Vector3 a, Vector3 b)
        {
            GL.Vertex(a);
            GL.Vertex(b);
        }

        static void DrawBox(BoxCollider b)
        {
            Matrix4x4 m = b.transform.localToWorldMatrix * Matrix4x4.Translate(b.center);
            Vector3 h = b.size * 0.5f;
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++)
                p[i] = m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
            for (int i = 0; i < 8; i++)
            {
                if ((i & 1) == 0) Line(p[i], p[i | 1]);
                if ((i & 2) == 0) Line(p[i], p[i | 2]);
                if ((i & 4) == 0) Line(p[i], p[i | 4]);
            }
        }

        static void DrawSphere(SphereCollider s)
        {
            Vector3 scale = s.transform.lossyScale;
            float r = s.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 c = s.transform.TransformPoint(s.center);
            Circle(c, Vector3.right, Vector3.up, r);
            Circle(c, Vector3.right, Vector3.forward, r);
            Circle(c, Vector3.up, Vector3.forward, r);
        }

        static void DrawCapsule(CapsuleCollider cap)
        {
            Transform t = cap.transform;
            Vector3 scale = t.lossyScale;
            Vector3 axisLocal = cap.direction == 0 ? Vector3.right : cap.direction == 1 ? Vector3.up : Vector3.forward;
            float axisScale = cap.direction == 0 ? Mathf.Abs(scale.x) : cap.direction == 1 ? Mathf.Abs(scale.y) : Mathf.Abs(scale.z);
            float radial = Mathf.Max(
                cap.direction == 0 ? 0f : Mathf.Abs(scale.x),
                Mathf.Max(cap.direction == 1 ? 0f : Mathf.Abs(scale.y), cap.direction == 2 ? 0f : Mathf.Abs(scale.z)));
            float r = cap.radius * radial;
            float half = Mathf.Max(0f, cap.height * axisScale * 0.5f - r);

            Vector3 center = t.TransformPoint(cap.center);
            Vector3 axis = t.TransformDirection(axisLocal).normalized;
            Vector3 u = Vector3.Cross(axis, Vector3.up);
            if (u.sqrMagnitude < 0.001f) u = Vector3.Cross(axis, Vector3.right);
            u.Normalize();
            Vector3 v = Vector3.Cross(axis, u);

            Vector3 a = center + axis * half, b = center - axis * half;
            Circle(a, u, v, r);
            Circle(b, u, v, r);
            Circle(a, u, axis, r);
            Circle(b, u, axis, r);
            Line(a + u * r, b + u * r);
            Line(a - u * r, b - u * r);
            Line(a + v * r, b + v * r);
            Line(a - v * r, b - v * r);
        }

        static void Circle(Vector3 center, Vector3 u, Vector3 v, float r)
        {
            const int segments = 20;
            Vector3 prev = center + u * r;
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r;
                Line(prev, next);
                prev = next;
            }
        }
    }
}
