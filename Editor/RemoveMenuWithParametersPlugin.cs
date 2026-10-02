using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using RemoveMenuWithParametersTool;
using UnityEditor.Animations;
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

            // 外部(Contact / BlendTree / Parameter Driver)から使用されるパラメーターが削除対象に含まれていれば警告
            var allControllers = GetAllControllers(descriptor);
            var externalParams = ExternalParamUtil.Collect(ctx.AvatarRootObject, allControllers);
            var conflicted = removedParams.Where(p => externalParams.ContainsKey(p)).OrderBy(p => p).ToList();
            if (conflicted.Count > 0)
            {
                var lines = conflicted.Select(p =>
                    $"  ・{p} {ExternalParamUtil.FormatUsers(externalParams[p], 5)}");
                Debug.LogWarning(
                    "[Remove Menu with Parameters] 以下のパラメーターは Contact / BlendTree / Parameter Driver からも " +
                    "使用されています。削除することでそれらの機能が正常に動作しない可能性があります:\n" +
                    string.Join("\n", lines));
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

            // Playable Layers から該当パラメータのみで駆動されているレイヤーを削除
            if (removedParams.Count > 0)
            {
                RemoveLayersUsedOnlyByParams(ctx, descriptor, removedParams);
            }
        }

        private static IEnumerable<AnimatorController> GetAllControllers(VRCAvatarDescriptor descriptor)
        {
            foreach (var l in descriptor.baseAnimationLayers)
                if (l.animatorController is AnimatorController ac) yield return ac;
            foreach (var l in descriptor.specialAnimationLayers)
                if (l.animatorController is AnimatorController ac) yield return ac;
        }

        // ---------------------------------------------------------------
        // Playable Layer のレイヤー削除
        // ---------------------------------------------------------------

        /// <summary>
        /// 全 Playable Layer を走査し、削除されたパラメーターのみを条件とする
        /// レイヤーを除去する。
        /// </summary>
        private static void RemoveLayersUsedOnlyByParams(
            BuildContext ctx, VRCAvatarDescriptor descriptor, HashSet<string> removedParams)
        {
            foreach (var layer in descriptor.baseAnimationLayers)
            {
                TryRemoveLayers(ctx, descriptor, layer.animatorController as AnimatorController, removedParams);
            }
            foreach (var layer in descriptor.specialAnimationLayers)
            {
                TryRemoveLayers(ctx, descriptor, layer.animatorController as AnimatorController, removedParams);
            }
        }

        private static void TryRemoveLayers(
            BuildContext ctx, VRCAvatarDescriptor descriptor, AnimatorController controller, HashSet<string> removedParams)
        {
            if (controller == null) return;

            // 元の AnimatorController を複製して書き換える
            var clone = Object.Instantiate(controller);
            clone.name = controller.name;
            ctx.AssetSaver.SaveAsset(clone);

            var layers = clone.layers.ToList();
            var removed = new List<string>();

            for (int i = layers.Count - 1; i >= 0; i--)
            {
                // ベースレイヤー(index 0)は安全のため常に保持
                if (i == 0) continue;

                if (IsLayerOnlyUsedByParams(layers[i].stateMachine, removedParams))
                {
                    removed.Add(layers[i].name);
                    layers.RemoveAt(i);
                }
            }

            if (removed.Count == 0) return;

            clone.layers = layers.ToArray();

            // Descriptor のレイヤー参照を更新
            ReplaceController(descriptor, controller, clone);

            Debug.Log(
                $"[Remove Menu with Parameters] {clone.name}: レイヤーを削除しました → {string.Join(", ", removed)}");
        }

        /// <summary>
        /// ステートマシン内のすべてのトランジション条件が
        /// removedParams のみで構成されている場合に true を返す。
        /// <br/>
        /// 以下のいずれかに該当する場合は false（保持）:
        /// ・条件が 0 件のトランジションが存在する（無条件 = 常に遷移）
        /// ・removedParams に含まれないパラメーターを条件に持つトランジションが存在する
        /// ・StateMachineBehaviour（VRC などのビヘイビア）が付いているステートが存在する
        /// </summary>
        private static bool IsLayerOnlyUsedByParams(
            AnimatorStateMachine sm, HashSet<string> removedParams)
        {
            if (sm == null) return false;

            return IsStateMachineOnlyUsedByParams(sm, removedParams, new HashSet<AnimatorStateMachine>());
        }

        private static bool IsStateMachineOnlyUsedByParams(
            AnimatorStateMachine sm, HashSet<string> removedParams, HashSet<AnimatorStateMachine> visited)
        {
            if (sm == null || !visited.Add(sm)) return true;

            // ルートのトランジション(Any State / Entry)
            foreach (var t in sm.anyStateTransitions)
            {
                if (!IsTransitionOnlyUsedByParams(t, removedParams)) return false;
            }
            foreach (var t in sm.entryTransitions)
            {
                if (!IsTransitionOnlyUsedByParams(t, removedParams)) return false;
            }

            // 各ステートのチェック
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state == null) continue;

                // StateMachineBehaviour が付いているステートは副作用があるため保持
                if (state.behaviours != null && state.behaviours.Length > 0) return false;

                foreach (var t in state.transitions)
                {
                    if (!IsTransitionOnlyUsedByParams(t, removedParams)) return false;
                }
            }

            // ネストされたサブステートマシン
            foreach (var childSm in sm.stateMachines)
            {
                if (!IsStateMachineOnlyUsedByParams(childSm.stateMachine, removedParams, visited))
                    return false;
            }

            return true;
        }

        private static bool IsTransitionOnlyUsedByParams(AnimatorTransitionBase t, HashSet<string> removedParams)
        {
            if (t == null) return true;

            // 条件がないトランジションは無条件遷移 → 保持すべきなので false
            if (t.conditions.Length == 0) return false;

            // 全条件が removedParams に含まれているか確認
            return t.conditions.All(c => removedParams.Contains(c.parameter));
        }

        /// <summary>
        /// Descriptor の baseAnimationLayers / specialAnimationLayers のうち
        /// originalController を参照しているものを newController に差し替える。
        /// </summary>
        private static void ReplaceController(
            VRCAvatarDescriptor descriptor, AnimatorController original, AnimatorController replacement)
        {
            var baseLayers = descriptor.baseAnimationLayers;
            for (int i = 0; i < baseLayers.Length; i++)
            {
                if (baseLayers[i].animatorController == original)
                    baseLayers[i].animatorController = replacement;
            }
            descriptor.baseAnimationLayers = baseLayers;

            var specialLayers = descriptor.specialAnimationLayers;
            for (int i = 0; i < specialLayers.Length; i++)
            {
                if (specialLayers[i].animatorController == original)
                    specialLayers[i].animatorController = replacement;
            }
            descriptor.specialAnimationLayers = specialLayers;
        }

        // ---------------------------------------------------------------
        // メニュー処理
        // ---------------------------------------------------------------

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