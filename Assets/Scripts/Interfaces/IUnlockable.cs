using UnityEngine;


public interface IUnlockable
{
	public GameObject Parent { get; set; }


	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms);

	public bool IsValidNextKey(GameObject obj);

	public bool Unlock(IKey key);
}
