using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Lays out a <see cref="RaceTrack"/> by clicking on the road, instead of by hand-placing empties.
/// </summary>
/// <remarks>
/// The decision to use child transforms for waypoints is right — they are visible, draggable and
/// readable in the scene file — but the honest cost of it is that laying out a lap means creating,
/// naming, ordering and grounding twenty-five GameObjects, which is half an hour of clicking and
/// twenty-five chances to leave one floating in the air. This is that cost paid off in one file.
///
/// PLACE MODE IS THE POINT. Everything else here is a convenience; being able to click along the
/// road and get a correctly ordered, correctly grounded chain is what makes the waypoint approach
/// as cheap as the generated one would have been.
///
/// Surfaces are found with <c>HandleUtility.RaySnap</c> rather than <c>Physics.Raycast</c>. It
/// picks against the rendered mesh, so it works on scenery whose collider is missing, coarser
/// than the mesh, or still importing — all three of which are true of a downloaded map at the
/// point where its racing line is being drawn.
///
/// Everything goes through <c>Undo</c>, so a mis-click is Ctrl+Z rather than a hunt through the
/// hierarchy for the object that was just created.
/// </remarks>
[CustomEditor(typeof(RaceTrack))]
public class RaceTrackEditor : Editor
{
    /// <summary>
    /// Whether clicking in the scene view adds a waypoint.
    /// </summary>
    /// <remarks>
    /// Static, so it survives the inspector being rebuilt — which happens on every selection
    /// change, including the one caused by creating a waypoint. A per-instance flag would switch
    /// itself off after the first click.
    /// </remarks>
    static bool placing;

    /// <summary>Metres above the surface a new waypoint is placed.</summary>
    /// <remarks>
    /// Not zero. A waypoint exactly on the surface reads as underground to the validator on any
    /// slope, because the ray it casts lands a few centimetres away on a face that has moved.
    /// Half a metre is inside the validator's tolerance at both ends and is where a car's
    /// suspension travel puts the body anyway.
    /// </remarks>
    const float PlaceLift = 0.5f;

    public override void OnInspectorGUI()
    {
        RaceTrack track = (RaceTrack)target;

        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Laying out the track", EditorStyles.boldLabel);

        // The toggle is drawn as a big obvious button rather than a checkbox, because leaving
        // place mode on by accident means the next click in the scene view creates a waypoint
        // instead of selecting something, and that is confusing rather than harmless.
        GUI.backgroundColor = placing ? new Color(1f, 0.78f, 0.15f) : Color.white;
        if (GUILayout.Button(placing
                ? "PLACING — click the road to add a waypoint (Esc or click here to stop)"
                : "Start placing waypoints",
                GUILayout.Height(30f)))
        {
            placing = !placing;
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.HelpBox(
            "Click along the road in the order you want to drive it. The first click is the " +
            "start/finish line. Waypoints are added at the END of the chain, so go all the way " +
            "round and stop just before the start — the track closes the loop itself.",
            MessageType.None);

        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Drop all to ground")) DropAll(track);
            if (GUILayout.Button("Renumber")) Renumber(track);
            if (GUILayout.Button("Reverse direction")) Reverse(track);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add point after each selected")) SplitSelected(track);
            if (GUILayout.Button("Delete last")) DeleteLast(track);
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Validate", GUILayout.Height(24f)))
        {
            List<string> problems = track.Validate();
            if (problems.Count == 0)
            {
                Debug.Log($"RaceTrack '{track.name}': {track.Count} waypoints, " +
                          $"{track.Length:0} m a lap, no problems found.", track);
            }
            else
            {
                Debug.LogWarning($"RaceTrack '{track.name}' has {problems.Count} problem(s):\n  " +
                                 string.Join("\n  ", problems), track);
            }
        }

        track.Rebuild();
        EditorGUILayout.LabelField($"{track.Count} waypoints, {track.Length:0} m a lap");
    }

    void OnSceneGUI()
    {
        RaceTrack track = (RaceTrack)target;
        track.Rebuild();

        DrawLabels(track);

        if (!placing) return;

        Event e = Event.current;

        // Take the default control, so a click in empty space comes here instead of clearing the
        // selection — which would deselect the track and end place mode on the first click.
        int control = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            placing = false;
            e.Use();
            Repaint();
            return;
        }

        // Modifier-free left click only. Alt is orbit, and stealing it makes the scene view
        // unnavigable while placing — which is exactly when you most need to move around.
        if (e.type != EventType.MouseDown || e.button != 0 || e.alt || e.control || e.shift) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (!Surface(ray, out Vector3 point))
        {
            Debug.LogWarning("RaceTrack: nothing under that click to put a waypoint on.", track);
            e.Use();
            return;
        }

        Add(track, point + Vector3.up * PlaceLift);
        e.Use();
    }

    /// <summary>Where a ray meets the scene, by mesh first and collider second.</summary>
    static bool Surface(Ray ray, out Vector3 point)
    {
        // RaySnap picks against rendered geometry, so it works before colliders are generated
        // and on scenery that has none. It returns a boxed RaycastHit or null.
        object hit = HandleUtility.RaySnap(ray);
        if (hit is RaycastHit snapped)
        {
            point = snapped.point;
            return true;
        }

        if (Physics.Raycast(ray, out RaycastHit physics, 5000f, ~0, QueryTriggerInteraction.Ignore))
        {
            point = physics.point;
            return true;
        }

        point = Vector3.zero;
        return false;
    }

    /// <summary>Numbers on the waypoints, which is what makes a mis-ordered chain obvious.</summary>
    static void DrawLabels(RaceTrack track)
    {
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
        };

        for (int i = 0; i < track.Count; i++)
        {
            Vector3 at = track.Point(i) + Vector3.up * 3f;

            // Skip anything behind the camera, or the labels pile up in the corner of the view.
            if (Vector3.Dot(SceneView.currentDrawingSceneView != null
                    ? SceneView.currentDrawingSceneView.camera.transform.forward
                    : Vector3.forward,
                    at - (SceneView.currentDrawingSceneView != null
                        ? SceneView.currentDrawingSceneView.camera.transform.position
                        : Vector3.zero)) <= 0f)
                continue;

            Handles.Label(at, i == 0 ? "0  START" : i.ToString(), style);
        }
    }

    // ---- operations ------------------------------------------------------------------------

    static void Add(RaceTrack track, Vector3 at)
    {
        GameObject point = new GameObject($"WP{track.transform.childCount:00}");
        Undo.RegisterCreatedObjectUndo(point, "Add race waypoint");

        point.transform.SetParent(track.transform, worldPositionStays: true);
        point.transform.position = at;
        point.transform.SetAsLastSibling();

        EditorUtility.SetDirty(track);
        SceneView.RepaintAll();
    }

    static void DeleteLast(RaceTrack track)
    {
        int n = track.transform.childCount;
        if (n == 0) return;

        Undo.DestroyObjectImmediate(track.transform.GetChild(n - 1).gameObject);
        SceneView.RepaintAll();
    }

    /// <summary>Puts every waypoint back on the surface under it.</summary>
    /// <remarks>
    /// The repair for the commonest mistake there is: dragging a waypoint sideways in the scene
    /// view moves it along the view plane, so it ends up hanging in the air over the road it
    /// used to sit on. That is invisible from most angles and puts a respawned car in the sky.
    /// </remarks>
    static void DropAll(RaceTrack track)
    {
        int moved = 0;

        for (int i = 0; i < track.transform.childCount; i++)
        {
            Transform point = track.transform.GetChild(i);
            Ray down = new Ray(point.position + Vector3.up * 60f, Vector3.down);

            if (!Surface(down, out Vector3 ground)) continue;

            Undo.RecordObject(point, "Drop waypoints to ground");
            point.position = ground + Vector3.up * PlaceLift;
            moved++;
        }

        Debug.Log($"RaceTrack '{track.name}': dropped {moved} of {track.transform.childCount} " +
                  "waypoints onto the ground.", track);
        SceneView.RepaintAll();
    }

    /// <summary>Renames the children to match their order, so the hierarchy reads correctly.</summary>
    /// <remarks>
    /// Names are decoration — the ORDER is sibling index and nothing else reads the name — but a
    /// hierarchy showing WP07 above WP03 is how a mis-drag gets noticed at all.
    /// </remarks>
    static void Renumber(RaceTrack track)
    {
        for (int i = 0; i < track.transform.childCount; i++)
        {
            Transform point = track.transform.GetChild(i);
            string wanted = $"WP{i:00}";
            if (point.name == wanted) continue;

            Undo.RecordObject(point.gameObject, "Renumber waypoints");
            point.name = wanted;
        }

        SceneView.RepaintAll();
    }

    /// <summary>
    /// Turns the lap round the other way, keeping waypoint 0 as the start line.
    /// </summary>
    /// <remarks>
    /// A whole lap laid out anticlockwise when the map wants clockwise is otherwise a redo. The
    /// start stays put and everything after it is reversed, so the start/finish line does not
    /// move to the other side of the track as a side effect.
    /// </remarks>
    static void Reverse(RaceTrack track)
    {
        int n = track.transform.childCount;
        if (n < 3) return;

        Undo.RegisterFullObjectHierarchyUndo(track.gameObject, "Reverse race direction");

        // Walk backwards from the last child, pushing each to the end. Waypoint 0 is never
        // touched, so it stays the start line.
        for (int i = n - 1; i >= 1; i--)
            track.transform.GetChild(i).SetAsLastSibling();

        Renumber(track);
        SceneView.RepaintAll();
    }

    /// <summary>Adds a waypoint halfway between each selected one and the next.</summary>
    /// <remarks>
    /// The fix for a corner that is too coarse: select the two or three waypoints through it and
    /// this doubles the resolution there, on the ground, without disturbing the rest of the lap.
    /// </remarks>
    static void SplitSelected(RaceTrack track)
    {
        List<Transform> selected = new List<Transform>();

        foreach (GameObject go in Selection.gameObjects)
        {
            if (go != null && go.transform.parent == track.transform) selected.Add(go.transform);
        }

        if (selected.Count == 0)
        {
            Debug.LogWarning("RaceTrack: select one or more waypoints in the hierarchy first.",
                             track);
            return;
        }

        // Highest index first, so inserting does not shift the indices still to be handled.
        selected.Sort((a, b) => b.GetSiblingIndex().CompareTo(a.GetSiblingIndex()));

        foreach (Transform point in selected)
        {
            int index = point.GetSiblingIndex();
            int nextIndex = (index + 1) % track.transform.childCount;
            Vector3 middle = (point.position + track.transform.GetChild(nextIndex).position) * 0.5f;

            if (Surface(new Ray(middle + Vector3.up * 60f, Vector3.down), out Vector3 ground))
                middle = ground + Vector3.up * PlaceLift;

            GameObject added = new GameObject("WP");
            Undo.RegisterCreatedObjectUndo(added, "Split race segment");
            added.transform.SetParent(track.transform, worldPositionStays: true);
            added.transform.position = middle;
            added.transform.SetSiblingIndex(index + 1);
        }

        Renumber(track);
        SceneView.RepaintAll();
    }

    // ---- creation --------------------------------------------------------------------------

    [MenuItem("GameObject/CarCrash/Race Track", false, 10)]
    static void Create(MenuCommand command)
    {
        GameObject go = new GameObject("RaceTrack");
        go.AddComponent<RaceTrack>();

        Undo.RegisterCreatedObjectUndo(go, "Create Race Track");
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        Selection.activeObject = go;

        placing = true;
    }
}
