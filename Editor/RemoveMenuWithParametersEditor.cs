using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace RemoveMenuWithParametersTool
{
    [CustomEditor(typeof(RemoveMenuWithParameters))]
    public class RemoveMenuWithParametersEditor : Editor
    {
        private const float ArrowWidth = 16f;

        private enum ViewMode { Radial, Tree }
        private static ViewMode _viewMode = ViewMode.Radial;
        private string _currentRadialPath = "";
        private int _selectedSlotIndex = -1;   // ホバー/選択で更新される「最新の対象スロット」
        private int _shownSlotIndex = -1;       // 下部カードに実際に表示中のスロット(Layout イベントでのみ更新)
        private const string RootCenterLabel = "Expressions";

        private readonly HashSet<string> _expanded = new HashSet<string>();
        private bool _showParams = true;
        private static GUIStyle _arrowStyle;

        private static GUIStyle ArrowStyle
        {
            get
            {
                if (_arrowStyle == null)
                {
                    _arrowStyle = new GUIStyle(EditorStyles.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        padding = new RectOffset(0, 0, 0, 0),
                        margin = new RectOffset(0, 0, 2, 0),
                        fontSize = 9,
                    };
                }
                return _arrowStyle;
            }
        }

        public override void OnInspectorGUI()
        {
            var comp = (RemoveMenuWithParameters)target;
            var descriptor = comp.GetComponent<VRCAvatarDescriptor>();

            if (descriptor == null)
            {
                EditorGUILayout.HelpBox(
                    "Avatar Descriptor が付いた GameObject に追加してください。", MessageType.Error);
                return;
            }

            var menu = descriptor.expressionsMenu;
            if (menu == null)
            {
                EditorGUILayout.HelpBox(
                    "Avatar Descriptor に Expressions Menu が設定されていません。", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "チェックしたメニュー項目と、その項目が操作するパラメーターをビルド時に削除します。\n" +
                "サブメニューにチェックを入れると、配下のすべての項目とパラメーターが対象になります。",
                MessageType.None);

            EditorGUI.BeginChangeCheck();
            bool keep = EditorGUILayout.ToggleLeft(
                new GUIContent("他のメニューで使われているパラメーターは残す",
                    "削除されずに残るメニュー項目が同じパラメーターを使っている場合、そのパラメーターは削除しません。"),
                comp.keepSharedParameters);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(comp, "Change Keep Shared Parameters");
                comp.keepSharedParameters = keep;
                EditorUtility.SetDirty(comp);
            }

            EditorGUILayout.Space(4);

            // ---- 表示モード切替ツールバー ----
            using (new EditorGUILayout.HorizontalScope())
            {
                _viewMode = (ViewMode)GUILayout.Toolbar((int)_viewMode,
                    new[] { "◎ ラジアルメニュー", "≡ ツリー表示" },
                    GUILayout.Height(24));
            }

            EditorGUILayout.Space(2);

            // 外部から操作されるパラメーターを収集（Contact / BlendTree / Parameter Driver）
            var controllers = GetAllControllers(descriptor);
            var externalParams = ExternalParamUtil.Collect(comp.gameObject, controllers);

            if (_viewMode == ViewMode.Radial)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    DrawRadialView(comp, menu, descriptor);
                }
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("すべて展開")) ExpandAll(menu, "", new HashSet<VRCExpressionsMenu>());
                    if (GUILayout.Button("すべて折りたたむ")) _expanded.Clear();
                    if (GUILayout.Button("選択をクリア"))
                    {
                        Undo.RecordObject(comp, "Clear Selection");
                        comp.removePaths.Clear();
                        EditorUtility.SetDirty(comp);
                    }
                }

                EditorGUILayout.Space(4);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(menu.name, EditorStyles.boldLabel);
                    DrawMenu(comp, menu, menu, "", 0, false, new HashSet<VRCExpressionsMenu>());
                }
            }

            DrawSummary(comp, menu, descriptor, externalParams);
        }

        /// <summary>Descriptor に設定された全 AnimatorController を列挙する。</summary>
        private static IEnumerable<UnityEditor.Animations.AnimatorController> GetAllControllers(
            VRCAvatarDescriptor descriptor)
        {
            foreach (var l in descriptor.baseAnimationLayers)
                if (l.animatorController is UnityEditor.Animations.AnimatorController ac) yield return ac;
            foreach (var l in descriptor.specialAnimationLayers)
                if (l.animatorController is UnityEditor.Animations.AnimatorController ac) yield return ac;
        }

        private void DrawMenu(RemoveMenuWithParameters comp, VRCExpressionsMenu root, VRCExpressionsMenu menu,
            string path, int depth, bool inherited, HashSet<VRCExpressionsMenu> stack)
        {
            if (menu == null || menu.controls == null) return;
            if (!stack.Add(menu))
            {
                GUILayout.Label("(循環参照)", EditorStyles.miniLabel);
                return;
            }

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var c = menu.controls[i];
                if (c == null) continue;

                var key = MenuUtil.MakeKey(path, i);
                bool effective = inherited || comp.removePaths.Contains(key);
                // 自身は対象外だが、配下に削除対象がある -> 「−」表示
                bool mixed = !effective && comp.removePaths.Any(p => p.StartsWith(key + "/"));
                bool isSub = c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.subMenu != null;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(depth * 16);

                    // 開閉ボタン(ボックス外にはみ出さないよう自前で描画)
                    if (isSub)
                    {
                        bool open = _expanded.Contains(key);
                        if (GUILayout.Button(open ? "▼" : "▶", ArrowStyle,
                                GUILayout.Width(ArrowWidth), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                        {
                            if (open) _expanded.Remove(key); else _expanded.Add(key);
                        }
                    }
                    else
                    {
                        GUILayout.Space(ArrowWidth + 2);
                    }

                    // チェックボックス(3状態)
                    EditorGUI.showMixedValue = mixed;
                    EditorGUI.BeginChangeCheck();
                    bool nv = EditorGUILayout.Toggle(effective, GUILayout.Width(16));
                    bool changed = EditorGUI.EndChangeCheck();
                    EditorGUI.showMixedValue = false;

                    if (changed)
                    {
                        Undo.RecordObject(comp, "Toggle Menu Removal");
                        if (nv) SelectNode(comp, root, key);
                        else DeselectNode(comp, root, key);
                        EditorUtility.SetDirty(comp);
                    }

                    var label = string.IsNullOrEmpty(c.name) ? "(名前なし)" : c.name;
                    var ps = MenuUtil.GetOwnParameters(c).Distinct().ToArray();
                    var tooltip = ps.Length > 0 ? string.Join(", ", ps) : "パラメーターなし";

                    var prev = GUI.color;
                    if (effective) GUI.color = new Color(1f, 0.55f, 0.55f);
                    else if (mixed) GUI.color = new Color(1f, 0.85f, 0.5f);
                    GUILayout.Label(new GUIContent(label, tooltip), GUILayout.MinWidth(60));
                    GUI.color = prev;

                    GUILayout.FlexibleSpace();
                    GUILayout.Label(c.type.ToString(), EditorStyles.miniLabel);
                    if (ps.Length > 0)
                        GUILayout.Label("[" + string.Join(", ", ps) + "]", EditorStyles.miniLabel);
                }

                if (isSub && _expanded.Contains(key))
                {
                    DrawMenu(comp, root, c.subMenu, key, depth + 1, effective, stack);
                }
            }

            stack.Remove(menu);
        }

        // ---------------------------------------------------------------
        // 選択状態の操作
        //   removePaths には「削除する項目」のキーだけを入れる。
        //   キーを持つ項目の配下は丸ごと削除扱い。入れ子のキーは持たない。
        // ---------------------------------------------------------------

        private static VRCExpressionsMenu GetSubMenu(VRCExpressionsMenu menu, string indexStr)
        {
            if (menu == null || menu.controls == null) return null;
            if (!int.TryParse(indexStr, out int i) || i < 0 || i >= menu.controls.Count) return null;
            var c = menu.controls[i];
            if (c == null || c.type != VRCExpressionsMenu.Control.ControlType.SubMenu) return null;
            return c.subMenu;
        }

        /// <summary>項目(と配下すべて)を削除対象にする。兄弟が全部対象になったら親に集約する。</summary>
        private static void SelectNode(RemoveMenuWithParameters comp, VRCExpressionsMenu root, string key)
        {
            var set = comp.removePaths;
            set.RemoveAll(p => p.StartsWith(key + "/"));
            if (!set.Contains(key)) set.Add(key);

            while (true)
            {
                int slash = key.LastIndexOf('/');
                if (slash < 0) break;

                string parentKey = key.Substring(0, slash);
                var menu = root;
                foreach (var part in parentKey.Split('/'))
                {
                    menu = GetSubMenu(menu, part);
                    if (menu == null) break;
                }
                if (menu == null || menu.controls == null) break;

                int count = 0;
                bool all = true;
                for (int j = 0; j < menu.controls.Count; j++)
                {
                    if (menu.controls[j] == null) continue;
                    count++;
                    if (!set.Contains(MenuUtil.MakeKey(parentKey, j))) { all = false; break; }
                }
                if (!all || count == 0) break;

                set.RemoveAll(p => p.StartsWith(parentKey + "/"));
                set.Add(parentKey);
                key = parentKey;
            }
        }

        /// <summary>
        /// 項目(と配下すべて)を削除対象から外す。祖先が丸ごと選択されていた場合は、
        /// その祖先を展開して、外した項目以外の兄弟だけを選択状態として残す。
        /// </summary>
        private static void DeselectNode(RemoveMenuWithParameters comp, VRCExpressionsMenu root, string key)
        {
            var set = comp.removePaths;
            var parts = key.Split('/');

            // 自身または祖先でキーを持つものを探す
            int aLen = -1;
            for (int len = 1; len <= parts.Length; len++)
            {
                if (set.Contains(string.Join("/", parts, 0, len))) { aLen = len; break; }
            }
            if (aLen < 0) return;

            set.Remove(string.Join("/", parts, 0, aLen));
            if (aLen == parts.Length) return;

            // 祖先 A の配下を辿りながら、経路上にない兄弟を選択として追加する
            var menu = root;
            for (int d = 0; d < aLen; d++)
            {
                menu = GetSubMenu(menu, parts[d]);
                if (menu == null) return;
            }

            for (int d = aLen; d < parts.Length; d++)
            {
                int next = int.Parse(parts[d]);
                string basePath = string.Join("/", parts, 0, d);

                for (int j = 0; j < menu.controls.Count; j++)
                {
                    if (j == next || menu.controls[j] == null) continue;
                    var sibling = MenuUtil.MakeKey(basePath, j);
                    if (!set.Contains(sibling)) set.Add(sibling);
                }

                if (d < parts.Length - 1)
                {
                    menu = GetSubMenu(menu, parts[d]);
                    if (menu == null) break;
                }
            }
        }

        private void DrawSummary(RemoveMenuWithParameters comp, VRCExpressionsMenu menu,
            VRCAvatarDescriptor descriptor, Dictionary<string, List<string>> externalParams)
        {
            var exParams = descriptor != null ? descriptor.expressionParameters : null;

            var result = new HashSet<string>();
            MenuUtil.CollectRemoved(menu, "", new HashSet<string>(comp.removePaths), result,
                new HashSet<VRCExpressionsMenu>());

            // パラメータの型マップを構築
            var typeMap = new Dictionary<string, VRCExpressionParameters.Parameter>();
            if (exParams != null && exParams.parameters != null)
            {
                foreach (var p in exParams.parameters)
                {
                    if (p != null && !string.IsNullOrEmpty(p.name))
                        typeMap[p.name] = p;
                }
            }

            // bit 計算
            int totalBits = exParams != null && exParams.parameters != null
                ? exParams.parameters.Where(p => p != null && !string.IsNullOrEmpty(p.name)).Sum(p => BitCost(p))
                : -1;
            int removedBits = CalcBits(result, typeMap);

            EditorGUILayout.Space(6);

            // ---- bit サマリーバー ----
            if (exParams != null)
            {
                int maxBits    = VRCExpressionParameters.MAX_PARAMETER_COST;
                int remainBits = totalBits - removedBits;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("パラメーター使用量", EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        GUILayout.Label($"{totalBits} / {maxBits} bit", EditorStyles.miniLabel);
                    }

                    // プログレスバー(使用中bit / 削除bit)
                    var barRect     = EditorGUILayout.GetControlRect(false, 14);
                    float fullRatio   = Mathf.Clamp01((float)totalBits  / maxBits);
                    float removeRatio = Mathf.Clamp01((float)removedBits / maxBits);
                    float remainRatio = fullRatio - removeRatio;

                    // 背景
                    EditorGUI.DrawRect(barRect, new Color(0.2f, 0.2f, 0.2f));
                    // 使用中bit(青)
                    var remainRect = new Rect(barRect.x, barRect.y,
                        barRect.width * remainRatio, barRect.height);
                    EditorGUI.DrawRect(remainRect, new Color(0.3f, 0.55f, 1f));
                    // 削除されるbit(赤)
                    var removeRect = new Rect(barRect.x + barRect.width * remainRatio, barRect.y,
                        barRect.width * removeRatio, barRect.height);
                    EditorGUI.DrawRect(removeRect, new Color(1f, 0.35f, 0.35f));
                    // 中央ラベル
                    var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                        { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                    EditorGUI.LabelField(barRect,
                        removedBits > 0
                            ? $"削除後 {remainBits} bit  (▼ {removedBits} bit)"
                            : $"{totalBits} bit",
                        labelStyle);

                    // 凡例
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawLegend(new Color(0.3f, 0.55f, 1f), $"使用中 {remainBits} bit");
                        GUILayout.Space(8);
                        DrawLegend(new Color(1f, 0.35f, 0.35f), $"削除 {removedBits} bit");
                        GUILayout.FlexibleSpace();
                    }
                }
            }

            // ---- パラメータ一覧 ----
            _showParams = EditorGUILayout.Foldout(_showParams,
                $"削除対象: メニュー {comp.removePaths.Count} 項目 / パラメーター {result.Count} 個", true);
            if (_showParams && result.Count > 0)
            {
                // 外部操作パラメータが削除対象に含まれていればまとめて警告
                var conflicted = result.Where(n => externalParams.ContainsKey(n)).OrderBy(n => n).ToList();
                if (conflicted.Count > 0)
                {
                    var detail = string.Join("\n", conflicted.Select(n =>
                        $"・{n}\n	{ExternalParamUtil.FormatUsers(externalParams[n])}"));
                    EditorGUILayout.HelpBox(
                        "以下のパラメーターは Contact / BlendTree / Parameter Driver からも使用されています。\n" +
                        "削除するとそれらの機能が正常に動作しない可能性があります:\n" +
                        detail,
                        MessageType.Warning);
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    foreach (var n in result.OrderBy(x => x))
                    {
                        bool isExternal = externalParams.ContainsKey(n);
                        string bitStr = "";
                        if (typeMap.TryGetValue(n, out var prm))
                            bitStr = prm.networkSynced
                                ? $"  [{TypeLabel(prm.valueType)}  {BitCost(prm)} bit]"
                                : $"  [{TypeLabel(prm.valueType)}  同期なし 0 bit]";

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            // 外部操作パラメータには警告アイコン
                            if (isExternal)
                            {
                                var icon = EditorGUIUtility.IconContent("console.warnicon.sml");
                                GUILayout.Label(icon, GUILayout.Width(16), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                            }
                            else
                            {
                                GUILayout.Space(18);
                            }

                            string tooltip = isExternal
                                ? "使用箇所:\n" + string.Join("\n", externalParams[n])
                                : "";
                            GUILayout.Label(new GUIContent(n + bitStr, tooltip), EditorStyles.miniLabel);
                        }
                    }

                    if (comp.keepSharedParameters)
                        GUILayout.Label("※ 残るメニューが使用しているパラメーターはビルド時に除外されます",
                            EditorStyles.miniLabel);
                }
            }
        }

        // ---------------------------------------------------------------
        // ビット計算ヘルパー
        // ---------------------------------------------------------------

        private static int BitCost(VRCExpressionParameters.ValueType vt)
            => vt == VRCExpressionParameters.ValueType.Bool ? 1 : 8;

        /// <summary>Synced にチェックが入っているパラメーターのみ bit を消費する。</summary>
        private static int BitCost(VRCExpressionParameters.Parameter p)
            => p.networkSynced ? BitCost(p.valueType) : 0;

        private static string TypeLabel(VRCExpressionParameters.ValueType vt)
        {
            switch (vt)
            {
                case VRCExpressionParameters.ValueType.Bool:  return "Bool";
                case VRCExpressionParameters.ValueType.Int:   return "Int";
                case VRCExpressionParameters.ValueType.Float: return "Float";
                default: return vt.ToString();
            }
        }

        private static int CalcBits(HashSet<string> paramNames,
            Dictionary<string, VRCExpressionParameters.Parameter> typeMap)
        {
            int bits = 0;
            foreach (var n in paramNames)
                if (typeMap.TryGetValue(n, out var prm))
                    bits += BitCost(prm);
            return bits;
        }

        // ---------------------------------------------------------------
        // ラジアルメニュー用の描画ヘルパー
        //   Handles.DrawSolidArc は扇形の分割や端の処理が環境によってずれ、
        //   隣のスライスと重なって境界が二重に見えることがあるため、
        //   角度を厳密に計算した凸ポリゴン(AA付き)で描画する。
        // ---------------------------------------------------------------

        private const float ArcStepDeg = 3f;

        /// <summary>中心から始まる扇形(sweep は 180° 以下)を塗りつぶす。</summary>
        private static void DrawPie(Vector2 center, float radius, float startDeg, float sweepDeg, Color color)
        {
            if (sweepDeg <= 0f) return;

            // 凸ポリゴンにするため 180° を超える場合は分割する
            if (sweepDeg > 180f)
            {
                float half = sweepDeg * 0.5f;
                DrawPie(center, radius, startDeg, half, color);
                DrawPie(center, radius, startDeg + half, half, color);
                return;
            }

            int steps = Mathf.Max(2, Mathf.CeilToInt(sweepDeg / ArcStepDeg));
            var pts = new Vector3[steps + 2];
            pts[0] = center;
            for (int i = 0; i <= steps; i++)
            {
                float a = (startDeg + sweepDeg * i / steps) * Mathf.Deg2Rad;
                pts[i + 1] = new Vector3(center.x + Mathf.Cos(a) * radius, center.y + Mathf.Sin(a) * radius, 0f);
            }

            Handles.color = color;
            Handles.DrawAAConvexPolygon(pts);
        }

        /// <summary>円周をアンチエイリアス付きの線で描画する。</summary>
        private static void DrawCircleLine(Vector2 center, float radius, Color color, float width)
        {
            const int segments = 96;
            var pts = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float a = (360f * i / segments) * Mathf.Deg2Rad;
                pts[i] = new Vector3(center.x + Mathf.Cos(a) * radius, center.y + Mathf.Sin(a) * radius, 0f);
            }

            Handles.color = color;
            Handles.DrawAAPolyLine(width, pts);
        }

        private static void DrawLegend(Color color, string text)
        {
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight,
                GUILayout.Width(10));
            rect.y += 2; rect.height -= 2;
            EditorGUI.DrawRect(rect, color);
            GUILayout.Label(text, EditorStyles.miniLabel);
        }

        private void ExpandAll(VRCExpressionsMenu menu, string path, HashSet<VRCExpressionsMenu> stack)
        {
            if (menu == null || menu.controls == null || !stack.Add(menu)) return;
            for (int i = 0; i < menu.controls.Count; i++)
            {
                var c = menu.controls[i];
                if (c == null) continue;
                if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.subMenu != null)
                {
                    var key = MenuUtil.MakeKey(path, i);
                    _expanded.Add(key);
                    ExpandAll(c.subMenu, key, stack);
                }
            }
            stack.Remove(menu);
        }

        // ===============================================================
        // ラジアルメニュー
        // ===============================================================

        private void DrawRadialView(RemoveMenuWithParameters comp, VRCExpressionsMenu root, VRCAvatarDescriptor descriptor)
        {
            var currentMenu = GetMenuAtPath(root, _currentRadialPath);
            if (currentMenu == null)
            {
                _currentRadialPath = "";
                currentMenu = root;
            }

            // 1. パンくずリスト（階層ナビゲーション）
            DrawBreadcrumbs(root, descriptor);

            EditorGUILayout.Space(6);

            // 2. ラジアルメニュー円盤の描画 & 操作
            DrawRadialMenuDisk(comp, root, currentMenu, descriptor);

            EditorGUILayout.Space(6);

            // 3. 選択中/ホバー中スロットの詳細・操作カード
            DrawSelectedControlCard(comp, root, currentMenu);
        }

        /// <summary>ルートから階層パスを辿って現在の VRCExpressionsMenu を取得する。</summary>
        private static VRCExpressionsMenu GetMenuAtPath(VRCExpressionsMenu root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            var parts = path.Split('/');
            var curr = root;
            foreach (var part in parts)
            {
                curr = GetSubMenu(curr, part);
                if (curr == null) return null;
            }
            return curr;
        }

        /// <summary>アセットファイル名ではなく、親コントロールに設定された表示名を取得する。</summary>
        private static string GetMenuDisplayName(VRCExpressionsMenu root, string path, string rootDefaultName)
        {
            if (string.IsNullOrEmpty(path)) return rootDefaultName;
            var parts = path.Split('/');
            var curr = root;
            string displayName = rootDefaultName;

            foreach (var part in parts)
            {
                if (curr == null || curr.controls == null) break;
                if (int.TryParse(part, out int idx) && idx >= 0 && idx < curr.controls.Count)
                {
                    var ctrl = curr.controls[idx];
                    if (ctrl != null && !string.IsNullOrEmpty(ctrl.name))
                        displayName = ctrl.name;
                    curr = ctrl?.subMenu;
                }
                else
                {
                    break;
                }
            }
            return displayName;
        }

        /// <summary>自身または親階層が removePaths に含まれているか判定。</summary>
        private static bool IsEffectivelyRemoved(RemoveMenuWithParameters comp, string key)
        {
            var parts = key.Split('/');
            for (int len = 1; len <= parts.Length; len++)
            {
                if (comp.removePaths.Contains(string.Join("/", parts, 0, len))) return true;
            }
            return false;
        }

        /// <summary>階層ナビゲーション（パンくずリスト）を描画する。</summary>
        private void DrawBreadcrumbs(VRCExpressionsMenu root, VRCAvatarDescriptor descriptor)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                string rootName = (descriptor != null && descriptor.gameObject != null)
                    ? descriptor.gameObject.name
                    : "Main Menu";

                if (GUILayout.Button($"🏠 {rootName}", EditorStyles.toolbarButton))
                {
                    _currentRadialPath = "";
                    _selectedSlotIndex = -1;
                }

                if (!string.IsNullOrEmpty(_currentRadialPath))
                {
                    var parts = _currentRadialPath.Split('/');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        GUILayout.Label("›", EditorStyles.miniLabel);
                        string targetPath = string.Join("/", parts, 0, i + 1);
                        string menuName = GetMenuDisplayName(root, targetPath, $"Sub {parts[i]}");

                        if (GUILayout.Button(menuName, EditorStyles.toolbarButton))
                        {
                            _currentRadialPath = targetPath;
                            _selectedSlotIndex = -1;
                        }
                    }
                }

                GUILayout.FlexibleSpace();
            }
        }

        /// <summary>ラジアルメニューの円盤を描画し、クリックイベントを処理する。</summary>
        private void DrawRadialMenuDisk(RemoveMenuWithParameters comp, VRCExpressionsMenu root, VRCExpressionsMenu currentMenu, VRCAvatarDescriptor descriptor)
        {
            float size = Mathf.Min(EditorGUIUtility.currentViewWidth - 60f, 320f);
            size = Mathf.Max(size, 260f);

            var totalRect = GUILayoutUtility.GetRect(size, size);
            // センタリング
            float xOffset = (totalRect.width - size) * 0.5f;
            var diskRect = new Rect(totalRect.x + xOffset, totalRect.y, size, size);

            var center = diskRect.center;
            float outerRadius = size * 0.48f;
            float innerRadius = size * 0.18f;

            bool isRoot = string.IsNullOrEmpty(_currentRadialPath);
            int controlCount = (currentMenu != null && currentMenu.controls != null) ? currentMenu.controls.Count : 0;

            // スロット数：
            // ルート時: スロット0(戻る) + スロット1(機能のないボタン) + controls = 2 + controlCount
            // サブメニュー時: スロット0(戻る) + controls = 1 + controlCount
            int totalSlots = isRoot ? (2 + controlCount) : (1 + controlCount);
            float sliceAngle = 360f / totalSlots;

            var e = Event.current;
            var mousePos = e.mousePosition;
            var fromCenter = mousePos - center;
            float dist = fromCenter.magnitude;

            // マウス角度からホバースロット (0〜totalSlots-1) を計算
            // 真上 (-90°) がスロット0の中心、時計回りに sliceAngle 刻み
            float angle = Mathf.Atan2(fromCenter.y, fromCenter.x) * Mathf.Rad2Deg;
            float normAngle = Mathf.Repeat(angle + 90f + (sliceAngle * 0.5f), 360f);
            int hoverSlot = (dist >= innerRadius && dist <= outerRadius)
                ? Mathf.FloorToInt(normAngle / sliceAngle)
                : -1;
            if (hoverSlot >= totalSlots) hoverSlot = -1;
            bool hoverCenter = (dist < innerRadius);

            if (e.type == EventType.MouseMove)
            {
                Repaint();
            }

            // マウスオーバーしたメニュー項目を下部カードの対象にする。
            // Layout イベント中は GetRect の結果が無効なので除外する。
            // 戻る/ダミー/中央/円盤の外にいる間は、最後にホバーした項目を維持する
            // (下部のボタンへマウスを移動しても表示が消えないように)。
            if (e.type != EventType.Layout && hoverSlot >= 0)
            {
                int hoverControlIndex = isRoot ? (hoverSlot - 2) : (hoverSlot - 1);
                if (hoverControlIndex >= 0 && hoverControlIndex < controlCount
                    && currentMenu.controls[hoverControlIndex] != null
                    && hoverControlIndex != _selectedSlotIndex)
                {
                    _selectedSlotIndex = hoverControlIndex;
                    Repaint();
                }
            }

            // -----------------------------------------------------------
            // クリック処理 (EventType.MouseDown)
            // -----------------------------------------------------------
            if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 1) && diskRect.Contains(mousePos))
            {
                if (hoverCenter)
                {
                    // 中央クリックは何もしない(階層を戻る場合は「◀ 戻る」スロットかパンくずを使う)
                    e.Use();
                    return;
                }
                else if (hoverSlot == 0)
                {
                    // スロット0: 戻るボタン
                    if (!isRoot)
                    {
                        int lastSlash = _currentRadialPath.LastIndexOf('/');
                        _currentRadialPath = (lastSlash >= 0) ? _currentRadialPath.Substring(0, lastSlash) : "";
                        _selectedSlotIndex = -1;
                        e.Use();
                        GUI.changed = true;
                        return;
                    }
                    // ルートの場合は親がないため何もしない
                    e.Use();
                    return;
                }
                else if (isRoot && hoverSlot == 1)
                {
                    // ルートのスロット1: 機能のないボタン（何もしない）
                    e.Use();
                    return;
                }
                else
                {
                    // メニュー項目のスロット
                    int controlIndex = isRoot ? (hoverSlot - 2) : (hoverSlot - 1);
                    if (controlIndex >= 0 && currentMenu != null && currentMenu.controls != null && controlIndex < currentMenu.controls.Count)
                    {
                        var c = currentMenu.controls[controlIndex];
                        if (c != null)
                        {
                            string key = MenuUtil.MakeKey(_currentRadialPath, controlIndex);
                            bool isSub = (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.subMenu != null);

                            _selectedSlotIndex = controlIndex;

                            if (e.button == 1)
                            {
                                // 右クリック: 削除トグル
                                Undo.RecordObject(comp, "Toggle Menu Removal");
                                if (IsEffectivelyRemoved(comp, key)) DeselectNode(comp, root, key);
                                else SelectNode(comp, root, key);
                                EditorUtility.SetDirty(comp);
                                e.Use();
                                return;
                            }
                            else if (e.button == 0)
                            {
                                // 左クリック: サブメニューなら下層へドリルダウン、通常項目は選択フォーカスのみ
                                if (isSub)
                                {
                                    _currentRadialPath = key;
                                    _selectedSlotIndex = -1;
                                }
                                e.Use();
                                return;
                            }
                        }
                    }
                }
            }

            // -----------------------------------------------------------
            // 描画処理 (EventType.Repaint)
            // -----------------------------------------------------------
            if (e.type == EventType.Repaint)
            {
                // 各スライスの背景扇形を描画
                for (int slot = 0; slot < totalSlots; slot++)
                {
                    bool isHover = (hoverSlot == slot);
                    Color sliceColor;

                    if (slot == 0)
                    {
                        // スロット0 (戻るボタン)
                        if (!isRoot)
                        {
                            sliceColor = isHover ? new Color(0.35f, 0.42f, 0.55f, 0.95f) : new Color(0.20f, 0.24f, 0.32f, 0.88f);
                        }
                        else
                        {
                            // ルートの戻る（無効）
                            sliceColor = isHover ? new Color(0.22f, 0.25f, 0.30f, 0.7f) : new Color(0.16f, 0.18f, 0.22f, 0.6f);
                        }
                    }
                    else if (isRoot && slot == 1)
                    {
                        // ルートのスロット1 (機能のないボタン)
                        sliceColor = isHover ? new Color(0.18f, 0.19f, 0.22f, 0.7f) : new Color(0.13f, 0.14f, 0.17f, 0.5f);
                    }
                    else
                    {
                        int controlIndex = isRoot ? (slot - 2) : (slot - 1);
                        var c = currentMenu.controls[controlIndex];
                        string key = MenuUtil.MakeKey(_currentRadialPath, controlIndex);
                        bool effective = (c != null) && IsEffectivelyRemoved(comp, key);
                        bool mixed = (c != null) && !effective && comp.removePaths.Any(p => p.StartsWith(key + "/"));

                        if (c == null)
                        {
                            sliceColor = new Color(0.12f, 0.13f, 0.16f, 0.5f);
                        }
                        else if (effective)
                        {
                            sliceColor = isHover ? new Color(0.95f, 0.32f, 0.32f, 0.95f) : new Color(0.82f, 0.24f, 0.24f, 0.88f);
                        }
                        else if (mixed)
                        {
                            sliceColor = isHover ? new Color(0.95f, 0.65f, 0.20f, 0.95f) : new Color(0.85f, 0.55f, 0.15f, 0.88f);
                        }
                        else
                        {
                            sliceColor = isHover ? new Color(0.30f, 0.35f, 0.45f, 0.95f) : new Color(0.18f, 0.20f, 0.25f, 0.88f);
                        }
                    }

                    float startAngle = -90f - (sliceAngle * 0.5f) + slot * sliceAngle;
                    DrawPie(center, outerRadius, startAngle, sliceAngle, sliceColor);
                }

                // スライスの境界線（2スロット以上の場合のみ）
                if (totalSlots > 1)
                {
                    for (int slot = 0; slot < totalSlots; slot++)
                    {
                        float sepAngle = -90f - (sliceAngle * 0.5f) + slot * sliceAngle;
                        Vector2 dir = new Vector2(Mathf.Cos(sepAngle * Mathf.Deg2Rad), Mathf.Sin(sepAngle * Mathf.Deg2Rad));
                        Handles.color = new Color(0.08f, 0.09f, 0.11f, 0.9f);
                        Handles.DrawAAPolyLine(2f,
                            (Vector3)(center + dir * innerRadius), (Vector3)(center + dir * outerRadius));
                    }
                }

                // 外周輪郭線
                DrawCircleLine(center, outerRadius, new Color(0.35f, 0.40f, 0.48f, 0.7f), 1.5f);

                // 中央サークル（現在のメニュー表示名を表示）
                DrawPie(center, innerRadius, 0f, 180f, new Color(0.14f, 0.15f, 0.18f, 1f));
                DrawPie(center, innerRadius, 180f, 180f, new Color(0.14f, 0.15f, 0.18f, 1f));
                DrawCircleLine(center, innerRadius, new Color(0.40f, 0.45f, 0.55f, 0.8f), 1.5f);

                // 中央のテキスト（アセット名ではなく表示名）
                string centerDisplayName = GetMenuDisplayName(root, _currentRadialPath, RootCenterLabel);

                var centerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.90f, 0.92f, 0.96f) },
                    fontSize = 11,
                    wordWrap = true
                };
                GUI.Label(new Rect(center.x - innerRadius + 4, center.y - innerRadius + 4, (innerRadius - 4) * 2, (innerRadius - 4) * 2), centerDisplayName, centerStyle);

                // 各スロットのラベル・アイコン描画
                for (int slot = 0; slot < totalSlots; slot++)
                {
                    float midAngle = -90f + slot * sliceAngle;
                    float midRadius = (innerRadius + outerRadius) * 0.52f;
                    Vector2 slotCenter = center + new Vector2(Mathf.Cos(midAngle * Mathf.Deg2Rad), Mathf.Sin(midAngle * Mathf.Deg2Rad)) * midRadius;

                    var slotRect = new Rect(slotCenter.x - 45f, slotCenter.y - 30f, 90f, 60f);

                    if (slot == 0)
                    {
                        // スロット0 (戻るボタン)
                        var backStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            normal = { textColor = isRoot ? new Color(0.6f, 0.65f, 0.7f) : Color.white },
                            fontSize = 11
                        };
                        GUI.Label(slotRect, "◀ 戻る", backStyle);
                    }
                    else if (isRoot && slot == 1)
                    {
                        // ルートのスロット1 (機能のないボタン)
                        var dummyStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            normal = { textColor = new Color(0.45f, 0.48f, 0.55f) },
                            fontSize = 10
                        };
                        GUI.Label(slotRect, "-", dummyStyle);
                    }
                    else
                    {
                        // メニュー項目スロット
                        int controlIndex = isRoot ? (slot - 2) : (slot - 1);
                        var c = currentMenu.controls[controlIndex];
                        if (c == null) continue;

                        string key = MenuUtil.MakeKey(_currentRadialPath, controlIndex);
                        bool effective = IsEffectivelyRemoved(comp, key);
                        bool mixed = !effective && comp.removePaths.Any(p => p.StartsWith(key + "/"));

                        // アイコン(32x32) + 項目名 + 削除バッジ を縦に積み、スロット中心に揃える。
                        // バッジ分の高さは常に確保し、削除状態の切替でレイアウトが動かないようにする。
                        const float iconSize = 32f;
                        const float badgeHeight = 12f;
                        const float gap = 2f;

                        var titleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                        {
                            alignment = TextAnchor.UpperCenter,
                            normal = { textColor = Color.white },
                            fontSize = 10,
                            wordWrap = true
                        };

                        string nameText = string.IsNullOrEmpty(c.name) ? "(無名)" : c.name;
                        float nameHeight = Mathf.Min(titleStyle.CalcHeight(new GUIContent(nameText), slotRect.width), 28f);

                        float totalHeight = (c.icon != null ? iconSize + gap : 0f) + nameHeight + gap + badgeHeight;
                        float contentTop = slotCenter.y - totalHeight * 0.5f;

                        if (c.icon != null)
                        {
                            var iconRect = new Rect(slotCenter.x - iconSize * 0.5f, contentTop, iconSize, iconSize);
                            GUI.DrawTexture(iconRect, c.icon, ScaleMode.ScaleToFit);
                            contentTop += iconSize + gap;
                        }

                        GUI.Label(new Rect(slotRect.x, contentTop, slotRect.width, nameHeight), nameText, titleStyle);
                        contentTop += nameHeight + gap;

                        // 削除状態のバッジ（項目名の下に表示）
                        if (effective || mixed)
                        {
                            var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
                            {
                                alignment = TextAnchor.MiddleCenter,
                                normal = { textColor = effective ? new Color(1f, 0.85f, 0.85f) : new Color(1f, 0.80f, 0.50f) },
                                fontSize = 8
                            };
                            string badgeText = effective ? "削除" : "一部削除";
                            GUI.Label(new Rect(slotRect.x, contentTop, slotRect.width, badgeHeight), badgeText, badgeStyle);
                        }
                    }
                }
            }
        }

        /// <summary>選択中またはホバー中のコントロールの詳細情報とワンクリック操作パネル。</summary>
        private void DrawSelectedControlCard(RemoveMenuWithParameters comp, VRCExpressionsMenu root, VRCExpressionsMenu currentMenu)
        {
            // ホバーで状態が変わっても、Layout と Repaint で GUI 構造がずれないよう、
            // 表示対象は Layout イベントでのみ確定させる。
            if (Event.current.type == EventType.Layout) _shownSlotIndex = _selectedSlotIndex;

            if (currentMenu == null || currentMenu.controls == null || currentMenu.controls.Count == 0) return;

            if (_shownSlotIndex == -100)
            {
                // オプション設定カード
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("⚙ ツールオプション", EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                    }

                    EditorGUI.BeginChangeCheck();
                    bool keep = EditorGUILayout.ToggleLeft(
                        new GUIContent("他のメニューで使われているパラメーターは残す",
                            "削除されずに残るメニュー項目が同じパラメーターを使っている場合、そのパラメーターは削除しません。"),
                        comp.keepSharedParameters);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(comp, "Change Keep Shared Parameters");
                        comp.keepSharedParameters = keep;
                        EditorUtility.SetDirty(comp);
                    }

                    EditorGUILayout.Space(2);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("選択をクリア", GUILayout.Width(100)))
                        {
                            Undo.RecordObject(comp, "Clear Selection");
                            comp.removePaths.Clear();
                            EditorUtility.SetDirty(comp);
                        }
                    }
                }
                return;
            }

            int activeIndex = (_shownSlotIndex >= 0 && _shownSlotIndex < currentMenu.controls.Count)
                ? _shownSlotIndex
                : 0;

            var c = currentMenu.controls[activeIndex];
            if (c == null) return;

            string key = MenuUtil.MakeKey(_currentRadialPath, activeIndex);
            bool effective = IsEffectivelyRemoved(comp, key);
            bool mixed = !effective && comp.removePaths.Any(p => p.StartsWith(key + "/"));
            bool isSub = (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.subMenu != null);

            var ps = MenuUtil.GetOwnParameters(c).Distinct().ToArray();
            string paramText = ps.Length > 0 ? string.Join(", ", ps) : "なし";

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (c.icon != null)
                    {
                        GUILayout.Label(c.icon, GUILayout.Width(28), GUILayout.Height(28));
                    }

                    using (new EditorGUILayout.VerticalScope())
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(string.IsNullOrEmpty(c.name) ? "(無名)" : c.name, EditorStyles.boldLabel);
                            GUILayout.FlexibleSpace();
                        }
                        GUILayout.Label($"操作パラメーター: {paramText}", EditorStyles.miniLabel);
                    }

                    GUILayout.FlexibleSpace();

                    // 削除切り替えボタン
                    string btnLabel = effective ? "削除解除" : (mixed ? "配下も全削除" : "削除対象にする");
                    var origColor = GUI.backgroundColor;
                    if (effective) GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    else if (mixed) GUI.backgroundColor = new Color(1f, 0.7f, 0.3f);

                    if (GUILayout.Button(btnLabel, GUILayout.Width(90), GUILayout.Height(24)))
                    {
                        Undo.RecordObject(comp, "Toggle Menu Removal");
                        if (effective) DeselectNode(comp, root, key);
                        else SelectNode(comp, root, key);
                        EditorUtility.SetDirty(comp);
                    }
                    GUI.backgroundColor = origColor;
                }
            }

            GUILayout.Label("左クリック: サブメニューに入る / 項目を選択　右クリック: 削除対象を切り替え",
                EditorStyles.miniLabel);
        }
    }
}