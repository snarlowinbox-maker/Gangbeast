using UnityEngine;
using UnityEngine.UI;

namespace BeefCity.UI
{
    public sealed class LoadingScreen : MenuScreen
    {
        static readonly string[] Tips =
        {
            "Hold both grab buttons on a ledge, then jump to climb.",
            "Hold the headbutt button to crouch and charge, release to unleash.",
            "Grab a foe with both hands, press taunt to lift, aim, then let go to throw.",
            "Nobody has a health bar. The edge of the map is the only enemy.",
            "Run, then punch for a super punch.",
            "Slide tackles hit hard but leave you on the floor."
        };

        ProgressBarUI _bar;
        Text _status, _tip;
        float _tipT;
        int _tipIndex;

        protected override string Title => "Loading";

        protected override void Build(RectTransform root)
        {
            RectTransform bg = UIKit.AddPanel(root, "Bg", new Color(0.04f, 0.05f, 0.08f, 1f));
            UIKit.Stretch(bg);

            RectTransform center = UIKit.NewRect("Center", root);
            UIKit.Stretch(center);
            UIKit.Column(center, 22f, 0, TextAnchor.MiddleCenter);
            UIKit.AddLabel(center, "LOADING", 72, TextAnchor.MiddleCenter, UIKit.Accent, 100f);
            _bar = UIKit.AddProgressBar(center, 900f, 44f);
            _status = UIKit.AddLabel(center, "", 26, TextAnchor.MiddleCenter, UIKit.Dim, 40f);
            UIKit.AddSpacer(center, 30f);
            _tip = UIKit.AddLabel(center, "", 28, TextAnchor.MiddleCenter, UIKit.TextColor, 80f);
            UIKit.Layout(_tip.rectTransform, 1100f, 80f);
        }

        public override void OnShow()
        {
            _bar.SetValue(0f);
            _status.text = "";
            _tipIndex = Random.Range(0, Tips.Length);
            _tipT = 0f;
            _tip.text = Tips[_tipIndex];
        }

        public void SetProgress(float value) => _bar.SetValue(value);
        public void SetStatus(string text) { if (_status != null) _status.text = text; }

        public override void Tick(float dt)
        {
            _tipT += dt;
            if (_tipT < 3.5f) return;
            _tipT = 0f;
            _tipIndex = (_tipIndex + 1) % Tips.Length;
            _tip.text = Tips[_tipIndex];
        }
    }
}
