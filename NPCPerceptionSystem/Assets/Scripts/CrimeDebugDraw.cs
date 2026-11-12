using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>When a component draws its scene-view gizmos.</summary>
public enum GizmoDrawMode
{
    Off,
    Selected,
    Always
}

/// <summary>Shows a serialized field in the inspector but greys it out (runtime status values).</summary>
public class ReadOnlyAttribute : PropertyAttribute
{
}

/// <summary>
/// Shared gizmo helpers so every system draws with the same look.
/// All Handles calls are editor-only; in builds these become no-ops.
/// </summary>
public static class CrimeDebugDraw
{
    public static readonly Color SightColor = new Color(0.2f, 0.85f, 1f, 1f);
    public static readonly Color HearingColor = new Color(1f, 0.85f, 0.2f, 1f);
    public static readonly Color AlertColor = new Color(1f, 0.35f, 0.2f, 1f);
    public static readonly Color OkColor = new Color(0.3f, 1f, 0.4f, 1f);
    public static readonly Color MutedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
    public static readonly Color PoliceColor = new Color(0.3f, 0.5f, 1f, 1f);
    public static readonly Color MedicColor = new Color(1f, 0.4f, 0.8f, 1f);

    public static Color GetIncidentColor(IncidentState state)
    {
        switch (state)
        {
            case IncidentState.Undiscovered: return MutedColor;
            case IncidentState.Reported: return HearingColor;
            case IncidentState.Responding: return PoliceColor;
            case IncidentState.Investigating: return SightColor;
            case IncidentState.Chasing: return AlertColor;
            case IncidentState.Searching: return new Color(1f, 0.6f, 0.1f, 1f);
            default: return OkColor;
        }
    }

    /// <summary>Filled + outlined view cone on the ground plane.</summary>
    public static void ViewCone(Vector3 origin, Vector3 forward, float angle, float radius, Color color, float fillAlpha = 0.08f)
    {
#if UNITY_EDITOR
        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
        {
            flatForward = Vector3.forward;
        }

        flatForward.Normalize();
        Vector3 from = Quaternion.Euler(0f, -angle * 0.5f, 0f) * flatForward;

        Handles.color = new Color(color.r, color.g, color.b, fillAlpha);
        Handles.DrawSolidArc(origin, Vector3.up, from, angle, radius);
        Handles.color = color;
        Handles.DrawWireArc(origin, Vector3.up, from, angle, radius);
        Handles.DrawLine(origin, origin + from * radius);
        Handles.DrawLine(origin, origin + (Quaternion.Euler(0f, angle, 0f) * from) * radius);
#endif
    }

    /// <summary>Flat circle on the ground plane (cleaner than a wire sphere for radii).</summary>
    public static void Circle(Vector3 center, float radius, Color color, float thickness = 1f)
    {
#if UNITY_EDITOR
        Handles.color = color;
        Handles.DrawWireDisc(center, Vector3.up, radius, thickness);
#endif
    }

    public static void DashedLine(Vector3 from, Vector3 to, Color color, float dashSize = 4f)
    {
#if UNITY_EDITOR
        Handles.color = color;
        Handles.DrawDottedLine(from, to, dashSize);
#endif
    }

    public static void Line(Vector3 from, Vector3 to, Color color, float thickness = 1f)
    {
#if UNITY_EDITOR
        Handles.color = color;
        Handles.DrawLine(from, to, thickness);
#endif
    }

    /// <summary>Small diamond marker, e.g. for last known positions or search points.</summary>
    public static void Marker(Vector3 position, Color color, float size = 0.3f)
    {
#if UNITY_EDITOR
        Handles.color = color;
        Vector3 up = position + Vector3.up * size;
        Vector3 down = position - Vector3.up * size;
        Vector3 l = position - Vector3.right * size;
        Vector3 r = position + Vector3.right * size;
        Vector3 f = position + Vector3.forward * size;
        Vector3 b = position - Vector3.forward * size;
        Handles.DrawLine(up, l); Handles.DrawLine(up, r); Handles.DrawLine(up, f); Handles.DrawLine(up, b);
        Handles.DrawLine(down, l); Handles.DrawLine(down, r); Handles.DrawLine(down, f); Handles.DrawLine(down, b);
#endif
    }

    /// <summary>Scene-view text label with a dark background so it stays readable.</summary>
    public static void Label(Vector3 position, string text, Color color)
    {
#if UNITY_EDITOR
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                padding = new RectOffset(4, 4, 1, 1)
            };
            labelStyle.normal.background = MakeTexture(new Color(0f, 0f, 0f, 0.6f));
        }

        labelStyle.normal.textColor = color;
        Handles.Label(position, text, labelStyle);
#endif
    }

    /// <summary>Draws the agent's current NavMesh path.</summary>
    public static void AgentPath(NavMeshAgent agent, Color color)
    {
#if UNITY_EDITOR
        if (!agent || !agent.enabled || !agent.hasPath)
        {
            return;
        }

        Vector3[] corners = agent.path.corners;
        Handles.color = color;
        for (int i = 0; i < corners.Length - 1; i++)
        {
            Handles.DrawDottedLine(corners[i] + Vector3.up * 0.05f, corners[i + 1] + Vector3.up * 0.05f, 3f);
        }
#endif
    }

#if UNITY_EDITOR
    private static GUIStyle labelStyle;

    private static Texture2D MakeTexture(Color color)
    {
        var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
#endif
}

/// <summary>Shared geometry helpers for sight checks.</summary>
public static class PerceptionUtility
{
    /// <summary>
    /// Point to aim sight raycasts at: the collider's center if there is one, so we don't aim at the feet/pivot.
    /// </summary>
    public static Vector3 GetAimPoint(Transform t)
    {
        if (!t)
        {
            return Vector3.zero;
        }

        Collider col = t.GetComponentInChildren<Collider>();
        return col && col.enabled ? col.bounds.center : t.position;
    }

    public static bool IsSameOrChild(Transform candidate, Transform root)
    {
        return candidate && root && (candidate == root || candidate.IsChildOf(root));
    }

    /// <summary>
    /// Range + FOV + optional line-of-sight check. Hits on <paramref name="self"/> or <paramref name="allowedHit"/> don't block.
    /// </summary>
    public static bool CanSee(
        Vector3 eye,
        Vector3 forward,
        Vector3 point,
        float viewDistance,
        float viewAngle,
        bool useLineOfSight,
        LayerMask obstructionMask,
        Transform self,
        Transform allowedHit)
    {
        Vector3 toPoint = point - eye;
        float sqrDistance = toPoint.sqrMagnitude;
        if (sqrDistance > viewDistance * viewDistance)
        {
            return false;
        }

        if (sqrDistance < 0.0001f)
        {
            return true;
        }

        if (Vector3.Angle(forward, toPoint) > viewAngle * 0.5f)
        {
            return false;
        }

        if (!useLineOfSight)
        {
            return true;
        }

        float distance = Mathf.Sqrt(sqrDistance);
        if (!Physics.Raycast(eye, toPoint / distance, out RaycastHit hit, distance, obstructionMask, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        if (IsSameOrChild(hit.transform, self) || IsSameOrChild(hit.transform, allowedHit))
        {
            return true;
        }

        // Nothing meaningful in the way before the point.
        return hit.distance + 0.05f >= distance;
    }
}
