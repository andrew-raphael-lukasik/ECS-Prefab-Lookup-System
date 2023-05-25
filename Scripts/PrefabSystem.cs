using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;

#if ENABLE_NETWORK
[WorldSystemFilter( WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation )]
#endif
[UpdateInGroup( typeof(InitializationSystemGroup) )]
[RequireMatchingQueriesForUpdate]
public partial struct PrefabSystem : ISystem
{

	//[Unity.Burst.BurstCompile]
	public void OnCreate ( ref SystemState state )
	{
		state.RequireForUpdate<RequestPrefabPoolRegistration>();
			
		Entity singleton = state.EntityManager.CreateEntity( typeof(Lookup) );
		SystemAPI.SetSingleton( new Lookup{
			Prefabs = new NativeHashMap<FixedString64Bytes,Entity>( 128 , Allocator.Persistent )
		} );
	}

	[Unity.Burst.BurstCompile]
	public void OnDestroy ( ref SystemState state )
	{
		if( SystemAPI.TryGetSingletonEntity<Lookup>(out Entity singleton) )
			state.EntityManager.DestroyEntity( singleton );
	}

	//[Unity.Burst.BurstCompile]
	public void OnUpdate ( ref SystemState state )
	{
		var prefabs = SystemAPI.GetSingleton<Lookup>().Prefabs;
		int numBefore = prefabs.Count;
		{
			var list = new NativeList<Entity>( 1 , Allocator.Temp );
			foreach( var (buffer,entity) in SystemAPI.Query<DynamicBuffer<RequestPrefabPoolRegistration>>().WithEntityAccess() )
			{
				foreach( var item in buffer )
					prefabs.TryAdd( item.ID , item.Prefab );
				list.Add( entity );
			}
			state.EntityManager.DestroyEntity( list.AsArray() );
		}
		int numAfter = prefabs.Count;
		UnityEngine.Debug.Log($"<color=cyan>{state.World.Name} {GetType().Name}</color>: new prefab data consumed, {numAfter-numBefore} prefabs added");
	}

	public struct RequestPrefabPoolRegistration : IBufferElementData
	{
		public FixedString64Bytes ID;
		public Entity Prefab;
	}

	public struct RequestPrefabRegistration : IComponentData
	{
		public FixedString64Bytes PrefabID;
	}

	public struct PrefabSystemID : ICleanupComponentData
	{
		public FixedString64Bytes Value;
	}

	public struct Lookup : IComponentData
	{
		public NativeHashMap<FixedString64Bytes,Entity> Prefabs;
		public JobHandle Dependency;
	}

	[RequireMatchingQueriesForUpdate]
	public partial struct PrefabDeregistrationSystem : ISystem
	{
		[Unity.Burst.BurstCompile]
		public void OnCreate ( ref SystemState state ) {}

		[Unity.Burst.BurstCompile]
		public void OnDestroy ( ref SystemState state ) {}

		[Unity.Burst.BurstCompile]
		public void OnUpdate ( ref SystemState state )
		{
			var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer( state.WorldUnmanaged );
			var lookup = SystemAPI.GetSingleton<Lookup>();
			
			state.Dependency = new UnregisterPrefabJob
			{
				ECB = ecb ,
				Prefabs = lookup.Prefabs ,
			}.Schedule( JobHandle.CombineDependencies(state.Dependency,lookup.Dependency) );
			lookup.Dependency = state.Dependency;
		}
	}

	[Unity.Burst.BurstCompile]
	[WithAll( typeof(Prefab) )]
	[WithNone( typeof(PrefabSystemID) )]
	partial struct RegisterPrefabJob : IJobEntity
	{
		public EntityCommandBuffer ECB;
		public NativeHashMap<FixedString64Bytes,Entity> Prefabs;
		public void Execute ( in Entity entity , in RequestPrefabRegistration request )
		{
			Prefabs.Add( request.PrefabID , entity );
			ECB.AddComponent( entity , new PrefabSystemID{
				Value = request.PrefabID
			} );
		}
	}

	[Unity.Burst.BurstCompile]
	[WithNone( typeof(Prefab) )]
	partial struct UnregisterPrefabJob : IJobEntity
	{
		public EntityCommandBuffer ECB;
		public NativeHashMap<FixedString64Bytes,Entity> Prefabs;
		public void Execute ( in Entity entity , in PrefabSystemID prefabID )
		{
			Prefabs.Remove( prefabID.Value );
			ECB.DestroyEntity( entity );
		}
	}

}