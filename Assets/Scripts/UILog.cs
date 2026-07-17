using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;

public class UILog : MonoBehaviour
{
    public TMP_Text logText;         // un TMP_Text dans un ScrollRect
    public ScrollRect scrollRect;    // optionnel, pour auto-scroll
    public int maxLines = 60;

    readonly Queue<string> lines = new Queue<string>();

    public void Append(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        // timestamp léger (optionnel)
        string line = $"• {message}";
        lines.Enqueue(line);
        while (lines.Count > maxLines) lines.Dequeue();

        var sb = new StringBuilder();
        foreach (var l in lines) sb.AppendLine(l);
        if (logText) logText.text = sb.ToString();

        if (scrollRect)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f; // scroll en bas
        }
    }

    public void Clear()
    {
        lines.Clear();
        if (logText) logText.text = "";
    }
}
