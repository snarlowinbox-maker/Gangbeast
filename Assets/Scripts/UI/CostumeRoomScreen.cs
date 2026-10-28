using UnityEngine;
using UnityEngine.UI;
using BeefCity.Core;
using BeefCity.Customization;
using BeefCity.Ragdoll;

namespace BeefCity.UI
{
    /// <summary>
    /// Costume room: the 3D character turns slowly in the middle of the screen while the left panel changes
    /// body color, accent color and primitive accessories (applied through MaterialPropertyBlocks).
    /// </summary>
    public sealed class CostumeRoomScreen : MenuScreen
    {
        CostumeData _c = CostumeData.CreateDefault();
        ScreenId _next = ScreenId.Main;
        RagdollRig _preview;
        SwatchUI[] _body, _accent;
        CyclerUI _hat, _nose, _extra;
        InputField _name;
        Button _continue;

        protected override string Title => "CostumeRoom";

        public void SetNext(ScreenId next, bool fromPlay)
        {
            _next = next;
            UIKit.SetButtonText(_continue, fromPlay ? "SAVE & CONTINUE" : "SAVE");
        }

        protected override void Build(RectTransform root)
        {
            RectTransform panel = UIKit.AddPanel(root, "Panel", UIKit.PanelColor);
            UIKit.Anchor(panel, new Vector2(0f, 0f), new Vector2(0.34f, 1f));
            UIKit.Column(panel, 10f, 28, TextAnchor.UpperCenter);

            UIKit.AddLabel(panel, "COSTUME ROOM", 46, TextAnchor.MiddleCenter, UIKit.Accent, 64f);
            _name = UIKit.AddInput(panel, "Player", "Your name", 16, 520f, 52f);

            UIKit.AddLabel(panel, "Body color", 24, TextAnchor.MiddleLeft, UIKit.Dim, 28f);
            RectTransform bodyGrid = UIKit.AddGrid(panel, 52f, 8f, 6, 112f);
            _body = new SwatchUI[CostumeData.BodyPalette.Length];
            for (int i = 0; i < _body.Length; i++)
            {
                int idx = i;
                _body[i] = UIKit.AddSwatch(bodyGrid, CostumeData.BodyPalette[i], () =>
                {
                    _c.bodyColor = CostumeData.BodyPalette[idx];
                    Refresh();
                });
            }

            UIKit.AddLabel(panel, "Accent color", 24, TextAnchor.MiddleLeft, UIKit.Dim, 28f);
            RectTransform accentGrid = UIKit.AddGrid(panel, 52f, 8f, 8, 60f);
            _accent = new SwatchUI[CostumeData.AccentPalette.Length];
            for (int i = 0; i < _accent.Length; i++)
            {
                int idx = i;
                _accent[i] = UIKit.AddSwatch(accentGrid, CostumeData.AccentPalette[i], () =>
                {
                    _c.accentColor = CostumeData.AccentPalette[idx];
                    Refresh();
                });
            }

            _hat = UIKit.AddCycler(panel, "Hat", CostumeData.Hats, 0, i => { _c.hat = i; Refresh(); });
            _nose = UIKit.AddCycler(panel, "Nose", CostumeData.Noses, 0, i => { _c.nose = i; Refresh(); });
            _extra = UIKit.AddCycler(panel, "Extra", CostumeData.Extras, 0, i => { _c.extra = i; Refresh(); });

            UIKit.AddSpacer(panel, 6f);
            UIKit.AddButton(panel, "RANDOMIZE", () =>
            {
                _c = CostumeData.CreateRandom(Random.Range(0, int.MaxValue));
                SyncCyclers();
                Refresh();
            }, 520f, 54f, 26);

            RectTransform row = UIKit.AddRow(panel, 64f, 12f);
            UIKit.AddButton(row, "BACK", () => Game.Show(ScreenId.Main), 160f, 64f, 26);
            _continue = UIKit.AddButton(row, "SAVE", () =>
            {
                Save();
                Game.Show(_next);
            }, 340f, 64f, 26);
        }

        public override void OnShow()
        {
            SaveData s = SaveSystem.Data;
            _c = s.costume.Clone();
            _name.text = s.playerName;
            SyncCyclers();
            _preview = Game.ShowPreview(true);
            Refresh();
        }

        public override void OnHide()
        {
            Game.ShowPreview(false);
        }

        public override void Tick(float dt)
        {
            if (_preview != null) _preview.transform.Rotate(Vector3.up, 35f * dt, Space.World);
        }

        void SyncCyclers()
        {
            _hat.SetIndex(_c.hat, false);
            _nose.SetIndex(_c.nose, false);
            _extra.SetIndex(_c.extra, false);
        }

        void Refresh()
        {
            int b = CostumeData.IndexOf(CostumeData.BodyPalette, _c.bodyColor);
            int a = CostumeData.IndexOf(CostumeData.AccentPalette, _c.accentColor);
            for (int i = 0; i < _body.Length; i++) _body[i].SetSelected(i == b);
            for (int i = 0; i < _accent.Length; i++) _accent[i].SetSelected(i == a);
            if (_preview != null) CostumeBuilder.Apply(_preview, _c);
        }

        void Save()
        {
            SaveData s = SaveSystem.Data;
            s.costume = _c;
            s.playerName = string.IsNullOrWhiteSpace(_name.text) ? "Player" : _name.text.Trim();
            SaveSystem.Save();
        }
    }
}
