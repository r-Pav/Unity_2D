using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class CreateUIHierarchy_Editor
{
    [MenuItem("Tools/Create UI Hierarchy Reference")]
    private static void CreateAll()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("Canvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
        }

        Transform ct = canvas.transform;
        var whiteTex = Texture2D.whiteTexture;

        // ═══════ 6.1 HUD ═══════
        var hud = Make("HUD", ct);
        Stretch(hud, 0, 0, 1, 1, 0, 0);

        var hpBar = MakeSlider("HP_Bar", hud.transform,
            0, 0, 0.4f, 0.05f, 20, -20, Color.red, 1f);
        var hpText = MakeTMP("HP_Text", hud.transform,
            0, 0, 0.4f, 0.05f, 20, -20, "HP: 100/100", 16);

        var mpBar = MakeSlider("MP_Bar", hud.transform,
            0, -0.06f, 0.4f, -0.01f, 20, -20, Color.blue, 1f);
        var mpText = MakeTMP("MP_Text", hud.transform,
            0, -0.06f, 0.4f, -0.01f, 20, -20, "MP: 100/100", 16);

        // ═══════ 6.2 PassivePanel ═══════
        var pp = MakePanel("PassivePanel", ct, new Color(0, 0, 0, 0.7f));
        Stretch(pp, 0, 0, 1, 1, 0, 0);

        for (int layer = 0; layer < 5; layer++)
        {
            float y0 = -layer * 0.18f;
            float y1 = -(layer + 1) * 0.18f + 1f;
            var row = MakeRow("LayerRow_" + Roman(layer + 1), pp.transform, y0, y1);

            var title = MakeTMP("Title", row.transform,
                0, 0, 0.25f, 1, 0, 0, "T" + Roman(layer + 1), 14);
            var lockI = MakeImage("LockIcon", row.transform,
                0, 0, 0.25f, 1, 0, 0, Color.white);
            lockI.SetActive(false);

            for (int s = 0; s < 3; s++)
            {
                float sx0 = 0.28f + s * 0.24f;
                float sx1 = 0.28f + (s + 1) * 0.24f;
                var slot = MakeSlot("Slot_" + s, row.transform, sx0, 0, sx1, 1, 0,
                    new Color(0.2f, 0.2f, 0.2f, 0.8f));
                MakeImage("Icon", slot.transform,
                    0, 0, 0.38f, 0.5f, 0, 0, Color.white);
                MakeTMP("LineName", slot.transform,
                    0.38f, 0.5f, 0.95f, 0.9f, 0, 0, "", 12);
                MakeTMP("Effect", slot.transform,
                    0.38f, 0.1f, 0.95f, 0.5f, 0, 0, "", 11);
                var ov = MakeImage("LockOverlay", slot.transform,
                    0, 0, 1, 1, 0, 0, new Color(0, 0, 0, 0.5f));
                ov.SetActive(false);
                MakeTMP("UnlockLabel", slot.transform,
                    0, 0, 1, 1, 0, 0, "Lv" + (layer + 1) + "解锁", 11,
                    TextAlignmentOptions.Center);
            }
        }

        // ═══════ LineSelectDialog ═══════
        var ld = MakePanel("LineSelectDialog", ct, new Color(0, 0, 0, 0.85f));
        Stretch(ld, 0, 0, 1, 1, 0, 0);
        ld.SetActive(false);

        MakeTMP("Title", ld.transform,
            0.3f, 0.75f, 0.7f, 0.9f, 0, 0, "选择要装备的线", 18,
            TextAlignmentOptions.Center);

        for (int i = 0; i < 5; i++)
        {
            var opt = MakeSlot("Option_" + i, ld.transform,
                0.1f, 0.65f - i * 0.1f, 0.9f, 0.55f - i * 0.1f, 0,
                new Color(0.3f, 0.3f, 0.3f, 0.8f));
            MakeTMP("Label", opt.transform,
                0, 0, 1, 1, 0, 0, "线" + (i + 1), 14,
                TextAlignmentOptions.Center);
        }
        MakeButton("CloseBtn", ld.transform,
            0.85f, 0.85f, 0.95f, 0.95f, 0, 0, "X", 18);

        // ═══════ 6.3 SkillTreePanel ═══════
        var st = MakePanel("SkillTreePanel", ct, new Color(0.05f, 0.05f, 0.1f, 0.9f));
        Stretch(st, 0, 0, 1, 1, 0, 0);
        st.SetActive(false);

        MakeTMP("SkillPointLabel", st.transform,
            0.02f, 0.92f, 0.4f, 1, 0, 0, "技能点: 0", 18);

        float[][] nodeLocs = new float[][]
        {
            new float[] { 0.5f, 0.15f },
            new float[] { 0.2f, 0.4f },
            new float[] { 0.8f, 0.4f },
            new float[] { 0.2f, 0.7f },
            new float[] { 0.8f, 0.7f },
        };

        for (int s = 0; s < 2; s++)
        {
            var sv = Make("Skill_" + (s == 0 ? "Q" : "E") + "_View", st.transform);
            Stretch(sv, s * 0.5f, 0, (s + 1) * 0.5f, 0.9f, 0, 0);

            for (int n = 0; n < 5; n++)
            {
                float nx = nodeLocs[n][0], ny = nodeLocs[n][1];
                var node = MakeNode("Node_Lv" + (n + 1), sv.transform,
                    nx - 0.08f, ny - 0.07f, nx + 0.08f, ny + 0.07f,
                    new Color(0.15f, 0.15f, 0.25f, 1f));
                MakeImage("Icon", node.transform,
                    0, 0, 1, 1, 0, 0, Color.white);
                MakeTMP("Name", node.transform,
                    -0.5f, 1, 1.5f, 1.8f, 0, 0, "", 10,
                    TextAlignmentOptions.Center);
                MakeTMP("Level", node.transform,
                    -0.3f, -0.5f, 1.3f, 0, 0, 0, "Lv" + (n + 1), 9,
                    TextAlignmentOptions.Center);
                var cb = MakeTMP("CostBadge", node.transform,
                    0.6f, -0.4f, 1.8f, 0.2f, 0, 0, "", 10,
                    TextAlignmentOptions.Center);
                cb.SetActive(false);
                var bm = MakeImage("BranchMask", node.transform,
                    0, 0, 1, 1, 0, 0, new Color(0, 0, 0, 0.5f));
                bm.SetActive(false);
                var gw = MakeImage("Glow", node.transform,
                    -0.1f, -0.1f, 1.1f, 1.1f, 0, 0,
                    new Color(1, 0.8f, 0, 0.3f));
                gw.SetActive(false);
            }
            MakeImage("ConnectorLines", sv.transform,
                0.3f, 0.12f, 0.7f, 0.72f, 0, 0, Color.gray);
        }

        // ═══════ BranchChoiceDialog ═══════
        var bd = MakePanel("BranchChoiceDialog", ct,
            new Color(0, 0, 0, 0.85f));
        Stretch(bd, 0, 0, 1, 1, 0, 0);
        bd.SetActive(false);

        var lc = MakeSlot("LeftCard", bd.transform,
            0.05f, 0.1f, 0.45f, 0.8f, 0,
            new Color(0.15f, 0.15f, 0.3f, 1f));
        var rc = MakeSlot("RightCard", bd.transform,
            0.55f, 0.1f, 0.95f, 0.8f, 0,
            new Color(0.15f, 0.15f, 0.3f, 1f));

        for (int side = 0; side < 2; side++)
        {
            var parent = side == 0 ? lc.transform : rc.transform;
            MakeTMP("Lv2Info", parent,
                0.05f, 0.55f, 0.95f, 0.95f, 0, 0, "Lv2信息", 12);
            MakeTMP("Lv3Info", parent,
                0.05f, 0.05f, 0.95f, 0.5f, 0, 0, "Lv3信息", 12);
        }
        MakeButton("ConfirmBtn", bd.transform,
            0.35f, 0.02f, 0.55f, 0.08f, 0, 0, "确认", 16);
        MakeButton("CloseBtn", bd.transform,
            0.9f, 0.88f, 0.98f, 0.98f, 0, 0, "X", 16);

        // ═══════ 6.4 CraftPanel ═══════
        var cp = MakePanel("CraftPanel", ct,
            new Color(0.05f, 0.05f, 0.1f, 0.9f));
        Stretch(cp, 0, 0, 1, 1, 0, 0);

        var sl = MakeSlot("Slot_Left", cp.transform,
            0.05f, 0.55f, 0.45f, 0.85f, 0,
            new Color(0.15f, 0.15f, 0.25f, 1f));
        MakeImage("Icon", sl.transform,
            0, 0, 1, 1, 0, 0, Color.white);
        MakeTMP("Name", sl.transform,
            0.05f, 0.75f, 0.95f, 0.95f, 0, 0, "", 14);
        MakeTMP("Level", sl.transform,
            0.05f, 0.55f, 0.95f, 0.75f, 0, 0, "", 12);
        MakeTMP("Placeholder", sl.transform,
            0, 0, 1, 0.55f, 0, 0, "选择材料", 16,
            TextAlignmentOptions.Center);

        var sr = MakeSlot("Slot_Right", cp.transform,
            0.55f, 0.55f, 0.95f, 0.85f, 0,
            new Color(0.15f, 0.15f, 0.25f, 1f));
        MakeImage("Icon", sr.transform,
            0, 0, 1, 1, 0, 0, Color.white);
        MakeTMP("Name", sr.transform,
            0.05f, 0.75f, 0.95f, 0.95f, 0, 0, "", 14);
        MakeTMP("Level", sr.transform,
            0.05f, 0.55f, 0.95f, 0.75f, 0, 0, "", 12);
        MakeTMP("Placeholder", sr.transform,
            0, 0, 1, 0.55f, 0, 0, "选择材料", 16,
            TextAlignmentOptions.Center);

        MakeTMP("LevelIndicator", cp.transform,
            0.05f, 0.42f, 0.95f, 0.5f, 0, 0, "", 12,
            TextAlignmentOptions.Center);

        var rp = MakePanel("ResultPreview", cp.transform,
            new Color(0.1f, 0.1f, 0.2f, 1f));
        Stretch(rp, 0.25f, 0.05f, 0.75f, 0.35f, 0, 0);
        MakeImage("Icon", rp.transform,
            0, 0, 1, 1, 0, 0, Color.white);
        MakeTMP("Name", rp.transform,
            0.05f, 0.7f, 0.95f, 0.95f, 0, 0, "", 14);
        MakeTMP("Desc", rp.transform,
            0.05f, 0.45f, 0.95f, 0.7f, 0, 0, "", 11);
        MakeTMP("Stats", rp.transform,
            0.05f, 0.2f, 0.95f, 0.45f, 0, 0, "", 11);
        MakeTMP("Placeholder", rp.transform,
            0, 0, 1, 0.2f, 0, 0, "选择两种材料预览组合", 14,
            TextAlignmentOptions.Center);

        MakeButton("CraftBtn", cp.transform,
            0.3f, 0.38f, 0.7f, 0.42f, 0, 0, "合成", 14);

        // ═══════ CraftConfirmDialog ═══════
        var ccd = MakePanel("CraftConfirmDialog", ct,
            new Color(0, 0, 0, 0.85f));
        Stretch(ccd, 0, 0, 1, 1, 0, 0);
        ccd.SetActive(false);

        MakeTMP("Mat1_Text", ccd.transform,
            0.1f, 0.55f, 0.9f, 0.7f, 0, 0, "", 14);
        MakeTMP("Mat2_Text", ccd.transform,
            0.1f, 0.4f, 0.9f, 0.55f, 0, 0, "", 14);
        MakeTMP("Result_Text", ccd.transform,
            0.1f, 0.2f, 0.9f, 0.4f, 0, 0, "", 16,
            TextAlignmentOptions.Center);
        MakeButton("ConfirmBtn", ccd.transform,
            0.1f, 0.05f, 0.45f, 0.15f, 0, 0, "确认", 16);
        MakeButton("CancelBtn", ccd.transform,
            0.55f, 0.05f, 0.9f, 0.15f, 0, 0, "取消", 16);

        // ═══════ CraftMatListDialog ═══════
        var cml = MakePanel("CraftMatListDialog", ct,
            new Color(0, 0, 0, 0.85f));
        Stretch(cml, 0, 0, 1, 1, 0, 0);
        cml.SetActive(false);

        Make("ItemContainer", cml.transform);
        var ip = MakeSlot("ItemPrefab", cml.transform,
            0, 0, 1, 0.08f, 0, new Color(0.2f, 0.2f, 0.3f, 1f));
        ip.SetActive(false);
        MakeTMP("Label", ip.transform,
            0, 0, 1, 1, 0, 0, "", 14, TextAlignmentOptions.Center);
        MakeButton("CloseBtn", cml.transform,
            0.85f, 0.88f, 0.95f, 0.98f, 0, 0, "X", 16);

        Debug.Log("UI hierarchy with components created.");
    }

    // ──────────────── Helpers ────────────────

    private static GameObject Make(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(GameObject go, float ax, float ay, float bx, float by, float ox, float oy)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(ax, ay);
        rt.anchorMax = new Vector2(bx, by);
        rt.offsetMin = new Vector2(ox, oy);
        rt.offsetMax = new Vector2(-ox, -oy);
    }

    private static GameObject MakeTMP(string name, Transform parent,
        float ax, float ay, float bx, float by, float ox, float oy,
        string text, float size, TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var go = Make(name, parent);
        Stretch(go, ax, ay, bx, by, ox, oy);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        return go;
    }

    private static GameObject MakeImage(string name, Transform parent,
        float ax, float ay, float bx, float by, float ox, float oy,
        Color color)
    {
        var go = Make(name, parent);
        Stretch(go, ax, ay, bx, by, ox, oy);
        var img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    private static GameObject MakeButton(string name, Transform parent,
        float ax, float ay, float bx, float by, float ox, float oy,
        string label, float fontSize)
    {
        var go = Make(name, parent);
        Stretch(go, ax, ay, bx, by, ox, oy);
        go.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.4f, 1f);
        go.AddComponent<Button>();

        if (!string.IsNullOrEmpty(label))
        {
            var txt = Make("Label", go.transform);
            Stretch(txt, 0, 0, 1, 1, 0, 0);
            var t = txt.AddComponent<TextMeshProUGUI>();
            t.text = label;
            t.fontSize = fontSize;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
        }
        return go;
    }

    private static GameObject MakePanel(string name, Transform parent, Color bgColor)
    {
        var go = Make(name, parent);
        var img = go.AddComponent<Image>();
        img.color = bgColor;
        return go;
    }

    private static GameObject MakeRow(string name, Transform parent, float y0, float y1)
    {
        var go = Make(name, parent);
        Stretch(go, 0, y0, 1, y1, 0, 0);
        go.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.15f, 0.6f);
        return go;
    }

    private static GameObject MakeSlot(string name, Transform parent,
        float x0, float y0, float x1, float y1, float pad,
        Color bgColor)
    {
        var go = MakeButton(name, parent, x0, y0, x1, y1, pad, pad, "", 0);
        go.GetComponent<Image>().color = bgColor;
        return go;
    }

    private static GameObject MakeNode(string name, Transform parent,
        float ax, float ay, float bx, float by, Color bg)
    {
        var go = Make(name, parent);
        Stretch(go, ax, ay, bx, by, 0, 0);
        go.AddComponent<Image>().color = bg;
        go.AddComponent<Button>();
        return go;
    }

    private static GameObject MakeSlider(string name, Transform parent,
        float ax, float ay, float bx, float by, float ox, float oy,
        Color fillColor, float value)
    {
        var go = Make(name, parent);
        Stretch(go, ax, ay, bx, by, ox, oy);
        var slider = go.AddComponent<Slider>();
        slider.value = value;
        slider.maxValue = 1f;

        var bg = MakeImage("Background", go.transform,
            0, 0, 1, 1, 0, 0, new Color(0.15f, 0.15f, 0.15f, 1f));

        var fillArea = Make("Fill Area", go.transform);
        Stretch(fillArea, 0, 0, 1, 1, 2, 2);
        var fill = MakeImage("Fill", fillArea.transform,
            0, 0, 1, 1, 0, 0, fillColor);

        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.targetGraphic = fill.GetComponent<Image>();

        return go;
    }

    private static string Roman(int v)
    {
        return new[] { "I", "II", "III", "IV", "V" }[v - 1];
    }
}
