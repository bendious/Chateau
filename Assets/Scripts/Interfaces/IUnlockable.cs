using UnityEngine;


public interface IUnlockable
{
	public Component Component => this as Component;

	public GameObject Parent { get; set; }
	public bool IsLocked { get; }

	public bool IsCriticalPath { set; }


	public void SpawnKeysStatic(RoomController lockRoom, RoomController[] keyRooms, float difficultyPct);
	public void SpawnKeysDynamic(RoomController lockRoom, RoomController[] keyRooms, float difficultyPct);

	public bool IsValidNextKey(GameObject obj);

	public bool Unlock(IKey key, bool silent = false);
}
