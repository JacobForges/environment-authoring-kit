namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Implemented by game enemy scripts; cave spawners call Configure after instantiate.</summary>
    public interface ICaveSpawnedEnemy
    {
        void Configure(int seed);
    }
}
