using UnityEngine;


public interface IUnlockable
{
	public GameObject Parent { get; set; }
	public bool IsLocked { get; }


	public void SpawnKeysStatic(RoomController lockRoom, RoomController[] keyRooms);
	public void SpawnKeysDynamic(RoomController lockRoom, RoomController[] keyRooms);

	public bool IsValidNextKey(GameObject obj);

	public bool Unlock(IKey key);
}
