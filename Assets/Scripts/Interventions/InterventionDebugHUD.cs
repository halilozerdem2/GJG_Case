using UnityEngine;

public class InterventionDebugHUD : MonoBehaviour
{
    [SerializeField] private bool show = true;
    [SerializeField] private Vector2 position = new Vector2(10, 10);
    [SerializeField] private Vector2 size = new Vector2(320, 140);

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void OnGUI()
    {
        if (!show) return;
        var dir = InterventionDirector.Instance;
        var gate = SafeWindowGate.Instance;
        var sm = LilController.Instance != null ? LilController.Instance.StateMachine : null;

        Rect r = new Rect(position, size);
        GUILayout.BeginArea(r, GUI.skin.box);
        GUILayout.Label("[Intervention Debug]");
        if (dir != null)
        {
            GUILayout.Label($"Budget: {GetPrivate(dir, "currentBudget"):0.00}");
            GUILayout.Label($"Move#: {GetPrivate(dir, "moveIndex")}  LastCombo: {GetPrivate(dir, "lastBigComboSize")}  NearFail: {GetPrivate(dir, "nearFail")}");
        }
        GUILayout.Label($"SafeWindow: {(gate != null && gate.IsOpen ? "OPEN" : "CLOSED")}");
        if (sm != null)
        {
            GUILayout.Label($"State: {sm.CurrentStateId}  OneShotActive: {sm.IsOneShotActive}");
        }
        GUILayout.EndArea();
    }

    private static float GetPrivate(object obj, string fieldName)
    {
        var f = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (f == null) return 0f;
        object v = f.GetValue(obj);
        if (v is float fv) return fv;
        if (v is int iv) return iv;
        if (v is bool bv) return bv ? 1f : 0f;
        return 0f;
    }
}

