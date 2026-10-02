using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDKBase;

namespace RemoveMenuWithParametersTool
{
    /// <summary>
    /// アバター上でメニュー以外から操作されているパラメーター名と、
    /// それを使っている場所(Contact Receiver / Blend Tree / Parameter Driver)を収集するユーティリティ。
    /// </summary>
    internal static class ExternalParamUtil
    {
        /// <summary>
        /// アバタールートオブジェクト配下と AnimatorController を走査し、
        /// 「パラメーター名 → 使用箇所の説明一覧」を返す。
        /// </summary>
        public static Dictionary<string, List<string>> Collect(GameObject avatarRoot,
            IEnumerable<AnimatorController> controllers)
        {
            var result = new Dictionary<string, List<string>>();

            CollectContact(avatarRoot, result);

            if (controllers != null)
            {
                foreach (var ctrl in controllers.Distinct())
                {
                    if (ctrl == null) continue;
                    foreach (var layer in ctrl.layers)
                    {
                        if (layer.stateMachine == null) continue;
                        string where = ctrl.name + " / " + layer.name;
                        CollectInStateMachine(layer.stateMachine, where, result,
                            new HashSet<AnimatorStateMachine>());
                    }
                }
            }

            return result;
        }

        /// <summary>使用箇所を「A / B / C 他N件」の形式にまとめる。</summary>
        public static string FormatUsers(List<string> users, int max = 3)
        {
            if (users == null || users.Count == 0) return "";
            if (users.Count <= max) return string.Join(" / ", users);
            return string.Join(" / ", users.Take(max)) + $" 他{users.Count - max}件";
        }

        private static void Add(Dictionary<string, List<string>> result, string param, string user)
        {
            if (string.IsNullOrEmpty(param)) return;
            if (!result.TryGetValue(param, out var list))
            {
                list = new List<string>();
                result[param] = list;
            }
            if (!list.Contains(user)) list.Add(user);
        }

        // ---------------------------------------------------------------
        // VRC Contact Receiver
        // ---------------------------------------------------------------

        private static void CollectContact(GameObject root, Dictionary<string, List<string>> result)
        {
            foreach (var cr in root.GetComponentsInChildren<VRCContactReceiver>(true))
            {
                if (string.IsNullOrEmpty(cr.parameter)) continue;
                Add(result, cr.parameter, "Contact Receiver: " + GetPath(cr.transform, root.transform));
            }
        }

        private static string GetPath(Transform t, Transform root)
        {
            if (t == root) return t.name;
            var names = new List<string>();
            for (var cur = t; cur != null && cur != root; cur = cur.parent)
                names.Add(cur.name);
            names.Reverse();
            return string.Join("/", names);
        }

        // ---------------------------------------------------------------
        // Animator: Blend Tree / Parameter Driver
        // ---------------------------------------------------------------

        private static void CollectInStateMachine(AnimatorStateMachine sm, string where,
            Dictionary<string, List<string>> result, HashSet<AnimatorStateMachine> visited)
        {
            if (sm == null || !visited.Add(sm)) return;

            // ステートマシン自体に付いた Parameter Driver
            CollectDrivers(sm.behaviours, where + " / " + sm.name + " (StateMachine)", result);

            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state == null) continue;
                string stateWhere = where + " / " + state.name;

                CollectDrivers(state.behaviours, stateWhere, result);

                if (state.motion is BlendTree bt)
                    CollectBlendTreeParams(bt, stateWhere, result, new HashSet<BlendTree>());
            }

            foreach (var childSm in sm.stateMachines)
                CollectInStateMachine(childSm.stateMachine, where, result, visited);
        }

        private static void CollectDrivers(StateMachineBehaviour[] behaviours, string where,
            Dictionary<string, List<string>> result)
        {
            if (behaviours == null) return;
            foreach (var driver in behaviours.OfType<VRCAvatarParameterDriver>())
            {
                if (driver.parameters == null) continue;
                foreach (var p in driver.parameters)
                {
                    if (p == null) continue;

                    // 書き込み先
                    Add(result, p.name, "Parameter Driver: " + where);

                    // Copy は参照元も使用する
                    if (p.type == VRC_AvatarParameterDriver.ChangeType.Copy)
                        Add(result, p.source, "Parameter Driver (Copy元): " + where);
                }
            }
        }

        private static void CollectBlendTreeParams(BlendTree bt, string where,
            Dictionary<string, List<string>> result, HashSet<BlendTree> visited)
        {
            if (bt == null || !visited.Add(bt)) return;
            string user = "Blend Tree: " + where;
            Add(result, bt.blendParameter, user);
            Add(result, bt.blendParameterY, user);
            foreach (var child in bt.children)
            {
                if (child.motion is BlendTree childBt)
                    CollectBlendTreeParams(childBt, where, result, visited);
            }
        }
    }
}