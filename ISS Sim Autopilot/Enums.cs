namespace ISS_Sim_Autopilot
{
    public partial class Autopilot
    {
        private enum Direction { None, xPos, xNeg, yPos, yNeg, zPos, zNeg, PitchUp, PitchDown, YawRight, YawLeft, RollRight, RollLeft };
        private enum AutopilotPhase { Waiting, FixingRoll, FixingDirection, Approaching, Stop, Docking, Docked }
    }
}