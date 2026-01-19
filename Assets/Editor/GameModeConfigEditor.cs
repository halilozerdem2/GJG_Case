#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GameModeConfig))]
public class GameModeConfigEditor : Editor
{
    private const float ToggleSize = 18f;
    private const float ToggleSpacing = 2f;

    private SerializedProperty _scriptProp;
    private SerializedProperty _staticTargetsProp;

    private void OnEnable()
    {
        _scriptProp = serializedObject.FindProperty("m_Script");
        _staticTargetsProp = serializedObject.FindProperty("staticTargetSpawns");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(serializedObject, _scriptProp.propertyPath, _staticTargetsProp.propertyPath);

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("Static Target Spawns", EditorStyles.boldLabel);
        DrawStaticTargetList();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawStaticTargetList()
    {
        if (_staticTargetsProp == null)
        {
            return;
        }

        EditorGUILayout.BeginVertical("box");
        int removeIndex = -1;
        for (int i = 0; i < _staticTargetsProp.arraySize; i++)
        {
            SerializedProperty element = _staticTargetsProp.GetArrayElementAtIndex(i);
            EditorGUILayout.BeginVertical("helpBox");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Entry {i + 1}", EditorStyles.boldLabel);
            if (GUILayout.Button("Remove", GUILayout.Width(80f)))
            {
                removeIndex = i;
            }
            EditorGUILayout.EndHorizontal();

            SerializedProperty targetPrefab = element.FindPropertyRelative("targetPrefab");
            SerializedProperty maskProp = element.FindPropertyRelative("placementMask");

            EditorGUILayout.PropertyField(targetPrefab);

            DrawPlacementMask(maskProp);
            EditorGUILayout.EndVertical();
        }

        if (removeIndex >= 0)
        {
            _staticTargetsProp.DeleteArrayElementAtIndex(removeIndex);
        }

        EditorGUILayout.Space(4f);
        if (GUILayout.Button("Add New Spawn"))
        {
            int index = _staticTargetsProp.arraySize;
            _staticTargetsProp.InsertArrayElementAtIndex(index);
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawPlacementMask(SerializedProperty maskProp)
    {
        if (maskProp == null)
        {
            return;
        }

        SerializedProperty customProp = maskProp.FindPropertyRelative("customCells");
        DrawCustomCellGrid(customProp);
    }

    private void DrawCustomCellGrid(SerializedProperty customCellsProp)
    {
        var config = target as GameModeConfig;
        BoardSettings boardSettings = config != null ? config.BoardSettings : null;
        if (boardSettings == null)
        {
            EditorGUILayout.HelpBox("Assign a BoardSettings asset on the GameModeConfig to edit custom target cells.", MessageType.Info);
            return;
        }

        int columns = Mathf.Max(1, boardSettings.Columns);
        int rows = Mathf.Max(1, boardSettings.Rows);
        EnsureBoolArraySize(customCellsProp, columns * rows);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Custom Cells", EditorStyles.miniBoldLabel);

        Rect gridRect = GUILayoutUtility.GetRect(columns * (ToggleSize + ToggleSpacing), rows * (ToggleSize + ToggleSpacing));
        float startX = gridRect.x;
        float startY = gridRect.y;

        for (int row = rows - 1; row >= 0; row--)
        {
            for (int col = 0; col < columns; col++)
            {
                int index = row * columns + col;
                SerializedProperty cellProp = customCellsProp.GetArrayElementAtIndex(index);
                Rect toggleRect = new Rect(
                    startX + col * (ToggleSize + ToggleSpacing),
                    startY + (rows - 1 - row) * (ToggleSize + ToggleSpacing),
                    ToggleSize,
                    ToggleSize);
                bool value = cellProp.boolValue;
                bool newValue = GUI.Toggle(toggleRect, value, GUIContent.none);
                if (newValue != value)
                {
                    cellProp.boolValue = newValue;
                }
            }
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Fill All"))
        {
            SetAllCells(customCellsProp, true);
        }
        if (GUILayout.Button("Clear All"))
        {
            SetAllCells(customCellsProp, false);
        }
        EditorGUILayout.EndHorizontal();
    }

    private static void EnsureBoolArraySize(SerializedProperty arrayProp, int targetSize)
    {
        if (arrayProp == null)
        {
            return;
        }

        if (arrayProp.arraySize == targetSize)
        {
            return;
        }

        arrayProp.arraySize = targetSize;
        for (int i = 0; i < targetSize; i++)
        {
            SerializedProperty element = arrayProp.GetArrayElementAtIndex(i);
            element.boolValue = element.boolValue;
        }
    }

    private static void SetAllCells(SerializedProperty arrayProp, bool value)
    {
        if (arrayProp == null)
        {
            return;
        }

        for (int i = 0; i < arrayProp.arraySize; i++)
        {
            arrayProp.GetArrayElementAtIndex(i).boolValue = value;
        }
    }
}
#endif
