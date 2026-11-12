using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Runtime debug overlay (IMGUI, debug only).
/// F1: incident panel with discovery / response stats.  F2: labels above NPCs, police and paramedics.  F3: health bars.
/// </summary>
[DisallowMultipleComponent]
public class CrimeDebugHUD : MonoBehaviour
{
    [Header("Toggles")]
    [SerializeField] private KeyCode togglePanelKey = KeyCode.F1;
    [SerializeField] private KeyCode toggleWorldLabelsKey = KeyCode.F2;
    [SerializeField] private KeyCode toggleHealthBarsKey = KeyCode.F3;
    [SerializeField] private bool showPanel = true;
    [SerializeField] private bool showWorldLabels = true;
    [SerializeField] private bool showHealthBars = true;

    [Header("Layout")]
    [SerializeField, Range(0.5f, 2f)] private float uiScale = 1f;
    [SerializeField, Min(200f)] private float panelWidth = 470f;
    [Tooltip("World labels are hidden beyond this camera distance.")]
    [SerializeField, Min(1f)] private float worldLabelMaxDistance = 40f;
    [SerializeField, Min(1)] private int maxIncidentRows = 10;

    private GUIStyle panelStyle;
    private GUIStyle headerStyle;
    private GUIStyle rowStyle;
    private GUIStyle worldLabelStyle;
    private Texture2D panelTexture;
    private Texture2D whiteTexture;
    private readonly StringBuilder sb = new StringBuilder(256);
    private readonly GUIContent content = new GUIContent();
    private Camera cam;

    private void Update()
    {
        if (Input.GetKeyDown(togglePanelKey)) showPanel = !showPanel;
        if (Input.GetKeyDown(toggleWorldLabelsKey)) showWorldLabels = !showWorldLabels;
        if (Input.GetKeyDown(toggleHealthBarsKey)) showHealthBars = !showHealthBars;
    }

    private void OnDestroy()
    {
        if (panelTexture) Destroy(panelTexture);
        if (whiteTexture) Destroy(whiteTexture);
    }

    private void EnsureStyles()
    {
        if (panelStyle != null)
        {
            return;
        }

        panelTexture = MakeTexture(new Color(0.05f, 0.06f, 0.08f, 0.82f));
        whiteTexture = MakeTexture(Color.white);

        panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(10, 10, 8, 8),
            alignment = TextAnchor.UpperLeft
        };
        panelStyle.normal.background = panelTexture;

        headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14, richText = true };
        headerStyle.normal.textColor = Color.white;

        rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = false };
        rowStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

        worldLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.LowerCenter,
            richText = true,
            padding = new RectOffset(4, 4, 1, 1)
        };
        worldLabelStyle.normal.background = panelTexture;
        worldLabelStyle.normal.textColor = Color.white;
    }

    private void OnGUI()
    {
        if (!showPanel && !showWorldLabels && !showHealthBars)
        {
            return;
        }

        EnsureStyles();
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        if (!cam) cam = Camera.main;

        if (cam && (showWorldLabels || showHealthBars))
        {
            DrawWorldOverlays();
        }

        if (showPanel)
        {
            DrawIncidentPanel();
        }

        GUI.matrix = oldMatrix;
    }

    private void DrawIncidentPanel()
    {
        CrimeManager manager = CrimeManager.Instance;
        GUILayout.BeginArea(new Rect(10f, 10f, panelWidth, Screen.height / uiScale - 20f));
        GUILayout.BeginVertical(panelStyle);

        GUILayout.Label($"Crime Incidents  <size=11><color=#999>[{togglePanelKey}] panel  [{toggleWorldLabelsKey}] labels  [{toggleHealthBarsKey}] health</color></size>", headerStyle);

        if (!manager)
        {
            GUILayout.Label("No CrimeManager in scene.", rowStyle);
            GUILayout.EndVertical();
            GUILayout.EndArea();
            return;
        }

        IReadOnlyList<CrimeIncident> incidents = manager.Incidents;
        int open = 0, discovered = 0, responded = 0, resolved = 0;
        float discoverySum = 0f, responseSum = 0f;
        for (int i = 0; i < incidents.Count; i++)
        {
            CrimeIncident incident = incidents[i];
            if (incident.IsOpen) open++; else resolved++;
            if (incident.DiscoveryDelay >= 0f) { discovered++; discoverySum += incident.DiscoveryDelay; }
            if (incident.ResponseDelay >= 0f) { responded++; responseSum += incident.ResponseDelay; }
        }

        sb.Clear();
        sb.Append("Open <b>").Append(open).Append("</b>   Resolved <b>").Append(resolved).Append("</b>");
        sb.Append("   Avg discovery <b>").Append(discovered > 0 ? (discoverySum / discovered).ToString("0.0") + "s" : "-").Append("</b>");
        sb.Append("   Avg response <b>").Append(responded > 0 ? (responseSum / responded).ToString("0.0") + "s" : "-").Append("</b>");
        GUILayout.Label(sb.ToString(), rowStyle);
        GUILayout.Space(4f);

        // Newest first.
        int shown = 0;
        for (int i = incidents.Count - 1; i >= 0 && shown < maxIncidentRows; i--, shown++)
        {
            GUILayout.Label(FormatIncident(incidents[i]), rowStyle);
        }

        if (incidents.Count == 0)
        {
            GUILayout.Label("<color=#999>No crimes yet.</color>", rowStyle);
        }

        GUILayout.Space(6f);
        DrawUnitsSection();

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private string FormatIncident(CrimeIncident incident)
    {
        Color color = CrimeDebugDraw.GetIncidentColor(incident.state);
        sb.Clear();
        sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(color)).Append('>');
        sb.Append('#').Append(incident.id).Append(' ').Append(incident.type).Append("  ").Append(incident.state);
        if (!incident.IsOpen)
        {
            sb.Append(" (").Append(incident.outcome).Append(')');
        }

        sb.Append("</color>  ");

        if (!incident.IsDiscovered)
        {
            sb.Append("<b>undiscovered ").Append(incident.Age.ToString("0.0")).Append("s</b>");
        }
        else
        {
            sb.Append("found ");
            sb.Append(incident.committedTimeKnown ? "+" + incident.DiscoveryDelay.ToString("0.0") + "s" : "(time unknown)");
            sb.Append(" by ").Append(incident.firstReporter ? incident.firstReporter.name : "?");
            sb.Append(" [").Append(incident.discoveryReason).Append(']');
            if (incident.ResponseDelay >= 0f)
            {
                sb.Append("  police +").Append(incident.ResponseDelay.ToString("0.0")).Append('s');
            }

            if (incident.reportCount > 1)
            {
                sb.Append("  x").Append(incident.reportCount).Append(" calls");
            }
        }

        if (incident.victimRevivedTime >= 0f)
        {
            sb.Append("  <color=#FF66CC>revived</color>");
        }

        return sb.ToString();
    }

    private void DrawUnitsSection()
    {
        IReadOnlyList<PoliceUnit> police = PoliceUnit.All;
        IReadOnlyList<ParamedicUnit> medics = ParamedicUnit.All;
        if (police.Count == 0 && medics.Count == 0)
        {
            return;
        }

        GUILayout.Label("Units", headerStyle);
        for (int i = 0; i < police.Count; i++)
        {
            if (police[i]) GUILayout.Label($"<color=#6688FF>{police[i].name}</color>  {police[i].StatusText}", rowStyle);
        }

        for (int i = 0; i < medics.Count; i++)
        {
            if (medics[i]) GUILayout.Label($"<color=#FF66CC>{medics[i].name}</color>  {medics[i].StatusText}", rowStyle);
        }
    }

    private void DrawWorldOverlays()
    {
        float maxSqr = worldLabelMaxDistance * worldLabelMaxDistance;
        Vector3 camPos = cam.transform.position;

        if (showHealthBars)
        {
            IReadOnlyList<ActorHealth> actors = ActorHealth.All;
            for (int i = 0; i < actors.Count; i++)
            {
                ActorHealth a = actors[i];
                if (!a || (a.transform.position - camPos).sqrMagnitude > maxSqr) continue;
                DrawHealthBar(a);
            }
        }

        if (!showWorldLabels)
        {
            return;
        }

        IReadOnlyList<NPCPerception> npcs = NPCPerception.All;
        for (int i = 0; i < npcs.Count; i++)
        {
            NPCPerception npc = npcs[i];
            if (!npc || (npc.transform.position - camPos).sqrMagnitude > maxSqr) continue;

            PerceptionActivity activity = npc.Activity;
            if (activity == PerceptionActivity.Idle) continue;

            NPCPoliceCallCountdown call = npc.CallCountdown;
            string text = call && call.IsCalling
                ? $"<color=#FFD940>CALLING POLICE {call.TimeRemaining:0.0}s</color>"
                : $"<color=#FF8855>{activity}</color>";
            DrawWorldLabel(npc.transform.position + Vector3.up * 2.4f, text);
        }

        IReadOnlyList<PoliceUnit> police = PoliceUnit.All;
        for (int i = 0; i < police.Count; i++)
        {
            if (police[i] && (police[i].transform.position - camPos).sqrMagnitude <= maxSqr)
            {
                DrawWorldLabel(police[i].transform.position + Vector3.up * 2.4f, $"<color=#7799FF>POLICE</color> {police[i].StatusText}");
            }
        }

        IReadOnlyList<ParamedicUnit> medics = ParamedicUnit.All;
        for (int i = 0; i < medics.Count; i++)
        {
            if (medics[i] && (medics[i].transform.position - camPos).sqrMagnitude <= maxSqr)
            {
                DrawWorldLabel(medics[i].transform.position + Vector3.up * 2.4f, $"<color=#FF66CC>MEDIC</color> {medics[i].StatusText}");
            }
        }
    }

    private void DrawHealthBar(ActorHealth actor)
    {
        if (!WorldToGui(actor.transform.position + Vector3.up * 2.05f, out Vector2 p))
        {
            return;
        }

        const float width = 50f, height = 6f;
        var back = new Rect(p.x - width * 0.5f, p.y - height * 0.5f, width, height);
        Color old = GUI.color;

        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(back, whiteTexture);

        float hp = actor.Health01;
        GUI.color = actor.IsDead ? CrimeDebugDraw.MutedColor : Color.Lerp(CrimeDebugDraw.AlertColor, CrimeDebugDraw.OkColor, hp);
        GUI.DrawTexture(new Rect(back.x + 1f, back.y + 1f, (width - 2f) * (actor.IsDead ? 1f : hp), height - 2f), whiteTexture);

        GUI.color = old;
    }

    private void DrawWorldLabel(Vector3 worldPos, string text)
    {
        if (!WorldToGui(worldPos, out Vector2 p))
        {
            return;
        }

        content.text = text;
        Vector2 size = worldLabelStyle.CalcSize(content);
        GUI.Label(new Rect(p.x - size.x * 0.5f, p.y - size.y, size.x, size.y), content, worldLabelStyle);
    }

    private bool WorldToGui(Vector3 worldPos, out Vector2 guiPos)
    {
        Vector3 screen = cam.WorldToScreenPoint(worldPos);
        guiPos = new Vector2(screen.x, Screen.height - screen.y) / uiScale;
        return screen.z > 0f;
    }

    private static Texture2D MakeTexture(Color color)
    {
        var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
}
