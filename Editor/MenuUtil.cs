using System.Collections.Generic;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace RemoveMenuWithParametersTool
{
    internal static class MenuUtil
    {
        public static string MakeKey(string parentPath, int index)
        {
            return string.IsNullOrEmpty(parentPath) ? index.ToString() : parentPath + "/" + index;
        }

        /// <summary>この項目自身が操作するパラメーター名(サブメニューの中身は含まない)。</summary>
        public static IEnumerable<string> GetOwnParameters(VRCExpressionsMenu.Control c)
        {
            if (c.parameter != null && !string.IsNullOrEmpty(c.parameter.name))
                yield return c.parameter.name;

            if (c.subParameters != null)
            {
                foreach (var p in c.subParameters)
                {
                    if (p != null && !string.IsNullOrEmpty(p.name))
                        yield return p.name;
                }
            }
        }

        /// <summary>項目のパラメーターを収集。サブメニューなら配下すべてを再帰的に収集する。</summary>
        public static void CollectControl(VRCExpressionsMenu.Control c, HashSet<string> result,
            HashSet<VRCExpressionsMenu> visited)
        {
            foreach (var n in GetOwnParameters(c)) result.Add(n);

            if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.subMenu != null)
                CollectMenu(c.subMenu, result, visited);
        }

        /// <summary>メニュー配下すべてのパラメーターを収集(循環参照対策あり)。</summary>
        public static void CollectMenu(VRCExpressionsMenu menu, HashSet<string> result,
            HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || !visited.Add(menu)) return;
            if (menu.controls == null) return;
            foreach (var c in menu.controls)
            {
                if (c != null) CollectControl(c, result, visited);
            }
        }

        /// <summary>選択された項目(とその配下)が操作するパラメーターを収集(インスペクタの集計用)。</summary>
        public static void CollectRemoved(VRCExpressionsMenu menu, string path, HashSet<string> selected,
            HashSet<string> result, HashSet<VRCExpressionsMenu> stack)
        {
            if (menu == null || menu.controls == null) return;
            if (!stack.Add(menu)) return;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var c = menu.controls[i];
                if (c == null) continue;
                var key = MakeKey(path, i);

                if (selected.Contains(key))
                {
                    CollectControl(c, result, new HashSet<VRCExpressionsMenu>());
                }
                else if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.subMenu != null)
                {
                    CollectRemoved(c.subMenu, key, selected, result, stack);
                }
            }

            stack.Remove(menu);
        }
    }
}
