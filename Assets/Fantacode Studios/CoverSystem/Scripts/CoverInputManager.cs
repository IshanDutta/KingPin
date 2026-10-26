using FS_Core;
using UnityEngine;

namespace FS_CoverSystem
{
    public class CoverInputManager : FSInputManagerBase
    {
        [SerializeField] CoverUI coverUI;
        [SerializeField] FSInput coverInput = new FSInput(KeyCode.G);

        public bool CoverPressed { get; private set; }

        public void SetCoverUI(CoverUI ui)
        {
            coverUI = ui;
            CacheUIInputs();
        }

        protected override void CacheUIInputs()
        {
            if (coverUI == null) return;

            coverUI.CacheButtons();
            coverInput.SetUIButton(coverUI.CoverUIButton);
        }

        void Update()
        {
#if inputsystem
            CoverPressed = ReadButtonDown(coverInput, () => input.Combat.Cover.WasPerformedThisFrame());
#else
            CoverPressed = ReadButtonDown(coverInput);
#endif
        }
    }
}