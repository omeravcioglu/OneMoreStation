using UnityEngine;
using UnityEngine.InputSystem;

namespace Metro
{
    /// <summary>Orthographic map camera: smooth zoom toward the cursor, drag/keyboard panning and an overview toggle.</summary>
    public sealed class CameraController
    {
        public readonly Camera cam;
        Rect limits = new Rect(0f, 0f, 64f, 44f);
        Vector2 targetPos;
        float targetSize = 7f;
        public float minSize = 1.1f, maxSize = 26f;
        bool overview;
        Vector2 savedPos;
        float savedSize;
        Vector2 lastMouse;
        bool panning;
        public bool inputEnabled = true;
        public bool keyboardEnabled = true;
        public bool edgePan;

        /// <summary>Distance moved by the camera this frame in pixels (tools use it to tell clicks from drags).</summary>
        public float dragPixels;

        public CameraController(Camera cam)
        {
            this.cam = cam;
            targetPos = cam.transform.position;
            targetSize = cam.orthographicSize;
            PublishScale();
        }

        /// <summary>Tells the vector shader (and StrokeWidth) the current pixel size and reference-pixel scale.</summary>
        void PublishScale()
        {
            float s = Mathf.Max(1, Screen.height) / 1080f;
            StrokeWidth.PxScale = s;
            Shader.SetGlobalFloat("_PxScale", s);
            Shader.SetGlobalFloat("_PixelWorldSize", PixelWorldSize);
        }

        public float PixelWorldSize => cam.orthographicSize * 2f / Mathf.Max(1, Screen.height);
        public float Size => cam.orthographicSize;
        public Vector2 Position => cam.transform.position;
        public bool InOverview => overview;

        public void Bind(CityModel city)
        {
            overview = false;
            SetLimits(Rect.MinMaxRect(Mathf.Max(0f, city.playArea.xMin), Mathf.Max(0f, city.playArea.yMin),
                Mathf.Min(city.width, city.playArea.xMax), Mathf.Min(city.height, city.playArea.yMax)));
        }

        /// <summary>The camera stays over the play area and can zoom out just far enough to see all of it.</summary>
        public void SetLimits(Rect area)
        {
            limits = area;
            float byH = area.height * 0.5f;
            float byW = area.width * 0.5f / Mathf.Max(0.2f, cam.aspect);
            maxSize = Mathf.Max(minSize + 0.5f, Mathf.Max(byH, byW) * 1.06f);
            if (targetSize > maxSize) targetSize = maxSize;
        }

        public Rect Limits => limits;

        public Vector2 ScreenToWorld(Vector2 screen)
        {
            Vector3 w = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
            return new Vector2(w.x, w.y);
        }

        public Vector2 WorldToScreen(Vector2 world)
        {
            Vector3 s = cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
            return new Vector2(s.x, s.y);
        }

        public void FocusOn(Vector2 p, float size = -1f, bool instant = false)
        {
            targetPos = p;
            if (size > 0f) targetSize = Mathf.Clamp(size, minSize, maxSize);
            overview = false;
            ClampTarget();
            if (instant) Snap();
        }

        public void Snap()
        {
            cam.transform.position = new Vector3(targetPos.x, targetPos.y, -10f);
            cam.orthographicSize = targetSize;
        }

        /// <summary>Size that fits a rect, not limited by the current zoom-out limit (the map may be growing).</summary>
        public float SizeToFitArea(Rect r, float padding = 1.04f)
        {
            float byH = r.height * 0.5f;
            float byW = r.width * 0.5f / Mathf.Max(0.2f, cam.aspect);
            return Mathf.Max(minSize, Mathf.Max(byH, byW) * padding);
        }

        public float SizeToFit(Rect r, float padding = 1.12f)
        {
            float byH = r.height * 0.5f;
            float byW = r.width * 0.5f / Mathf.Max(0.2f, cam.aspect);
            return Mathf.Clamp(Mathf.Max(byH, byW) * padding, minSize, maxSize);
        }

        public void ToggleOverview(Rect fit)
        {
            if (overview)
            {
                targetPos = savedPos;
                targetSize = savedSize;
                overview = false;
            }
            else
            {
                savedPos = targetPos;
                savedSize = targetSize;
                targetPos = fit.center;
                targetSize = SizeToFit(fit, 1.08f);
                overview = true;
            }
            ClampTarget();
        }

        void ClampTarget()
        {
            float m = 0.5f;
            targetPos.x = Mathf.Clamp(targetPos.x, limits.xMin - m, limits.xMax + m);
            targetPos.y = Mathf.Clamp(targetPos.y, limits.yMin - m, limits.yMax + m);
            targetSize = Mathf.Clamp(targetSize, minSize, maxSize);
        }

        /// <summary>Per-frame update. pointerOverUI blocks wheel zoom; leftPan allows left-drag panning.</summary>
        /// <summary>True on frames where the player moved the view by hand (drag, keys or screen edge), not by zooming.</summary>
        public bool UserPanned { get; private set; }

        public void Tick(float dt, bool pointerOverUI, bool leftPan)
        {
            dragPixels = 0f;
            UserPanned = false;
            Vector2 mouse = InputX.MousePos;
            if (inputEnabled)
            {
                float scroll = pointerOverUI ? 0f : InputX.Scroll;
                if (Mathf.Abs(scroll) > 0.001f)
                {
                    float factor = Mathf.Pow(0.85f, Mathf.Clamp(scroll, -4f, 4f));
                    float newSize = Mathf.Clamp(targetSize * factor, minSize, maxSize);
                    Vector2 ndc = new Vector2(mouse.x / Mathf.Max(1, Screen.width) * 2f - 1f, mouse.y / Mathf.Max(1, Screen.height) * 2f - 1f);
                    Vector2 world = targetPos + new Vector2(ndc.x * targetSize * cam.aspect, ndc.y * targetSize);
                    targetPos = world - new Vector2(ndc.x * newSize * cam.aspect, ndc.y * newSize);
                    targetSize = newSize;
                    overview = false;
                }

                bool panHeld = InputX.RightHeld || InputX.MiddleHeld || (leftPan && InputX.LeftHeld);
                bool panStart = !pointerOverUI && (InputX.RightDown || InputX.MiddleDown || (leftPan && InputX.LeftDown));
                if (panStart)
                {
                    panning = true;
                    lastMouse = mouse;
                }
                if (panning && panHeld)
                {
                    Vector2 d = mouse - lastMouse;
                    if (d.sqrMagnitude > 0f)
                    {
                        float pw = PixelWorldSize;
                        targetPos -= d * pw;
                        Vector3 p = cam.transform.position;
                        cam.transform.position = new Vector3(p.x - d.x * pw, p.y - d.y * pw, p.z);
                        dragPixels = d.magnitude;
                        overview = false;
                        UserPanned = true;
                    }
                    lastMouse = mouse;
                }
                else panning = false;

                if (keyboardEnabled)
                {
                    Vector2 move = Vector2.zero;
                    if (InputX.Key(Key.W) || InputX.Key(Key.UpArrow)) move.y += 1f;
                    if (InputX.Key(Key.S) || InputX.Key(Key.DownArrow)) move.y -= 1f;
                    if (InputX.Key(Key.A) || InputX.Key(Key.LeftArrow)) move.x -= 1f;
                    if (InputX.Key(Key.D) || InputX.Key(Key.RightArrow)) move.x += 1f;
                    if (edgePan && Application.isFocused && !pointerOverUI)
                    {
                        if (mouse.x < 4) move.x -= 1f;
                        if (mouse.x > Screen.width - 5) move.x += 1f;
                        if (mouse.y < 4) move.y -= 1f;
                        if (mouse.y > Screen.height - 5) move.y += 1f;
                    }
                    if (move.sqrMagnitude > 0f)
                    {
                        targetPos += move.normalized * targetSize * 1.4f * dt;
                        overview = false;
                        UserPanned = true;
                    }
                    if (InputX.Key(Key.Equals) || InputX.Key(Key.NumpadPlus)) targetSize *= Mathf.Pow(0.4f, dt);
                    if (InputX.Key(Key.Minus) || InputX.Key(Key.NumpadMinus)) targetSize *= Mathf.Pow(2.5f, dt);
                }
            }
            ClampTarget();

            float k = 1f - Mathf.Exp(-dt * 11f);
            Vector3 cp = cam.transform.position;
            Vector2 pos = Vector2.Lerp(new Vector2(cp.x, cp.y), targetPos, k);
            cam.transform.position = new Vector3(pos.x, pos.y, -10f);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetSize, k);
            PublishScale();
        }

        /// <summary>Slow cinematic drift used behind the main menu.</summary>
        public void Drift(float dt, Vector2 center, float radius, float size, float t)
        {
            targetPos = center + new Vector2(Mathf.Cos(t * 0.05f), Mathf.Sin(t * 0.037f)) * radius;
            targetSize = size + Mathf.Sin(t * 0.03f) * size * 0.08f;
            float k = 1f - Mathf.Exp(-dt * 0.8f);
            Vector3 cp = cam.transform.position;
            Vector2 pos = Vector2.Lerp(new Vector2(cp.x, cp.y), targetPos, k);
            cam.transform.position = new Vector3(pos.x, pos.y, -10f);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetSize, k);
            PublishScale();
        }
    }
}
