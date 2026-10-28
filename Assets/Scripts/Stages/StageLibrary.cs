using UnityEngine;
using BeefCity.Core;

namespace BeefCity.Stages
{
    /// <summary>
    /// Registers the 22 stages (plus the Proving Ground test arena) when the game starts, before any scene loads.
    /// Add a stage by writing a builder that returns an ArenaInfo and registering it here.
    /// </summary>
    public static class StageLibrary
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            StageRegistry.Register("gondola", "Gondola", StagesA.Gondola);
            StageRegistry.Register("grind", "Grind", StagesA.Grind);
            StageRegistry.Register("incinerator", "Incinerator", StagesA.Incinerator);
            StageRegistry.Register("trucks", "Trucks", StagesA.Trucks);
            StageRegistry.Register("containers", "Containers", StagesA.Containers);
            StageRegistry.Register("wheel", "Wheel", StagesA.Wheel);
            StageRegistry.Register("fans", "Fans", StagesA.Fans);
            StageRegistry.Register("chutes", "Chutes", StagesA.Chutes);

            StageRegistry.Register("tower", "Tower", StagesB.Tower);
            StageRegistry.Register("billboards", "Billboards", StagesB.Billboards);
            StageRegistry.Register("buoy", "Buoy", StagesB.Buoy);
            StageRegistry.Register("blimp", "Blimp", StagesB.Blimp);
            StageRegistry.Register("girders", "Girders", StagesB.Girders);
            StageRegistry.Register("subway", "Subway", StagesB.Subway);
            StageRegistry.Register("ring", "Ring", StagesB.Ring);
            StageRegistry.Register("roof", "Roof", StagesB.Roof);

            StageRegistry.Register("crane", "Crane", StagesC.Crane);
            StageRegistry.Register("presses", "Presses", StagesC.Presses);
            StageRegistry.Register("carousel", "Carousel", StagesC.Carousel);
            StageRegistry.Register("elevator", "Elevator", StagesC.Elevator);
            StageRegistry.Register("pier", "Pier", StagesC.Pier);
            StageRegistry.Register("parking", "Parking Garage", StagesC.Parking);
        }
    }
}
