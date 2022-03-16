using UnityEngine;


public interface IUnlockable
{
	public GameObject Parent { get; set; }


	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms);

	public bool IsKey(GameObject obj);

	public void Unlock(GameObject key);
}
