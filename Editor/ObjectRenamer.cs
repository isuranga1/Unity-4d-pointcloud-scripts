using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// An editor window to batch rename GameObjects based on a predefined list of keywords, with suffixing for duplicates.
/// </summary>
public class ObjectRenamerEditor : EditorWindow
{
    // The root object whose children will be renamed.
    private GameObject rootObject;
    // The default name to assign if no keywords are found in an object's name.
    private string defaultLabel = "RenameThis";
    // Scroll position for the list of labels, in case it gets long.
    private Vector2 scrollPos;

    // Maps a found keyword to a final desired name (e.g., "Bookcase" becomes "Shelf").
    private readonly Dictionary<string, string> keywordMappings = new Dictionary<string, string>
    {
        { "Bookcase", "Shelf" },{"Mattress", "Bed"}
    };

    // Defines a rule where if both keywords are present, the dominant one is chosen.
    private struct KeywordOverride
    {
        public string Dominant;   // This keyword will be chosen.
        public string Submissive; // This keyword will be ignored if the dominant is also present.
    }

    // List of override rules. The first match found will be used.
    private readonly List<KeywordOverride> keywordOverrides = new List<KeywordOverride>
    {
        new KeywordOverride { Dominant = "Light", Submissive = "Ceiling" },
        new KeywordOverride { Dominant = "Light", Submissive = "Wall" },
        new KeywordOverride { Dominant = "Lamp", Submissive = "Floor" },
        new KeywordOverride { Dominant = "Art", Submissive = "Wall" },
    };

    // High-priority keywords. If found, these will be used for renaming over any other keyword.
    private readonly List<string> priorityLabels = new List<string>
    {
        "Floor", "Ceiling", "Wall"
    };
    // The list of standard keywords to search for within GameObject names.
    private readonly List<string> labelsToFind = new List<string>
    {
        "Window", "Door","Bed","Bathtub", "Sink", "Counter", "Hardware", 
        "Cabinet", "Lamp", "Light", "Plant", "Shelf", "Mirror", 
        "Bookcase", "Toilet", "WallArt", "Table","Camera", "Chair", "Pillow", "Female","Desk","Art"
    };

    /// <summary>
    /// Creates a menu item in the Unity Editor to open this window.
    /// </summary>
    [MenuItem("Tools/Object Renamer")]
    public static void ShowWindow()
    {
        // Get existing open window or if none, make a new one.
        GetWindow<ObjectRenamerEditor>("Object Renamer");
    }

    /// <summary>
    /// Renders the UI for the editor window.
    /// </summary>
    void OnGUI()
    {
        GUILayout.Label("Batch Rename Child Objects", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Renames children based on keywords. Priority logic: Mappings > Overrides > Priority Labels > Standard Labels. A numbered suffix like '_1' will always be added.", MessageType.Info);

        // Field for the user to drag and drop the root GameObject.
        rootObject = (GameObject)EditorGUILayout.ObjectField("Root Object", rootObject, typeof(GameObject), true);

        // Field for the user to specify the default label.
        defaultLabel = EditorGUILayout.TextField("Default Label", defaultLabel);

        // Display the list of keywords in a scrollable view.
        EditorGUILayout.LabelField("Keyword Priority Logic:", EditorStyles.boldLabel);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(300));

        EditorGUILayout.LabelField("1. Keyword Mappings", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        foreach (var mapping in keywordMappings)
        {
            EditorGUILayout.LabelField($"'{mapping.Key}' becomes '{mapping.Value}'");
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();
        
        EditorGUILayout.LabelField("2. Keyword Overrides", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        foreach (var rule in keywordOverrides)
        {
            EditorGUILayout.LabelField($"'{rule.Dominant}' wins over '{rule.Submissive}'");
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("3. Priority Keywords", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        foreach (string label in priorityLabels)
        {
            EditorGUILayout.LabelField("- " + label);
        }
        EditorGUI.indentLevel--;

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("4. Standard Keywords", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        // Use a distinct list to avoid showing duplicate keywords from the code.
        foreach (string label in labelsToFind.Distinct())
        {
            EditorGUILayout.LabelField("- " + label);
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.EndScrollView();

        // Disable the button if no root object is assigned.
        EditorGUI.BeginDisabledGroup(rootObject == null);

        GUI.backgroundColor = new Color(0.8f, 1f, 0.8f); // A light green color
        if (GUILayout.Button("Rename All Children", GUILayout.Height(40)))
        {
            if (EditorUtility.DisplayDialog("Confirm Rename",
                $"Are you sure you want to rename all children of '{rootObject.name}'? This action can be undone.",
                "Yes, Rename", "Cancel"))
            {
                RenameObjects();
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUI.EndDisabledGroup();
    }

    /// <summary>
    /// Initiates the renaming process using a two-pass system.
    /// </summary>
    private void RenameObjects()
    {
        if (rootObject == null)
        {
            Debug.LogWarning("No root object selected. Please assign a root object in the Renamer window.");
            return;
        }

        Undo.SetCurrentGroupName("Batch Rename Scene Objects");
        int group = Undo.GetCurrentGroup();
        int renameCount = 0;

        // --- PASS 1: Group all descendant objects by their determined base name.
        var objectsByBaseName = new Dictionary<string, List<Transform>>();
        Transform[] allChildren = rootObject.GetComponentsInChildren<Transform>(true)
                                          .Where(t => t != rootObject.transform)
                                          .ToArray();

        foreach (Transform child in allChildren)
        {
            string baseName = FindLabelForName(child.name);
            if (!objectsByBaseName.ContainsKey(baseName))
            {
                objectsByBaseName[baseName] = new List<Transform>();
            }
            objectsByBaseName[baseName].Add(child);
        }

        // --- PASS 2: Iterate through the groups and rename objects, always adding a numbered suffix.
        foreach (var pair in objectsByBaseName)
        {
            string baseName = pair.Key;
            List<Transform> transforms = pair.Value;

            // Always add a suffix to each object, starting from _1.
            for (int i = 0; i < transforms.Count; i++)
            {
                Transform currentTransform = transforms[i];
                string newName = $"{baseName}_{i + 1}";
                if (currentTransform.name != newName)
                {
                    Undo.RecordObject(currentTransform.gameObject, "Rename Object");
                    currentTransform.name = newName;
                    renameCount++;
                }
            }
        }

        Undo.CollapseUndoOperations(group);
        Debug.Log($"Batch rename complete. {renameCount} objects were renamed.");
    }

    /// <summary>
    /// Finds the final, mapped label for a name.
    /// </summary>
    private string FindLabelForName(string currentName)
    {
        // First, find the base label using the override/priority/standard logic.
        string baseLabel = GetBaseLabel(currentName);

        // Finally, check if this base label has a mapping and return the result.
        if (keywordMappings.ContainsKey(baseLabel))
        {
            return keywordMappings[baseLabel];
        }
        
        return baseLabel;
    }

    /// <summary>
    /// Finds the correct base label for a name based on a 3-tier priority system.
    /// </summary>
    private string GetBaseLabel(string currentName)
    {
        string currentNameLower = currentName.ToLower();

        // 1. Check for keyword overrides first. This is the highest priority.
        foreach (var rule in keywordOverrides)
        {
            if (currentNameLower.Contains(rule.Dominant.ToLower()) && 
                currentNameLower.Contains(rule.Submissive.ToLower()))
            {
                return rule.Dominant;
            }
        }

        // 2. If no override applies, check for high-priority labels.
        foreach (string keyword in priorityLabels)
        {
            if (currentNameLower.Contains(keyword.ToLower()))
            {
                return keyword;
            }
        }

        // 3. If no priority match, check the standard list.
        foreach (string keyword in labelsToFind)
        {
            if (currentNameLower.Contains(keyword.ToLower()))
            {
                return keyword;
            }
        }
        
        // 4. If no match is found in any list, return the default.
        return defaultLabel;
    }
}

