using UnityEngine;
using UnityEngine.UI;
using BeefCity.Controls;
using BeefCity.Core;

namespace BeefCity.UI
{
    public enum ScreenId { None, Splash, Main, Settings, Costume, Lobby, Loading }

    /// <summary>A full-screen menu page built from code. Plain C# class (not a MonoBehaviour); GameBootstrap drives it.</summary>
    public abstract class MenuScreen
    {
        public RectTransform Root { get; private set; }
        protected GameBootstrap Game { get; private set; }
        protected abstract string Title { get; }
        protected abstract void Build(RectTransform root);

        public virtual void OnShow() { }
        public virtual void OnHide() { }
        public virtual void Tick(float dt) { }

        public void Init(GameBootstrap game, Transform canvas)
        {
            Game = game;
            Root = UIKit.NewRect(Title + "Screen", canvas);
            UIKit.Stretch(Root);
            Build(Root);
            Root.gameObject.SetActive(false);
        }

        public void SetVisible(bool visible)
        {
            if (Root.gameObject.activeSelf == visible) return;
            Root.gameObject.SetActive(visible);
            if (visible) OnShow();
            else OnHide();
        }
    }

    // ---------------------------------------------------------------------- splash

    public sealed class SplashScreen : MenuScreen
    {
        const float Total = 3f;
        CanvasGroup _group;
        float _t;

        protected override string Title => "Splash";

        protected override void Build(RectTransform root)
        {
            RectTransform bg = UIKit.AddPanel(root, "Bg", Color.black);
            UIKit.Stretch(bg);

            RectTransform center = UIKit.NewRect("Center", root);
            UIKit.Stretch(center);
            UIKit.Column(center, 10f, 0, TextAnchor.MiddleCenter);
            UIKit.AddLabel(center, "BEEF CITY BRAWLERS", 96, TextAnchor.MiddleCenter, UIKit.Accent, 140f);
            UIKit.AddLabel(center, "a very wobbly production", 32, TextAnchor.MiddleCenter, UIKit.Dim, 50f);

            _group = root.gameObject.AddComponent<CanvasGroup>();
        }

        public override void OnShow()
        {
            _t = 0f;
            _group.alpha = 0f;
        }

        public override void Tick(float dt)
        {
            _t += dt;
            float alpha = _t < 0.8f ? _t / 0.8f : (_t > Total - 0.8f ? Mathf.Clamp01((Total - _t) / 0.8f) : 1f);
            _group.alpha = alpha;
            if ((_t > 0.4f && UIKit.AnyPressed()) || _t >= Total) Game.Show(ScreenId.Main);
        }
    }

    // ---------------------------------------------------------------------- main menu

    public sealed class MainMenuScreen : MenuScreen
    {
        protected override string Title => "MainMenu";

        protected override void Build(RectTransform root)
        {
            RectTransform side = UIKit.AddPanel(root, "Side", UIKit.PanelColor);
            UIKit.Anchor(side, new Vector2(0f, 0f), new Vector2(0.34f, 1f));
            UIKit.Column(side, 18f, 40, TextAnchor.MiddleCenter);

            UIKit.AddLabel(side, "BEEF CITY\nBRAWLERS", 64, TextAnchor.MiddleCenter, UIKit.Accent, 190f);
            UIKit.AddSpacer(side, 30f);
            UIKit.AddButton(side, "PLAY", () => Game.OpenCostume(ScreenId.Lobby, true), 460f, 78f, 38);
            UIKit.AddButton(side, "CUSTOMIZE", () => Game.OpenCostume(ScreenId.Main, false), 460f, 78f, 38);
            UIKit.AddButton(side, "SETTINGS", () => Game.Show(ScreenId.Settings), 460f, 78f, 38);
            UIKit.AddButton(side, "QUIT", Game.Quit, 460f, 78f, 38);
            UIKit.AddSpacer(side, 20f);
            UIKit.AddLabel(side, "v" + Application.version, 20, TextAnchor.MiddleCenter, UIKit.Dim, 30f);
        }
    }

    // ---------------------------------------------------------------------- settings

    public sealed class SettingsScreen : MenuScreen
    {
        Slider _volume;
        Button _fullscreen;
        CyclerUI _quality;

        protected override string Title => "Settings";

        protected override void Build(RectTransform root)
        {
            RectTransform panel = UIKit.AddPanel(root, "Panel", UIKit.PanelColor);
            UIKit.Anchor(panel, new Vector2(0.28f, 0.14f), new Vector2(0.72f, 0.86f));
            UIKit.Column(panel, 16f, 30, TextAnchor.UpperCenter);

            UIKit.AddLabel(panel, "SETTINGS", 54, TextAnchor.MiddleCenter, UIKit.Accent, 80f);
            UIKit.AddSpacer(panel, 10f);

            _volume = UIKit.AddSlider(panel, "Volume", 0f, 1f, 1f, v => AudioListener.volume = v);
            _fullscreen = UIKit.AddButton(panel, "Fullscreen", () =>
            {
                Screen.fullScreen = !Screen.fullScreen;
                UIKit.SetButtonText(_fullscreen, "Fullscreen: " + (Screen.fullScreen ? "ON" : "OFF"));
            }, 640f);
            string[] levels = QualitySettings.names;
            _quality = UIKit.AddCycler(panel, "Quality", levels, QualitySettings.GetQualityLevel(), i => QualitySettings.SetQualityLevel(i, true));
            UIKit.AddButton(panel, "CONTROLS...", () =>
            {
                if (RebindingMenu.Instance != null) RebindingMenu.Instance.Open();
            }, 640f);
            UIKit.AddSpacer(panel, 10f);
            UIKit.AddButton(panel, "BACK", () =>
            {
                SaveData s = SaveSystem.Data;
                s.masterVolume = _volume.value;
                s.fullscreen = Screen.fullScreen;
                s.quality = QualitySettings.GetQualityLevel();
                SaveSystem.Save();
                Game.Show(ScreenId.Main);
            }, 640f);
        }

        public override void OnShow()
        {
            SaveData s = SaveSystem.Data;
            _volume.SetValueWithoutNotify(s.masterVolume);
            UIKit.SetButtonText(_fullscreen, "Fullscreen: " + (Screen.fullScreen ? "ON" : "OFF"));
            _quality.SetIndex(QualitySettings.GetQualityLevel(), false);
        }
    }
}
