using FS_Core;
using UnityEngine;

namespace FS_CoverSystem
{
    public class CoverUI : FSInputUIBase
    {
        [Header("UI Buttons")]
        [SerializeField] UIImageInteraction coverUIButton;

        public UIImageInteraction CoverUIButton => coverUIButton;

        public override void CacheButtons()
        {
            coverUIButton ??= FindButton("Cover", "Take Cover");
        }
    }
}