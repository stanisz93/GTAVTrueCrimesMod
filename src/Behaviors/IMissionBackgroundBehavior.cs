namespace GTAVTrueCrimesMod.Behaviors
{
    public interface IMissionBackgroundBehavior
    {
        string Id { get; }
        string DebugText { get; }
        void Tick(MissionRuntime runtime);
        void Clear();
    }
}
