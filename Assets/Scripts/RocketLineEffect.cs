using System.Collections;
using UnityEngine;

/// <summary>
/// Simple helper that animates a bidirectional line from the blast origin
/// towards two targets and fades the line out once both ends arrive.
/// </summary>
public class RocketLineEffect : MonoBehaviour
{
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private float defaultFadeDuration = 0.12f;

    private static Material sharedMaterial;

    private Vector3 origin;
    private Vector3 firstTarget;
    private Vector3 secondTarget;
    private float firstTravelTime;
    private float secondTravelTime;
    private float elapsed;
    private float fadeDuration;
    private bool animating;

    public static RocketLineEffect Create(Transform parent)
    {
        GameObject go = new GameObject("RocketLineEffect");
        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }

        return go.AddComponent<RocketLineEffect>();
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
        lineRenderer.positionCount = 3;
        lineRenderer.useWorldSpace = true;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        lineRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        lineRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        lineRenderer.sortingOrder = 60;
    }

    public void Play(Vector3 start, Vector3 targetA, Vector3 targetB, Color color, float width, float travelSpeed,
        float fadeOutDuration)
    {
        StopAllCoroutines();

        origin = start;
        firstTarget = targetA;
        secondTarget = targetB;
        fadeDuration = Mathf.Max(0.01f, fadeOutDuration > 0f ? fadeOutDuration : defaultFadeDuration);
        float speed = Mathf.Max(0.01f, travelSpeed);
        firstTravelTime = Vector3.Distance(start, targetA) / speed;
        secondTravelTime = Vector3.Distance(start, targetB) / speed;
        elapsed = 0f;
        animating = true;

        lineRenderer.positionCount = 3;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, start);
        lineRenderer.SetPosition(2, start);
    }

    private void Update()
    {
        if (!animating)
        {
            return;
        }

        elapsed += Time.deltaTime;
        bool firstDone = AdvanceTowards(0, firstTarget, firstTravelTime);
        bool secondDone = AdvanceTowards(2, secondTarget, secondTravelTime);
        lineRenderer.SetPosition(1, origin);

        if (firstDone && secondDone)
        {
            animating = false;
            StartCoroutine(FadeOut());
        }
    }

    private bool AdvanceTowards(int index, Vector3 target, float travelTime)
    {
        if (travelTime <= 0.0001f)
        {
            lineRenderer.SetPosition(index, target);
            return true;
        }

        float t = Mathf.Clamp01(elapsed / travelTime);
        Vector3 position = Vector3.Lerp(origin, target, t);
        lineRenderer.SetPosition(index, position);
        return t >= 1f;
    }

    private IEnumerator FadeOut()
    {
        float timer = 0f;
        Color startColor = lineRenderer.startColor;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / fadeDuration);
            Color color = Color.Lerp(startColor, new Color(startColor.r, startColor.g, startColor.b, 0f), t);
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            yield return null;
        }

        Destroy(gameObject);
    }
}
