using System.Collections;
using UnityEngine;

public class ColorClearLineEffect : MonoBehaviour
{
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private float fadeDuration = 0.1f;

    private static Material sharedMaterial;
    private Vector3 startPoint;
    private Vector3 endPoint;
    private float travelTime = 0.1f;
    private float elapsed;
    private System.Action onArrive;
    private bool playing;

    public static ColorClearLineEffect Create(Transform parent)
    {
        GameObject go = new GameObject("ColorClearLine");
        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }
        return go.AddComponent<ColorClearLineEffect>();
    }

    private void Awake()
    {
        if (lineRenderer == null)
        {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
        }

        if (sharedMaterial == null)
        {
            sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        lineRenderer.material = sharedMaterial;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.alignment = LineAlignment.TransformZ;
        lineRenderer.numCapVertices = 4;
        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        lineRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        lineRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        lineRenderer.sortingOrder = 50;
    }

    public void Play(Vector3 start, Vector3 end, Color color, float width, float speed, System.Action onComplete)
    {
        startPoint = start;
        endPoint = end;
        onArrive = onComplete;
        elapsed = 0f;
        float distance = Vector3.Distance(startPoint, endPoint);
        travelTime = Mathf.Max(0.01f, distance / Mathf.Max(0.01f, speed));
        playing = true;

        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, startPoint);
        lineRenderer.SetPosition(1, startPoint);
    }

    private void Update()
    {
        if (!playing)
        {
            return;
        }

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / travelTime);
        Vector3 tip = Vector3.Lerp(startPoint, endPoint, t);
        lineRenderer.SetPosition(1, tip);

        if (t >= 1f)
        {
            playing = false;
            onArrive?.Invoke();
            StartCoroutine(FadeOut());
        }
    }

    private IEnumerator FadeOut()
    {
        float duration = Mathf.Max(0.01f, fadeDuration);
        float timer = 0f;
        Color startColor = lineRenderer.startColor;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            Color c = startColor;
            c.a = Mathf.Lerp(startColor.a, 0f, t);
            lineRenderer.startColor = c;
            lineRenderer.endColor = c;
            yield return null;
        }

        Destroy(gameObject);
    }
}
