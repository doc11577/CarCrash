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
    /// The track being laid out, or null when not placing.
    /// </summary>
    /// <remarks>
    /// STATIC, and place mode does not live on the custom editor at all — it runs from
    /// <c>SceneView.duringSceneGui</c>. Two reasons, and the second is the one that was got
    /// wrong first time round:
    ///
    /// * An inspector is rebuilt on every selection change, including the one caused by creating
    ///   a waypoint, so an instance field switches place mode off after the first click.
    /// * <c>OnSceneGUI</c> only runs while the track is SELECTED. If a click ever does leak
    ///   through to the scene view's own picking, the selection moves to whatever was clicked,
    ///   the editor is torn down, and place mode disappears — which is indistinguishable from it
    ///   never having worked. Running from the scene-view callback means it survives that.
    /// </remarks>
    static RaceTrack placingTrack;

    /// <summary>Stable control id, so the same control is claimed on every event of a frame.</summary>
    static readonly int PlaceHint = "RaceTrackPlace".GetHashCode();

    /// <summary>Metres above the surface a new waypoint is placed.</summary>
    /// <remarks>
    /// Not zero. A waypoint exactly on the surface reads as underground to the validator on any
    /// slope, because the ray it casts lands a few centimetres away on a face that has moved.
    /// Half a metre is inside the validator's tolerance at both ends and is where a car's
    /// suspension travel puts the body anyway.
    /// </remarks>
    const float PlaceLift = 0.5f;

    static bool Placing => placingTrack != null;

    // ---- inspector ---------------------------------------------------------------------------

    public override void OnInspectorGUI()
    {
        RaceTrack track = (RaceTrack)target;

        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Laying out the track", EditorStyles.boldLabel);

        bool placingThis = placingTrack == track;

        // Drawn as a big obvious button rather than a checkbox, because leaving place mode on by
        // accident means the next click in the scene view creates a waypoint instead of selecting
        // something, and that is confusing rather than harmless.
        GUI.backgroundColor = placingThis ? new Color(1f, 0.78f, 0.15f) : Color.white;
        if (GUILayout.Button(placingThis
                ? "PLACING — click the road to add a waypoint (Esc, or click here, to stop)"
                : "Start placing waypoints",
                GUILayout.Height(30f)))
        {
            if (placingThis) StopPlacing();
            else StartPlacing(track);
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.HelpBox(
            "Click along the road in the order you want to drive it. The first click is the " +
            "start/finish line. Waypoints are added at the END of the chain, so go all the way " +
            "round and stop just before the start — the track closes the loop itself.\n\n" +
            "While placing, a left click will NOT select anything in the scene. Alt-drag still " +
            "orbits, and the scroll wheel still zooms.",
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

    void OnDisable()
    {
        // Selecting something else while placing is not an instruction to stop — the callback is
        // global and survives it deliberately. But a track that has been DELETED must not leave
        // place mode running against a destroyed object.
        if (placingTrack == null) StopPlacing();
    }

    // ---- place mode --------------------------------------------------------------------------

    static void StartPlacing(RaceTrack track)
    {
        placingTrack = track;

        // Subscribed once. Removing first is not paranoia: a domain reload during play mode can
        // leave the delegate attached with the static field already cleared, and a second
        // subscription would then handle every event twice — placing two waypoints per click.
        SceneView.duringSceneGui -= OnSceneEvent;
        SceneView.duringSceneGui += OnSceneEvent;

        SceneView.RepaintAll();
    }

    static void StopPlacing()
    {
        placingTrack = null;
        SceneView.duringSceneGui -= OnSceneEvent;
        SceneView.RepaintAll();
    }

    /// <summary>
    /// The scene-view click handler. This is the part that has to fight uGUI's own picking.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>AddDefaultControl</c> ALONE IS NOT ENOUGH, and the failure looks exactly like place
    /// mode not existing: every click selects the road instead of adding a waypoint. The scene
    /// view picks objects on mouse UP, so consuming only the mouse DOWN leaves the selection
    /// change to happen a moment later — and the selection change tears down the editor, which
    /// is why the first attempt appeared to do nothing at all rather than misbehaving visibly.
    ///
    /// The fix is the full hot-control handshake, which is what every built-in tool does:
    /// claim the default control during Layout so clicks in empty space arrive here at all, take
    /// <c>hotControl</c> on mouse down so no one else can have the drag, and release it and place
    /// the waypoint on mouse up, consuming BOTH events.
    ///
    /// Placement happens on mouse UP for the same reason a uGUI button raises onClick there: a
    /// press that turns into a drag is someone moving the camera, not someone placing a point.
    /// </remarks>
    static void OnSceneEvent(SceneView view)
    {
        if (!Placing)
        {
            SceneView.duringSceneGui -= OnSceneEvent;
            return;
        }

        Event e = Event.current;

        // Requested unconditionally and first, so the id is the same on every event in a frame.
        // Control ids are handed out in call order; a conditional request shifts every later id
        // and hands the drag to something else halfway through it.
        int control = GUIUtility.GetControlID(PlaceHint, FocusType.Passive);

        DrawOverlay(view);

        // Escape is read from the RAW event type, not through GetTypeForControl. Key events are
        // routed to whatever holds keyboard focus, and this control deliberately never takes it
        // (FocusType.Passive) — so asked that way, the key would simply never arrive and the only
        // way out of place mode would be the inspector button.
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            StopPlacing();
            e.Use();
            return;
        }

        switch (e.GetTypeForControl(control))
        {
            case EventType.Layout:
            case EventType.MouseMove:
                // Makes this control the fallback for the whole view, so a click on scenery is
                // offered here before the scene view's own object picking gets it.
                HandleUtility.AddDefaultControl(control);
                if (e.type == EventType.MouseMove) view.Repaint();
                break;

            case EventType.MouseDown:
                // Alt is orbit and the right button is the look-around, and stealing either makes
                // the scene view unnavigable at exactly the moment you need to move around.
                if (e.button != 0 || e.alt) break;

                GUIUtility.hotControl = control;
                e.Use();
                break;

            case EventType.MouseUp:
                if (e.button != 0 || GUIUtility.hotControl != control) break;

                GUIUtility.hotControl = 0;
                Place(placingTrack, e.mousePosition);
                e.Use();
                break;

            case EventType.Repaint:
                DrawPreview(e.mousePosition);
                break;
        }
    }

    /// <summary>A banner, so place mode is never on without saying so.</summary>
    static void DrawOverlay(SceneView view)
    {
        Handles.BeginGUI();

        Rect box = new Rect(10f, 10f, 320f, 46f);
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.Box(box, GUIContent.none);
        GUI.color = Color.white;

        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = new Color(1f, 0.78f, 0.15f) },
            wordWrap = true,
        };

        int placed = placingTrack != null ? placingTrack.transform.childCount : 0;
        GUI.Label(new Rect(box.x + 8f, box.y + 5f, box.width - 16f, box.height - 10f),
                  $"PLACING WAYPOINTS — {placed} so far\nClick the road in driving order. " +
                  "Esc to stop.", style);

        Handles.EndGUI();
    }

    /// <summary>A marker where the next click would land, so it is not a guess.</summary>
    static void DrawPreview(Vector2 mouse)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
        if (!Surface(ray, out Vector3 point)) return;

        Handles.color = new Color(1f, 0.78f, 0.15f, 0.9f);
        Handles.SphereHandleCap(0, point + Vector3.up * PlaceLift, Quaternion.identity, 1.6f,
                                EventType.Repaint);

        // Joined to the last waypoint, so the segment about to be created is visible before it
        // exists — which is how a click that would double back gets noticed rather than undone.
        if (placingTrack != null && placingTrack.transform.childCount > 0)
        {
            Transform last = placingTrack.transform.GetChild(placingTrack.transform.childCount - 1);
            Handles.DrawDottedLine(last.position, point + Vector3.up * PlaceLift, 4f);
        }
    }

    static void Place(RaceTrack track, Vector2 mouse)
    {
        if (track == null) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
        if (!Surface(ray, out Vector3 point))
        {
            Debug.LogWarning("RaceTrack: nothing under that click to put a waypoint on.", track);
            return;
        }

        Add(track, point + Vector3.up * PlaceLift);
    }

    /// <summary>Where a ray meets the scene, by rendered mesh first and collider second.</summary>
    static bool Surface(Ray ray, out Vector3 point)
    {
        // RaySnap picks against rendered geometry, so it works before colliders are generated
        // and on scenery that has none. It returns a boxed RaycastHit, or null for a miss.
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

    // ---- labels ------------------------------------------------------------------------------

    void OnSceneGUI()
    {
        RaceTrack track = (RaceTrack)target;
        track.Rebuild();
        DrawLabels(track);
    }

    /// <summary>Numbers on the waypoints, which is what makes a mis-ordered chain obvious.</summary>
    static void DrawLabels(RaceTrack track)
    {
        SceneView view = SceneView.currentDrawingSceneView;
        if (view == null || view.camera == null) return;

        Transform eye = view.camera.transform;

        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
        };

        for (int i = 0; i < track.Count; i++)
        {
            Vector3 at = track.Point(i) + Vector3.up * 3f;

            // Anything behind the camera would otherwise pile its label up in one corner of the
            // view, which on a 25-point lap is a solid block of numbers over the road.
            if (Vector3.Dot(eye.forward, at - eye.position) <= 0f) continue;

            Handles.Label(at, i == 0 ? "0  START" : i.ToString(), style);
        }
    }

    // ---- operations --------------------------------------------------------------------------

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
    /// view moves it along the VIEW PLANE, so it ends up hanging in the air over the road it
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

    // ---- creation ----------------------------------------------------------------------------

    [MenuItem("GameObject/CarCrash/Race Track", false, 10)]
    static void Create(MenuCommand command)
    {
        GameObject go = new GameObject("RaceTrack");
        RaceTrack track = go.AddComponent<RaceTrack>();

        Undo.RegisterCreatedObjectUndo(go, "Create Race Track");
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        Selection.activeObject = go;

        StartPlacing(track);
    }
}
