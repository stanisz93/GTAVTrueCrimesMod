namespace GTAVTrueCrimesMod.Behaviors
{
    public interface IScriptedMissionKillTarget
    {
        bool BeginScriptedKillByOther(int shotCount, int shotGapMs, int damage);
        bool IsNearPosition(GTA.Math.Vector3 position, float distance);
    }
}
