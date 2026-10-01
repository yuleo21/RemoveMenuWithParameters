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

            DrawSummary(comp, menu);
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

        private void DrawSummary(RemoveMenuWithParameters comp, VRCExpressionsMenu menu)
        {
            var result = new HashSet<string>();
            MenuUtil.CollectRemoved(menu, "", new HashSet<string>(comp.removePaths), result,
                new HashSet<VRCExpressionsMenu>());

            EditorGUILayout.Space(6);
            _showParams = EditorGUILayout.Foldout(_showParams,
                $"削除対象: メニュー {comp.removePaths.Count} 項目 / パラメーター {result.Count} 個", true);
            if (_showParams && result.Count > 0)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    foreach (var n in result.OrderBy(x => x))
                        GUILayout.Label(n, EditorStyles.miniLabel);
                    if (comp.keepSharedParameters)
                        GUILayout.Label("※ 残るメニューが使用しているパラメーターはビルド時に除外されます",
                            EditorStyles.miniLabel);
                }
            }
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
    }
}
