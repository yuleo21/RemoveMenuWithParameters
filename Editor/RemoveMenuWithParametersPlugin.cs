using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using RemoveMenuWithParametersTool;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

[assembly: ExportsPlugin(typeof(RemoveMenuWithParametersPlugin))]

namespace RemoveMenuWithParametersTool
{
    public class RemoveMenuWithParametersPlugin : Plugin<RemoveMenuWithParametersPlugin>
    {
        public override string QualifiedName => "net.yuleo21.remove-menu-with-parameters";
        public override string DisplayName => "Remove Menu with Parameters";

        protected override void Configure()
        {
            // Modular Avatar がメニューを統合する前に実行し、
            // インスペクタで見えている(Avatar Descriptor の)メニュー構造と一致させる。
            InPhase(BuildPhase.Transforming)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Remove Menu with Parameters", Execute);
        }

        private static void Execute(BuildContext ctx)
        {
            var root = ctx.AvatarRootObject;
            var components = root.GetComponentsInChildren<RemoveMenuWithParameters>(true);
            if (components.Length == 0) return;

            var descriptor = ctx.AvatarDescriptor as VRCAvatarDescriptor;
            var main = root.GetComponent<RemoveMenuWithParameters>();

            if (main != null && descriptor != null)
            {
                Apply(ctx, descriptor, main);
            }
            else
            {
                Debug.LogWarning(
                    "[Remove Menu with Parameters] Avatar Descriptor と同じ GameObject に付けてください。処理をスキップします。");
            }

            // 自己削除
            foreach (var c in components)
            {
                Object.DestroyImmediate(c);
            }
        }

        private static void Apply(BuildContext ctx, VRCAvatarDescriptor descriptor, RemoveMenuWithParameters setting)
        {
            var rootMenu = descriptor.expressionsMenu;
            if (rootMenu == null || setting.removePaths == null || setting.removePaths.Count == 0) return;

            var selected = new HashSet<string>(setting.removePaths);
            var removedParams = new HashSet<string>();

            // 元アセットを書き換えないよう、メニューは複製しながら処理する
            var newRoot = ProcessMenu(ctx, rootMenu, "", selected, removedParams, new HashSet<VRCExpressionsMenu>());
            descriptor.expressionsMenu = newRoot;

            // 残ったメニューが使っているパラメーターは消さない(オプション)
            if (setting.keepSharedParameters)
            {
                var stillUsed = new HashSet<string>();
                MenuUtil.CollectMenu(newRoot, stillUsed, new HashSet<VRCExpressionsMenu>());
                removedParams.ExceptWith(stillUsed);
            }

            // Expression Parameters から削除(こちらも複製)
            var srcParams = descriptor.expressionParameters;
            if (srcParams != null && srcParams.parameters != null && removedParams.Count > 0)
            {
                var newParams = Object.Instantiate(srcParams);
                newParams.name = srcParams.name;
                newParams.parameters = newParams.parameters
                    .Where(p => p != null && !removedParams.Contains(p.name))
                    .ToArray();
                ctx.AssetSaver.SaveAsset(newParams);
                descriptor.expressionParameters = newParams;
            }
        }

        private static VRCExpressionsMenu ProcessMenu(
            BuildContext ctx,
            VRCExpressionsMenu src,
            string path,
            HashSet<string> selected,
            HashSet<string> removedParams,
            HashSet<VRCExpressionsMenu> stack)
        {
            var clone = Object.Instantiate(src);
            clone.name = src.name;
            ctx.AssetSaver.SaveAsset(clone);

            stack.Add(src);

            var kept = new List<VRCExpressionsMenu.Control>();
            for (int i = 0; i < clone.controls.Count; i++)
            {
                var c = clone.controls[i];
                if (c == null) continue;
                var key = MenuUtil.MakeKey(path, i);

                if (selected.Contains(key))
                {
                    // 項目自身 + サブメニューなら配下すべてのパラメーターを削除対象に
                    MenuUtil.CollectControl(c, removedParams, new HashSet<VRCExpressionsMenu>());
                    continue; // 項目を削除
                }

                if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                    && c.subMenu != null
                    && !stack.Contains(c.subMenu))
                {
                    c.subMenu = ProcessMenu(ctx, c.subMenu, key, selected, removedParams, stack);
                }

                kept.Add(c);
            }

            clone.controls = kept;
            stack.Remove(src);
            return clone;
        }
    }
}
