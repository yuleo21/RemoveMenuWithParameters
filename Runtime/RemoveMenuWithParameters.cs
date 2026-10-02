using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace RemoveMenuWithParametersTool
{
    /// <summary>
    /// Avatar Descriptor と同じ GameObject に付けて使用する。
    /// チェックしたメニュー項目と、その項目(サブメニューの場合は配下すべて)が操作する
    /// パラメーターをビルド時に削除する。コンポーネント自身もビルド時に削除される。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Remove Menu with Parameters")]
    public class RemoveMenuWithParameters : MonoBehaviour, IEditorOnly
    {
        /// <summary>
        /// 削除対象のメニュー項目。ルートメニューからのインデックス経路 ("2" / "2/0/3" など)。
        /// </summary>
        public List<string> removePaths = new List<string>();

        /// <summary>
        /// 削除されずに残るメニュー項目が使っているパラメーターは削除しない。
        /// </summary>
        public bool keepSharedParameters = true;

        /// <summary>
        /// 削除されたパラメーターのみで駆動されている Playable Layer のレイヤーを削除する。
        /// </summary>
        public bool removeUnusedLayers = true;
    }
}