namespace BeefCity.Ragdoll
{
    public enum MoveButton { Jump, Kick, Headbutt, Crouch, Taunt, PunchL, PunchR }

    /// <summary>Snapshot of what the character is doing when a button is pressed.</summary>
    public struct ComboContext
    {
        public bool Grounded;
        public bool Crouching;
        public bool ArmsHoldStatic;   // both hands anchored to scenery (a ledge)
        public bool ArmsHoldBody;     // both hands holding something movable (a foe, a prop)
        public bool ArmsReaching;     // both grab buttons held with nothing grabbed yet
        public float Speed;           // horizontal speed in m/s
        public float HeadbuttCharge;  // 0..1, only meaningful when the headbutt button is released
    }

    /// <summary>
    /// Maps "button + situation" to an advanced move.
    /// !! PLACEHOLDER TABLE !! These combinations are my best guess, not your reference guide.
    /// Edit TryResolve (one switch) to match the guide; nothing else needs to change.
    /// Returning MoveId.None means "do the basic action" (jump, punch, kick, headbutt).
    /// </summary>
    public static class MoveCombos
    {
        public const float RunSpeed = 3.5f;
        public const float SlideSpeed = 1.5f;

        public static bool TryResolve(MoveButton button, in ComboContext c, out MoveId id)
        {
            id = MoveId.None;
            switch (button)
            {
                case MoveButton.Jump:
                    if (c.ArmsHoldStatic) id = c.Grounded ? MoveId.LeapUpClimb : MoveId.SwingUpClimb;
                    else if (c.Crouching && c.Grounded) id = MoveId.Backflip;
                    break;

                case MoveButton.Kick:
                    if (!c.Grounded)
                    {
                        if (c.Crouching) id = MoveId.FlipKick;
                        else if (c.Speed > RunSpeed) id = MoveId.SuperDropKick;
                        else id = MoveId.FlyingDropKick;
                    }
                    else if (c.Crouching)
                    {
                        id = c.Speed > SlideSpeed ? MoveId.SlideTackle : MoveId.StandingDropKick;
                    }
                    break;

                case MoveButton.Headbutt: // evaluated when the button is released
                    if (!c.Grounded) id = MoveId.BodySlam;
                    else if (c.HeadbuttCharge >= 1f) id = MoveId.KnockoutHeadbutt;
                    else if (c.HeadbuttCharge >= 0.5f) id = MoveId.ChargedHeadbutt;
                    break;

                case MoveButton.Crouch: // evaluated when the button is pressed
                    if (c.ArmsReaching && c.Grounded) id = MoveId.Handstand;
                    else if (c.Grounded && c.Speed > RunSpeed) id = MoveId.Powerslide;
                    else if (c.Grounded && c.Speed > SlideSpeed) id = MoveId.DropSlide;
                    break;

                case MoveButton.Taunt:
                    if (c.ArmsHoldBody) id = MoveId.Lift;                       // Lift, then move + release = Throwing Foes
                    else if (c.Crouching && c.Grounded && c.Speed < 0.5f) id = MoveId.Sit;
                    else id = MoveId.Taunt;
                    break;

                case MoveButton.PunchL:
                case MoveButton.PunchR:
                    if (c.Speed > RunSpeed) id = MoveId.SuperPunch;
                    break;
            }
            return id != MoveId.None;
        }
    }
}
